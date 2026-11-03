using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// Plans plc_write items from public reads only, each at most once per call: the tag inventory
/// (which also resolves the PLC), the project tree of a PLC only when one of its items needs it
/// (<see cref="PlcWorkingState.NeedsTree"/>) and one export per content item. Earlier items are
/// applied to a working state so later items see them and carry <c>DependsOn</c>; only those
/// dependent items are re-planned before their mutation. The shared runner owns sequencing and mutation.
/// </summary>
public sealed class PlcWritePlanner(OpennessWorkerClient client)
{
    internal sealed record Evidence(PlcTagInventoryInfo? Inventory, WriteToolError? Error);

    private sealed record ItemOutcome(PlcWorkingState? State, PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards,
        IReadOnlyList<CheckedPrecondition> Preconditions, WriteToolError? Error);

    /// <summary>The planning reads of one call: the inventory, each PLC's working state and which PLCs need a tree.</summary>
    private sealed class CallReads(PlcTagInventoryInfo inventory, IReadOnlyList<PlcOperationRequest> items)
    {
        public PlcTagInventoryInfo Inventory { get; } = inventory;

        public Dictionary<PlcTagInventoryPlcInfo, PlcWorkingState> States { get; } = new(ReferenceEqualityComparer.Instance);

        public HashSet<PlcTagInventoryPlcInfo> TreePlcs { get; } = new(items.Where(PlcWorkingState.NeedsTree)
            .Select(item => MatchPlc(item, inventory).Plc).OfType<PlcTagInventoryPlcInfo>(), ReferenceEqualityComparer.Instance);

        /// <summary>The planned document of each block or type an earlier interface edit of this pass changed.</summary>
        public Dictionary<string, (string Content, string OperationId)> PlannedDocuments { get; } = new(StringComparer.Ordinal);

        public HashSet<PlcTagInventoryPlcInfo> WatchPlcs { get; } = new(items.Where(PlcWorkingState.NeedsWatchTables)
            .Select(item => MatchPlc(item, inventory).Plc).OfType<PlcTagInventoryPlcInfo>(), ReferenceEqualityComparer.Instance);

        public HashSet<PlcTagInventoryPlcInfo> SourcePlcs { get; } = new(items.Where(PlcWorkingState.NeedsExternalSources)
            .Select(item => MatchPlc(item, inventory).Plc).OfType<PlcTagInventoryPlcInfo>(), ReferenceEqualityComparer.Instance);

        public HashSet<PlcTagInventoryPlcInfo> TextListPlcs { get; } = new(items.Where(PlcWorkingState.NeedsTextLists)
            .Select(item => MatchPlc(item, inventory).Plc).OfType<PlcTagInventoryPlcInfo>(), ReferenceEqualityComparer.Instance);

        public HashSet<PlcTagInventoryPlcInfo> TechnologyPlcs { get; } = new(items.Where(PlcWorkingState.NeedsTechnologyObjects)
            .Select(item => MatchPlc(item, inventory).Plc).OfType<PlcTagInventoryPlcInfo>(), ReferenceEqualityComparer.Instance);

        /// <summary>The fail-safe access state of the pass (read lazily, only for fail-safe items and F-block changes).</summary>
        public PlcSafetyPlanning Safety { get; init; } = null!;
    }

    private readonly Dictionary<string, PlcPlannedContent> _planned = new(StringComparer.Ordinal);

    /// <summary>The document an interface edit imports and the hash of the export it was computed from.</summary>
    internal bool TryGetPlanned(string operationId, out PlcPlannedContent planned) => _planned.TryGetValue(operationId, out planned!);

    internal IReadOnlyDictionary<string, PlcPlannedContent> Planned => _planned;

    private readonly Dictionary<string, string> _unplanned = new(StringComparer.Ordinal);

    /// <summary>
    /// Why a granular edit has no planned document (its plc_state_unverifiable reason, e.g. an export without SimaticML of a
    /// block not compiled since it was created, K55), or null when it has one or none is known.
    /// </summary>
    internal string? UnplannedReason(string operationId) => _unplanned.GetValueOrDefault(operationId);

    private void Remember(PlcOperationRequest item, PlcPlannedContent? planned, IReadOnlyList<FiredGuard> guards)
    {
        if (planned is not null)
        {
            _planned[item.OperationId] = planned;
            _unplanned.Remove(item.OperationId);
            return;
        }

        _planned.Remove(item.OperationId);
        if (guards.LastOrDefault(g => g.Id == PlcGuardDefinitions.StateUnverifiable && g.OperationId == item.OperationId) is { } reason)
            _unplanned[item.OperationId] = reason.Message;
        else
            _unplanned.Remove(item.OperationId);
    }

    private readonly Dictionary<string, PlcV21PitfallPlan> _pitfalls = new(StringComparer.Ordinal);

    /// <summary>The V21 pitfall findings of each block overwrite (roadmap row 22), by operationId.</summary>
    internal IReadOnlyDictionary<string, PlcV21PitfallPlan> Pitfalls => _pitfalls;

    private readonly Dictionary<string, PlcMainSafetyLinkPlan> _safetyLinks = new(StringComparer.Ordinal);

    /// <summary>The F-runtime groups each block import's target was the main safety block (or IDB) of at planning, by operationId (E7).</summary>
    internal IReadOnlyDictionary<string, PlcMainSafetyLinkPlan> SafetyLinks => _safetyLinks;

    public async Task<PlanResult<PlcWriteEffect>> PlanAsync(IReadOnlyList<PlcOperationRequest> items)
    {
        var evidence = await ReadInventoryAsync().ConfigureAwait(false);
        if (evidence.Error is { } inventoryError) return Failed(inventoryError);
        var reads = new CallReads(evidence.Inventory!, items) { Safety = new PlcSafetyPlanning(client, _safetyLinks) };
        // One budget per call keeps every diff of the call inside the per-call excerpt bound.
        var budget = new PlcContentDiffBudget();
        var plans = new List<ItemPlan<PlcWriteEffect>>();
        var guards = new Dictionary<string, IReadOnlyList<FiredGuard>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var outcome = await ResolveAsync(item, reads, budget).ConfigureAwait(false);
            if (outcome.Error is { } error) return Failed(error);
            var effect = outcome.Effect ?? throw new InvalidOperationException($"Operation '{item.OperationId}' resolved without an effect or an error.");
            var dependsOn = outcome.State?.LastTouchedBy(effect.Target, item);
            outcome.State?.Apply(item);
            // The effect stays known: the pipeline drops plans with a null effect.
            plans.Add(ItemPlan<PlcWriteEffect>.Resolved(effect with { DependsOn = dependsOn }, outcome.Preconditions) with { DependsOn = dependsOn });
            guards[item.OperationId] = outcome.Guards;
        }

        return new PlanResult<PlcWriteEffect>(WritePlan<PlcWriteEffect>.Ok(plans), guards);
    }

    /// <summary>Re-reads real state for one dependent item just before its own mutation, with that item's read set.</summary>
    public async Task<ReplanResult<PlcWriteEffect>> ReplanAsync(PlcOperationRequest item)
    {
        var evidence = await ReadInventoryAsync().ConfigureAwait(false);
        var outcome = evidence.Error is { } inventoryError
            ? new ItemOutcome(null, null, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), inventoryError)
            : await ResolveAsync(item, new CallReads(evidence.Inventory!, new[] { item }) { Safety = new PlcSafetyPlanning(client, _safetyLinks) }, new PlcContentDiffBudget()).ConfigureAwait(false);
        return outcome.Error is { } error
            ? new ReplanResult<PlcWriteEffect>(ItemReplan<PlcWriteEffect>.Fail(error.Category, error.Message), Array.Empty<FiredGuard>())
            : new ReplanResult<PlcWriteEffect>(ItemReplan<PlcWriteEffect>.Ok(ItemPlan<PlcWriteEffect>.Resolved(outcome.Effect!, outcome.Preconditions)), outcome.Guards);
    }

    private async Task<ItemOutcome> ResolveAsync(PlcOperationRequest item, CallReads reads, PlcContentDiffBudget budget)
    {
        // The common alarm classes belong to the project, not to a PLC.
        if (PlcAlarmMethods.IsProjectScoped(item.Operation)) return Alarm(await PlcAlarms.PlcAlarmPlanning.ResolveProjectAsync(client, item).ConfigureAwait(false), null);
        // The block-compilation settings belong to the project, not to a PLC (K10).
        if (item.Operation == ProjectCompilationSettingsMethods.Set)
        {
            var (settingsEffect, settingsError) = PlcCompilationSettings.Resolve(client.BindingSnapshot, item);
            return settingsError is not null ? Fail(settingsError)
                : new ItemOutcome(null, settingsEffect, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), null);
        }
        // The project's languages and texts belong to the project, not to a PLC (issue #8).
        if (Contracts.Project.ProjectLanguageMethods.IsWrite(item.Operation))
        {
            var languages = await PlcProjectLanguages.ResolveAsync(client, item).ConfigureAwait(false);
            return languages.Error is { } languageFailure ? Fail(languageFailure)
                : new ItemOutcome(null, languages.Effect, languages.Guards, Array.Empty<CheckedPrecondition>(), null);
        }

        var inventory = reads.Inventory;
        // The settings family (issues #15, #13, #12, #14, #21, #25): a project-wide item has no PLC; a PLC item is matched in the
        // inventory; the services are resolved and bound by the worker. The password policies and the account locking are read
        // once for their current values (security review F1); the other items have no planning read.
        if (Contracts.Plc.Settings.PlcSettingsCatalog.IsWrite(item.Operation))
        {
            PlcTagInventoryPlcInfo? settingsPlc = null;
            if (!Contracts.Plc.Settings.PlcSettingsCatalog.IsProjectScoped(item.Operation))
            {
                var (matched, settingsError) = MatchPlc(item, inventory);
                if (settingsError is not null) return Fail(settingsError);
                settingsPlc = matched;
            }

            var (settingsItemEffect, settingsGuards) = await PlcSettings.ResolveAsync(client, item, settingsPlc).ConfigureAwait(false);
            return new ItemOutcome(null, settingsItemEffect, settingsGuards, Array.Empty<CheckedPrecondition>(), null);
        }

        // The OPC UA configuration of one PLC (issue #24): matched in the inventory, everything else is resolved by the worker.
        // Issue #16: a folder download or the online master secret of one PLC, matched in the inventory; the worker resolves the rest.
        if (Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsWorkerMethod(item.Operation))
        {
            var (dataPlc, dataError) = MatchPlc(item, inventory);
            if (dataError is not null) return Fail(dataError);
            var (dataEffect, dataGuards) = PlcDeviceData.Resolve(item, dataPlc!);
            return new ItemOutcome(null, dataEffect, dataGuards, Array.Empty<CheckedPrecondition>(), null);
        }

        if (Contracts.Plc.OpcUa.PlcOpcUaMethods.IsWrite(item.Operation))
        {
            var (opcUaPlc, opcUaError) = MatchPlc(item, inventory);
            if (opcUaError is not null) return Fail(opcUaError);
            var (opcUaEffect, opcUaGuards) = PlcOpcUa.Resolve(item, opcUaPlc!);
            return new ItemOutcome(null, opcUaEffect, opcUaGuards, Array.Empty<CheckedPrecondition>(), null);
        }

        // A loadable file of one PLC: matched in the inventory, everything else is resolved and bound by the worker (issue #26).
        if (item.Operation == Contracts.Plc.Loadable.PlcLoadableMethods.GenerateLoadable)
        {
            string? loadableSelector;
            try
            {
                loadableSelector = PlcLoadable.Selector(item);
            }
            catch (ArgumentException exception)
            {
                return Fail(new(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {exception.Message}"));
            }

            var (loadablePlc, loadableError) = MatchPlc(new PlcOperationRequest { OperationId = item.OperationId, Operation = item.Operation, PlcName = loadableSelector }, inventory);
            if (loadableError is not null) return Fail(loadableError);
            var (loadableEffect, loadableGuards) = PlcLoadable.Resolve(item, loadablePlc!);
            return new ItemOutcome(null, loadableEffect, loadableGuards, Array.Empty<CheckedPrecondition>(), null);
        }

        // Block know-how and write protection (issue #5): the block is read once for its state and fail-safe identity.
        if (Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.IsWrite(item.Operation))
        {
            var (protectionPlc, protectionError) = MatchPlc(item, inventory);
            if (protectionError is not null) return Fail(protectionError);
            var protection = await PlcBlockProtection.ResolveAsync(client, item, protectionPlc!, reads.Safety).ConfigureAwait(false);
            return protection.Error is { } protectionFailure ? Fail(protectionFailure)
                : new ItemOutcome(null, protection.Effect, protection.Guards, Array.Empty<CheckedPrecondition>(), null);
        }

        var (plc, matchError) = MatchPlc(item, inventory);
        if (matchError is { Category: WorkerFailureCategories.TargetNotFound } && !inventory.IsComplete)
        {
            // The PLC may be one whose discovery failed: unverifiable, never absent.
            var hidden = new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                $"{matchError.Message} The tag inventory is incomplete, so the PLC may exist but cannot be verified.");
            return new ItemOutcome(null, PlcWorkingState.Unresolved(item, PlcSelector(item) ?? string.Empty, null), new[] { hidden },
                Array.Empty<CheckedPrecondition>(), null);
        }

        if (matchError is not null) return Fail(matchError);
        if (!reads.States.TryGetValue(plc!, out var state))
        {
            ProjectTreeObservation? tree = null;
            if (reads.TreePlcs.Contains(plc!))
            {
                var (read, treeError) = await ReadTreeAsync(plc!).ConfigureAwait(false);
                if (treeError is not null) return Fail(treeError);
                tree = read;
            }

            PlcWatchTablePlcInfo? watch = null;
            if (reads.WatchPlcs.Contains(plc!))
            {
                var (read, watchError) = await ReadWatchTablesAsync(plc!).ConfigureAwait(false);
                if (watchError is not null) return Fail(watchError);
                watch = read;
            }

            var (sources, sourcesComplete, sourcesProblem) = ((PlcExternalSourcePlcInfo?)null, true, (string?)null);
            if (reads.SourcePlcs.Contains(plc!))
            {
                var (read, complete, sourceError) = await PlcSourceFolderOperations.ReadSourcesAsync(client, plc!).ConfigureAwait(false);
                // A read the worker could not complete degrades to unreadable evidence (plc_state_unverifiable), like the
                // other inventories; a lost Portal, a binding or protocol failure still stops the plan.
                if (sourceError is not null && sourceError.Category != Contracts.Worker.WorkerFailureCategories.WorkerOperationFailed) return Fail(sourceError);
                (sources, sourcesComplete, sourcesProblem) = sourceError is null
                    ? (read, complete, null)
                    : (new PlcExternalSourcePlcInfo { PlcName = plc!.PlcName, DeviceName = plc.DeviceName ?? string.Empty }, false, sourceError.Message);
            }

            PlcTextListPlcInfo? textLists = null;
            if (reads.TextListPlcs.Contains(plc!))
            {
                var (read, textListError) = await PlcAlarms.PlcAlarmPlanning.ReadTextListsAsync(client, plc!).ConfigureAwait(false);
                if (textListError is not null) return Fail(textListError);
                textLists = read;
            }

            TechnologyObjectPlcInfo? technology = null;
            if (reads.TechnologyPlcs.Contains(plc!))
            {
                var (read, technologyError) = await PlcTechnologyObjectPlanning.ReadInventoryAsync(client, plc!).ConfigureAwait(false);
                if (technologyError is not null) return Fail(technologyError);
                technology = read;
            }

            reads.States[plc!] = state = PlcWorkingState.Create(plc!, inventory.IsComplete, tree).WithWatchTables(watch)
                .WithSourceFolders(tree, sources, sourcesComplete, sourcesProblem).WithTextLists(textLists).WithTechnologyObjects(technology);
        }

        var resolution = state.Resolve(item);
        if (resolution.Error is not null) return Fail(resolution.Error);
        var effect = resolution.Effect!;
        var resolvedGuards = resolution.Guards;
        if (state.ChangesFailSafeBlock(item))
        {
            // An F-block delete, rename, renumber, copy or move (or a group delete holding one): TIA Portal's rule, not a
            // stricter one: the acceptance guard, and the login block guard only when a safety password is set.
            var (failSafe, _) = await reads.Safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' ({item.Operation} of '{item.BlockPath}')").ConfigureAwait(false);
            resolvedGuards = resolvedGuards.Concat(failSafe).ToArray();
        }

        if (PlcSafetyPlanning.Handles(item))
        {
            var (safetyEffect, safetyGuards, safetyError) = await reads.Safety.ResolveAsync(item, effect).ConfigureAwait(false);
            return safetyError is not null ? Fail(safetyError)
                : new ItemOutcome(state, safetyEffect, resolvedGuards.Concat(safetyGuards).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        if (PlcBlockEditMethods.InterfaceEdits.Contains(item.Operation, StringComparer.Ordinal))
        {
            var edit = await PlcInterfaceEditPlanner.ResolveAsync(client, item, effect, state.LastTouchedBy(effect.Target), reads.PlannedDocuments, budget, reads.Safety).ConfigureAwait(false);
            if (edit.Error is { } editError) return Fail(editError);
            Remember(item, edit.Planned, edit.Guards);
            // A block interface edit overwrites the block with the planned bundle (SimaticML route).
            var pitfalls = edit.Planned is { IsType: false } blockPlan && item.BlockPath is not null
                ? await PlcV21Pitfalls.PlanAsync(client, item, SourceFormatNames.Xml, null, blockPlan.Content).ConfigureAwait(false)
                : PlcV21PitfallPlan.None;
            _pitfalls[item.OperationId] = pitfalls;
            return new ItemOutcome(state, edit.Effect, resolvedGuards.Concat(edit.Guards).Concat(pitfalls.Guards).ToArray(), edit.Preconditions, null);
        }

        if (PlcBlockEditMethods.DocumentEdits.Contains(item.Operation, StringComparer.Ordinal))
        {
            var edit = await PlcDocumentEditPlanner.ResolveAsync(client, item, effect, state.LastTouchedBy(effect.Target), reads.PlannedDocuments, budget, reads.Safety).ConfigureAwait(false);
            if (edit.Error is { } editError) return Fail(editError);
            Remember(item, edit.Planned, edit.Guards);
            var pitfalls = edit.Planned is { IsType: false } blockPlan && item.BlockPath is not null
                ? await PlcV21Pitfalls.PlanAsync(client, item, SourceFormatNames.Xml, null, blockPlan.Content).ConfigureAwait(false)
                : PlcV21PitfallPlan.None;
            _pitfalls[item.OperationId] = pitfalls;
            return new ItemOutcome(state, edit.Effect, resolvedGuards.Concat(edit.Guards).Concat(pitfalls.Guards).ToArray(), edit.Preconditions, null);
        }

        if (Contracts.Plc.SimaticSd.SimaticSdMethods.NetworkEdits.Contains(item.Operation, StringComparer.Ordinal))
        {
            var edit = await PlcSdNetworkEditPlanner.ResolveAsync(client, item, effect, state.LastTouchedBy(effect.Target), reads.PlannedDocuments, budget, reads.Safety).ConfigureAwait(false);
            if (edit.Error is { } editError) return Fail(editError);
            Remember(item, edit.Planned, edit.Guards);
            // Every network edit takes the SIMATIC SD route: the V21 .s7dcl import pitfalls apply.
            var pitfalls = edit.Planned is { } sdPlan
                ? await PlcV21Pitfalls.PlanAsync(client, item, SourceFormatNames.Sd, null, sdPlan.Content).ConfigureAwait(false)
                : PlcV21PitfallPlan.None;
            _pitfalls[item.OperationId] = pitfalls;
            return new ItemOutcome(state, edit.Effect, resolvedGuards.Concat(edit.Guards).Concat(pitfalls.Guards).ToArray(), edit.Preconditions, null);
        }

        if (item.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument)
            return new ItemOutcome(state, effect, resolvedGuards.Concat(await CreateFromDocumentGuardsAsync(item, effect, reads.Safety,
                    resolvedGuards.Any(g => g.Id == PlcGuardDefinitions.SafetyAcceptance)).ConfigureAwait(false)).ToArray(),
                Array.Empty<CheckedPrecondition>(), null);

        if (item.Operation == PlcBlockEditMethods.ReplaceTagComments)
        {
            var comments = await PlcTagCommentPlanning.ResolveAsync(client, item, effect, state.LastTouchedBy(effect.Target, item) is not null).ConfigureAwait(false);
            return comments.Error is { } commentError ? Fail(commentError)
                : new ItemOutcome(state, comments.Effect, resolvedGuards.Concat(comments.Guards).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        if (PlcWorkingState.IsAlarmOperation(item.Operation))
            return Alarm(await PlcAlarms.PlcAlarmPlanning.ResolveAsync(client, item, effect, resolvedGuards, state.HasEarlierWrites).ConfigureAwait(false), state);

        if (item.Operation == PlcBlockEditMethods.UpdateDbMember)
        {
            var member = await PlcInterfaceEditPlanner.ResolveDbMemberAsync(client, item, effect, state.LastTouchedBy(effect.Target), reads.Safety).ConfigureAwait(false);
            return member.Error is { } memberError ? Fail(memberError)
                : new ItemOutcome(state, member.Effect, resolvedGuards.Concat(member.Guards).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        if (PlcTechnologyObjectPlanning.Handles(item))
        {
            var technology = await PlcTechnologyObjectPlanning.ResolveAsync(client, item, effect, state.LastTouchedBy(effect.Target, item) is not null).ConfigureAwait(false);
            return technology.Error is { } technologyError ? Fail(technologyError)
                : new ItemOutcome(state, technology.Effect, resolvedGuards.Concat(technology.Guards).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        if (item.Operation is not ("update_block_logic" or "update_type_content" or TechnologyObjectMethods.UpdateContent) && !PlcWorkingState.IsWatchContentUpdate(item.Operation))
            return new ItemOutcome(state, effect, resolvedGuards, Array.Empty<CheckedPrecondition>(), null);
        return await ResolveContentAsync(item, state, effect, resolvedGuards, budget, reads.Safety).ConfigureAwait(false);
    }

    /// <summary>Exports the target in the write's format: the hash check and the diff evidence.</summary>
    private async Task<ItemOutcome> ResolveContentAsync(PlcOperationRequest item, PlcWorkingState state,
        PlcWriteEffect effect, IReadOnlyList<FiredGuard> guards, PlcContentDiffBudget budget, PlcSafetyPlanning safety)
    {
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = await ExportAsync(client, item, format, effect.Target).ConfigureAwait(false);
        if (!export.Success && export.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
            return Fail(new(export.FailureCategory!, $"Operation '{item.OperationId}': {export.Error}"));
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
        {
            // Unreadable content is never hashed: an empty export is not "unchanged" evidence.
            var unverifiable = new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                $"Operation '{item.OperationId}': the current content could not be exported ({(export.Success ? "the export was empty" : export.Error)}), so expectedContentHash cannot be checked.");
            return new ItemOutcome(state, effect, guards.Append(unverifiable).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        var check = ContentHashes.Check(item.ExpectedContentHash, format, export.Payload);
        if (!check.Success) return Fail(new(check.ErrorCategory!, $"Operation '{item.OperationId}': {check.Message}"));
        if (item.Operation == "update_block_logic"
            && (Contracts.Plc.Safety.PlcSafetyPolicy.DeclaresFailSafe(export.Payload) || Contracts.Plc.Safety.PlcSafetyPolicy.DeclaresFailSafe(item.Content)
                || format != SourceFormatNames.Xml && await IsFailSafeBlockAsync(item).ConfigureAwait(false)))
        {
            // An F-block (or a document that would make one): the acceptance guard always, the login block guard unless permitted.
            var (safetyGuards, _) = await safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' (update_block_logic of '{item.BlockPath}')").ConfigureAwait(false);
            guards = guards.Concat(safetyGuards).ToArray();
        }

        if (item.Operation == "update_block_logic")
        {
            var pitfalls = await PlcV21Pitfalls.PlanAsync(client, item, format, export.Payload, item.Content!).ConfigureAwait(false);
            _pitfalls[item.OperationId] = pitfalls;
            guards = guards.Concat(pitfalls.Guards).ToArray();
            if (format == SourceFormatNames.Sd && SdExportFormGuard(item, export.Payload, item.Content!) is { } exportForms)
                guards = guards.Append(exportForms).ToArray();
        }

        return new ItemOutcome(state, effect with { ContentDiff = PlcContentDiff.BuildForWrite(format, export.Payload, item.Content!, budget) },
            guards, new[] { check.Evidence! }, null);
    }

    /// <summary>
    /// update_block_logic sd: the submitted networks that are unchanged from the current export (same body in comparison
    /// form) and hold a reported export-only form (<see cref="Contracts.Plc.SimaticSd.SimaticSdExportForms"/>); null when none.
    /// </summary>
    internal static FiredGuard? SdExportFormGuard(PlcOperationRequest item, string current, string submitted)
    {
        try
        {
            if (!Contracts.Plc.Interface.PlcBlockBundleText.TryGetDocument(current, ".s7dcl", out _, out var currentS7dcl)
                || !Contracts.Plc.Interface.PlcBlockBundleText.TryGetDocument(submitted, ".s7dcl", out _, out var submittedS7dcl))
                return null;
            var exported = new HashSet<string>(Contracts.Plc.SimaticSd.SimaticSdDocument.Parse(currentS7dcl).Networks
                .Select(n => Contracts.Plc.SimaticSd.SimaticSdText.NormalizeForComparison(n.BodyLines)), StringComparer.Ordinal);
            var found = Contracts.Plc.SimaticSd.SimaticSdDocument.Parse(submittedS7dcl).Networks
                .Where(n => exported.Contains(Contracts.Plc.SimaticSd.SimaticSdText.NormalizeForComparison(n.BodyLines)))
                .Select(n => (n.Number, Forms: Contracts.Plc.SimaticSd.SimaticSdExportForms.Find(n.Code))).Where(n => n.Forms.Count > 0).ToList();
            return found.Count == 0 ? null : new FiredGuard(PlcGuardDefinitions.SdReimportsExportForm, item.OperationId,
                $"Operation '{item.OperationId}': {string.Join(", ", found.Select(n => $"network {n.Number} ({string.Join(" ", n.Forms)})"))} of '{item.BlockPath}' "
                + "are re-imported in the form TIA Portal exported them; these forms are reported (1748477582/tia-portal-openness-mcp) to fail "
                + "ImportFromDocuments (reported, not reproduced live on V21 2026-11-02 for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF, Add). "
                + "Nothing is rewritten; if TIA Portal refuses the import, write those networks in the import form.");
        }
        catch (Contracts.Plc.SimaticSd.SimaticSdException)
        {
            // An unreadable network structure is TIA Portal's to judge at import.
            return null;
        }
    }

    /// <summary>
    /// create_block_from_document: an S7_Safety := "TRUE" header is a fail-safe block (TIA Portal's login rule and the
    /// acceptance guard), and the .s7dcl constructs the V21 SD import is reported to alter fire the pitfall guard.
    /// </summary>
    private async Task<IReadOnlyList<FiredGuard>> CreateFromDocumentGuardsAsync(PlcOperationRequest item, PlcWriteEffect effect, PlcSafetyPlanning safety, bool failSafeAlreadyPlanned)
    {
        var guards = new List<FiredGuard>();
        if (Contracts.Plc.SimaticSd.SimaticSdBlockDocument.TryCheck(item.Content!, out _) is not { } document) return guards;
        if (document.IsFailSafe && !failSafeAlreadyPlanned)
        {
            var (safetyGuards, _) = await safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' (create_block_from_document of '{effect.Target.BlockPath}')").ConfigureAwait(false);
            guards.AddRange(safetyGuards);
        }

        var hazards = Contracts.Plc.Interface.S7dclImportHazards.Find(document.S7dcl, null);
        if (hazards.Count > 0)
            guards.Add(new FiredGuard(PlcGuardDefinitions.S7dclImportMayAlter, item.OperationId,
                $"Operation '{item.OperationId}': the .s7dcl document has constructs TIA Portal V21's .s7dcl import is reported (rickgaiser/TiaMcp, not verified on V21) "
                + $"to alter without an error: {string.Join(" ", hazards.Select(h => h.Detail))} Inspect the created block with plc_read get_block_content."));
        return guards;
    }

    /// <summary>
    /// Whether a source-format update targets an F-block: the source text carries no ProgrammingLanguage, so the block
    /// identity is read (get_block_interface). An unreadable identity is not an F-block here; the worker reads the
    /// language itself before the import and applies the same login rule.
    /// </summary>
    private async Task<bool> IsFailSafeBlockAsync(PlcOperationRequest item)
    {
        var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetBlockInterface, new PlcBlockEditRequestInfo { BlockPath = item.BlockPath }).ConfigureAwait(false);
        if (!read.Success) return false;
        var (info, _) = PlcInterfaceEditPlanner.Decode<PlcBlockInterfaceInfo>(item, PlcBlockEditMethods.GetBlockInterface, read, "the block identity could not be read");
        return info is not null && (info.IsSafety || Contracts.Plc.Safety.PlcSafetyPolicy.IsFailSafeLanguage(info.Block.ProgrammingLanguage));
    }

    /// <summary>The current content of a content item in the write's format: the block, the type, or the watch/force table SimaticML.</summary>
    internal static Task<WorkerCallResult> ExportAsync(OpennessWorkerClient client, PlcOperationRequest item, string format, PlcTargetIdentity? target = null)
        => item.Operation switch
        {
            "update_block_logic" => client.GetBlockContentAsync(item.BlockPath!, format: format),
            "update_type_content" => client.GetTypeContentAsync(item.TypePath!, format: format),
            PlcOnlineMethods.UpdateWatchTableContent => client.GetWatchOrForceTableContentAsync(PlcOnlineMethods.GetWatchTableContent,
                target?.PlcName ?? item.PlcName, target?.TableName ?? item.TableName!, target?.FolderPath ?? item.FolderPath),
            PlcOnlineMethods.UpdateForceTableContent => client.GetWatchOrForceTableContentAsync(PlcOnlineMethods.GetForceTableContent,
                target?.PlcName ?? item.PlcName, target?.TableName ?? item.TableName!, target?.FolderPath ?? item.FolderPath),
            TechnologyObjectMethods.UpdateContent => client.TechnologyObjectAsync(TechnologyObjectMethods.GetContent,
                new TechnologyObjectRequestInfo { PlcName = target?.PlcName ?? item.PlcName, Name = target?.Name ?? item.Name }),
            _ => throw new InvalidOperationException($"'{item.Operation}' is not a content write."),
        };

    /// <summary>The PLC's watch-and-force table inventory, read once per call for the PLCs whose items need it.</summary>
    internal async Task<(PlcWatchTablePlcInfo? Tables, WriteToolError? Error)> ReadWatchTablesAsync(PlcTagInventoryPlcInfo plc)
    {
        var call = await client.ListWatchAndForceTablesAsync(plc.PlcName).ConfigureAwait(false);
        if (!call.Success)
            return (null, new(call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The watch and force tables of PLC '{plc.PlcName}' could not be read: {call.Error}"));
        try
        {
            var inventory = PlcOnline.PlcOnlinePayloadContract.DecodeWatchTables(call.Payload);
            var match = inventory.Plcs.Where(p => p.PlcName == plc.PlcName && p.DeviceName == plc.DeviceName).ToList();
            return match.Count == 1 ? (match[0], null)
                : (null, new(WorkerFailureCategories.StateChanged, $"The watch-table inventory does not describe PLC '{plc.PlcName}' on device '{plc.DeviceName}' exactly once."));
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, new(WorkerFailureCategories.ProtocolError, PlcOnline.PlcOnlinePayloadContract.ProtocolFailureMessage));
        }
    }

    /// <summary>D5: the selector matches a software or device name among every PLC, case-insensitively.</summary>
    private static (PlcTagInventoryPlcInfo? Plc, WriteToolError? Error) MatchPlc(PlcOperationRequest item, PlcTagInventoryInfo inventory)
    {
        string? selector;
        try
        {
            selector = PlcSelector(item);
        }
        catch (ArgumentException ex)
        {
            return (null, new(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {ex.Message}"));
        }

        var matches = inventory.Plcs.Where(plc => selector is null
            || PlcNameRules.Comparer.Equals(plc.PlcName, selector) || PlcNameRules.Comparer.Equals(plc.DeviceName, selector)).ToArray();
        var described = selector is null ? "An omitted PLC name" : $"PLC '{selector}'";
        return matches.Length switch
        {
            1 => (matches[0], null),
            0 => (null, new(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': {described} matches no PLC in the project.")),
            _ => (null, new(WorkerFailureCategories.TargetAmbiguous,
                $"Operation '{item.OperationId}': {described} matches {matches.Length} PLCs ({string.Join(", ", matches.Select(m => $"'{m.PlcName}' on device '{m.DeviceName}'"))}). Name the PLC software uniquely.")),
        };
    }

    /// <exception cref="ArgumentException">The block or type path is malformed.</exception>
    private static string? PlcSelector(PlcOperationRequest item)
        => PlcWorkingState.HandlesSourceFolder(item.Operation) ? PlcWorkingState.SourceFolderPlcSelector(item)
            : item.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument && item.TargetGroupPath is not null
                ? BlockAddress.Parse(item.TargetGroupPath.TrimEnd('/') + "/_").PlcName
            : item.BlockPath is not null ? BlockAddress.Parse(item.BlockPath).PlcName
            : item.TypePath is not null ? PlcTypeAddress.Parse(item.TypePath).PlcName
            : item.PlcName;

    internal async Task<Evidence> ReadInventoryAsync()
    {
        var request = new PlcOperationRequest { OperationId = "plan", Operation = "list_tag_tables" };
        var projected = PlcPayloadContract.Project(request, await client.ListTagTablesAsync(plcName: null).ConfigureAwait(false));
        return projected.Result is { } result
            ? new Evidence(CanonicalJson.Deserialize<PlcTagInventoryInfo>(result.GetRawText()), null)
            : new Evidence(null, new(projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The PLC tag inventory could not be read: {projected.Failure?.Message}"));
    }

    internal async Task<(ProjectTreeObservation? Tree, WriteToolError? Error)> ReadTreeAsync(PlcTagInventoryPlcInfo plc)
    {
        if (string.IsNullOrWhiteSpace(plc.DeviceName))
            return (null, new(WorkerFailureCategories.WorkerOperationFailed, $"The device of PLC '{plc.PlcName}' could not be identified, so its project tree cannot be read."));
        var selector = new[]
        {
            new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.Device, Name = plc.DeviceName },
            new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = plc.PlcName },
        };
        var call = await client.BrowseProjectTreeV3SnapshotAsync(startSelector: selector).ConfigureAwait(false);
        if (!call.WorkerResult.Success)
            return (null, new(call.WorkerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The project tree of PLC '{plc.PlcName}' could not be read: {call.WorkerResult.Error}"));
        try
        {
            return (ProjectTreeWorkerPayloadContract.Decode(call.WorkerResult, selector, null), null);
        }
        catch (ProjectTreeProtocolException ex)
        {
            return (null, new(ex.Category, ex.Message));
        }
    }

    private static ItemOutcome Alarm(PlcAlarms.PlcAlarmPlanning.Outcome outcome, PlcWorkingState? state)
        => outcome.Error is { } error ? Fail(error) : new ItemOutcome(state, outcome.Effect, outcome.Guards, outcome.Preconditions, null);

    private static ItemOutcome Fail(WriteToolError error)
        => new(null, null, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), error);

    private static PlanResult<PlcWriteEffect> Failed(WriteToolError error)
        => new(WritePlan<PlcWriteEffect>.Fail(error.Category, error.Message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
}
