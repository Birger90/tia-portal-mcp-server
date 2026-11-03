using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>
/// library_write on the guarded single-call pipeline: validate, verified standalone binding, plan every item
/// through the worker's non-mutating <c>library_plan</c> (an item on a library an earlier item of the call
/// touches depends on it and is re-planned just before its mutation), informative guards, <c>dryRun</c>,
/// mutate in order (stop at the first failure, no rollback), trusted/superseded verification (notVerified where the
/// worker could not read an item's postcondition back), audit.
/// Content items never prompt. A call containing a global-library lifecycle item (open, retrieve, create,
/// save, close: they create, extract, open or overwrite library files outside the project) is confirmed
/// once per actual read-write call, like the project lifecycle tools; full runs it under policy. Every item's
/// mutation answers TIA Portal confirmation dialogs by its <c>portalConfirmations</c> policy and records them
/// (plc_download's rule: declined ones fail the item, accepted ones warn). A close of a library with unsaved
/// changes this server made is blocked (<see cref="LibraryUnsavedChanges"/>).
/// </summary>
public sealed class LibraryWriteDomain
    : IWriteDomain<LibraryOperationRequest, LibraryWriteEffect, WriteVerification, LibraryGuardedWriteResponse>
{
    private readonly Func<string, LibraryRequestInfo, Task<WorkerCallResult>> _call;
    private readonly LibraryUnsavedChanges _unsaved;
    private readonly Func<int?> _portalProcessId;
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly List<string> _planWarnings = new();
    /// <summary>The security classification of each planned set_portal_setting, sent with its mutation (F2).</summary>
    private readonly Dictionary<string, bool> _plannedSecurity = new(StringComparer.Ordinal);
    private IReadOnlyList<LibraryOperationRequest> _items = [];
    private int? _portal;
    private bool _portalRead;

    public LibraryWriteDomain(OpennessWorkerClient client)
        : this(client.LibraryAsync, LibraryUnsavedChanges.For(client), () => client.BindingSnapshot is { IsVerified: true } binding ? binding.PortalProcessId : null)
    {
    }

    /// <summary>
    /// Worker-independent seam for unit tests of planning, guards, confirmation and projection. <paramref name="portalProcessId"/>
    /// reads the TIA Portal process of the binding the call runs under (null: unknown); the ledger is keyed by it.
    /// </summary>
    internal LibraryWriteDomain(Func<string, LibraryRequestInfo, Task<WorkerCallResult>> call, LibraryUnsavedChanges? unsaved = null,
        Func<int?>? portalProcessId = null)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));
        _unsaved = unsaved ?? new LibraryUnsavedChanges();
        _portalProcessId = portalProcessId ?? (() => null);
    }

    /// <summary>The TIA Portal process of this call (read when planning starts; directly when a stage runs without planning).</summary>
    private int? Portal => _portalRead ? _portal : _portalProcessId();

    public string ToolName => LibraryMethods.WriteTool;

    public string ContractVersion => LibraryPayloadContract.ContractVersion;

    /// <summary>True when the call contains a global-library lifecycle item (read after validation and planning).</summary>
    /// <remarks>
    /// Also true for every call with a set_portal_setting (security review 2026-11-03, F2): a TIA Portal setting acts on the
    /// whole TIA Portal instance and no documented member names what it protects, so the human confirms every such change in
    /// read-write; a security-relevant one (by the names the worker resolved in planning) also fires the acknowledge guard.
    /// A prompting call refuses unsafe operationIds like every confirming call (plc_write's fail-safe rule).
    /// </remarks>
    public bool ConfirmsEveryCall => _items.Any(item => LibraryMethods.IsLifecycle(item?.Operation) || item?.Operation == LibraryMethods.SetPortalSetting);

    /// <summary>
    /// The read-write prompt: the effect first (what the call does to library files outside the project), then one line
    /// per lifecycle item (<see cref="LifecycleLine"/>) with its operation, its target and its operationId quoted and
    /// escaped (<see cref="ConfirmationText.Quote"/>: targets embed caller paths), the open mode it requests and its TIA
    /// Portal dialog policy. The pipeline shows acknowledge guard lines before it; library_write has none (its guards are
    /// info and one block), so the prompt consists of this description only.
    /// </summary>
    public string DescribeForConfirmation(IReadOnlyList<LibraryOperationRequest> items, IReadOnlyList<ItemPlan<LibraryWriteEffect>> plans)
    {
        var lines = items.Select((item, index) => (item, effect: index < plans.Count ? plans[index]?.Effect : null))
            .Where(pair => LibraryMethods.IsLifecycle(pair.item.Operation))
            .Select((pair, number) => $"{number + 1}. {pair.item.Operation} {LifecycleLine(pair.item, pair.effect)}");
        var content = items.Count(item => !LibraryMethods.IsLifecycle(item.Operation));
        var settings = items.Select((item, index) => (item, effect: index < plans.Count ? plans[index]?.Effect : null))
            .Where(pair => pair.item.Operation == LibraryMethods.SetPortalSetting)
            .Select((pair, number) => $"S{number + 1}. set_portal_setting {ConfirmationText.Quote(pair.effect?.Target ?? $"{pair.item.SettingsFolderPath}/{pair.item.SettingName} -> {pair.item.SettingValue}")}; "
                + $"operationId {ConfirmationText.Quote(pair.item.OperationId)}").ToArray();
        if (!lines.Any())
            return "Confirm library_write: this call changes TIA Portal settings of this TIA Portal instance (not the project):\n" + string.Join("\n", settings)
                + (content > settings.Length ? $"\nThe call also runs {content - settings.Length} library content item(s) in order; they stop at the first failure without rollback." : string.Empty);
        return "Confirm library_write: this call opens, creates, retrieves, saves, archives or closes global library files or secondary projects outside the bound project:\n"
            + string.Join("\n", lines)
            + (settings.Length == 0 ? string.Empty : "\n" + string.Join("\n", settings))
            + (content == 0 ? string.Empty : $"\nThe call also runs {content} library content item(s) in order; they stop at the first failure without rollback.")
            + "\nTIA Portal confirmation dialogs raised by an item are answered by its portalConfirmations policy (decline: No, recorded, the item fails; "
            + "accept: Yes, recorded, a warning).";
    }

    /// <summary>
    /// One lifecycle item of the prompt, built from the item itself wherever the plan cannot say it: the resolved target, or
    /// for an item without a planned effect (it depends on an earlier item of the call and is planned again only after the
    /// confirmation) the item's own path, archive and directory, name or library, all quoted; then always the open mode the
    /// item requests (OpenWithUpgrade, OpenMode ReadWrite or ReadOnly), so an in-place upgrade or a writable open is visible
    /// before the human confirms; then the TIA Portal dialog policy and the operationId.
    /// </summary>
    internal static string LifecycleLine(LibraryOperationRequest item, LibraryWriteEffect? effect)
    {
        static string Q(string? value) => ConfirmationText.Quote(value);
        var target = effect is not null
            ? Q(effect.Target)
            : item.Operation switch
            {
                LibraryMethods.OpenGlobalLibrary => $"path {Q(item.Path)}",
                LibraryMethods.RetrieveGlobalLibrary => $"archive {Q(item.Path)} into {Q(item.TargetDirectory)}",
                LibraryMethods.CreateGlobalLibrary => $"new library {Q(item.Name)} in {Q(item.TargetDirectory)}",
                LibraryMethods.ArchiveGlobalLibrary => $"archive of {Q(item.Library)} as {Q(item.Name)} in {Q(item.TargetDirectory)}",
                LibraryMethods.OpenSecondaryProject => $"secondary project {Q(item.Path)}",
                LibraryMethods.RetrieveSecondaryProject => $"secondary project from archive {Q(item.Path)} into {Q(item.TargetDirectory)}",
                LibraryMethods.CloseSecondaryProject => $"secondary project {Q(item.SecondaryProject)}",
                LibraryMethods.SaveGlobalLibraryAs => $"copy of {Q(item.Library)} in {Q(item.TargetDirectory)}",
                _ => $"library {Q(item.Library)}",
            } + " (resolved just before its mutation: it depends on an earlier item of this call)";
        var mode = item.Operation switch
        {
            LibraryMethods.OpenGlobalLibrary when item.Upgrade == true => "; OpenWithUpgrade: may upgrade the library file in place on disk",
            LibraryMethods.RetrieveGlobalLibrary when item.Upgrade == true => "; RetrieveWithUpgrade: the retrieved library is upgraded to the current version"
                + (item.OpenMode == "ReadWrite" ? "; OpenMode ReadWrite: save_global_library overwrites the library file" : $"; OpenMode {item.OpenMode ?? LibraryPolicy.DefaultOpenMode}"),
            LibraryMethods.ArchiveGlobalLibrary => $"; LibraryArchivationMode {item.ArchivationMode ?? LibraryPolicy.DefaultArchivationMode}",
            LibraryMethods.OpenSecondaryProject or LibraryMethods.RetrieveSecondaryProject => "; ProjectOpenMode.Secondary (read-only reference project; the binding does not change)"
                + (item.Upgrade == true ? "; with upgrade: may upgrade the project file on disk" : string.Empty),
            LibraryMethods.OpenGlobalLibrary or LibraryMethods.RetrieveGlobalLibrary when item.OpenMode == "ReadWrite"
                => "; OpenMode ReadWrite: save_global_library overwrites the library file",
            LibraryMethods.OpenGlobalLibrary or LibraryMethods.RetrieveGlobalLibrary => $"; OpenMode {LibraryPolicy.DefaultOpenMode}",
            _ => string.Empty,
        };
        return $"{target}{mode}; TIA Portal dialogs: {LibraryCatalog.PortalConfirmationPolicy(item)}; operationId {Q(item.OperationId)}";
    }

    public WriteValidation Validate(IReadOnlyList<LibraryOperationRequest> items, McpAccessMode accessMode)
    {
        // ConfirmsEveryCall is read from these items; WriteRun refuses unsafe operationIds of a confirming call right after
        // this validation (and again before any prompt), with the rule and message every confirming tool uses.
        _items = items.ToArray();
        return WriteValidation.Compose(
            LibraryCatalog.ValidateWrite(items),
            () => LibraryWritePayloadBudget.MeasureProtectedCore(items),
            () => LibraryCatalog.ValidateAccessMode(items, accessMode),
            ToolName);
    }

    public async Task<WritePlan<LibraryWriteEffect>> PlanAsync(IReadOnlyList<LibraryOperationRequest> items)
    {
        _items = items.ToArray();
        // The first leased stage: the binding is pinned, so its Portal process is the one every item of the call acts in.
        _portal = _portalProcessId();
        _portalRead = true;
        var plans = new List<ItemPlan<LibraryWriteEffect>>();
        var touched = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var keys = Touches(item);
            var predecessor = keys.Where(touched.ContainsKey).Select(key => touched[key])
                .OrderByDescending(id => IndexOf(items, id)).FirstOrDefault();
            if (predecessor is not null)
            {
                // The predecessor changes this library or target; its state is unknown until that mutation finished.
                plans.Add(ItemPlan<LibraryWriteEffect>.DependsOnItem(predecessor));
                _guards[item.OperationId] = [];
            }
            else
            {
                var resolved = await ReplanAsync(item).ConfigureAwait(false);
                if (!resolved.Success) return WritePlan<LibraryWriteEffect>.Fail(resolved.Error!.Category, $"Operation '{item.OperationId}': {resolved.Error.Message}");
                plans.Add(resolved.Plan!);
            }

            foreach (var key in keys.Concat(Changes(item))) touched[key] = item.OperationId;
        }

        return WritePlan<LibraryWriteEffect>.Ok(plans);
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<LibraryOperationRequest> items, IReadOnlyList<ItemPlan<LibraryWriteEffect>> plans)
        => items.SelectMany(item => _guards.GetValueOrDefault(item.OperationId) ?? [])
            .Concat(UnsavedCloseGuards(items, plans, _unsaved.Snapshot(Portal))).ToArray();

    /// <summary>
    /// The block guard of every close_global_library whose library has a change this server made (an earlier call's,
    /// from the ledger of this call's TIA Portal process, or an earlier item of this call) that no save_global_library in
    /// between covers. Walks the items in call order; a fresh open (planned, not already open) of a path starts it without
    /// known changes.
    /// </summary>
    internal static IEnumerable<FiredGuard> UnsavedCloseGuards(IReadOnlyList<LibraryOperationRequest> items,
        IReadOnlyList<ItemPlan<LibraryWriteEffect>> plans, IReadOnlyDictionary<string, LibraryUnsavedChanges.Pending> known)
    {
        var pending = new Dictionary<string, (List<string> Listed, int Count)>(StringComparer.Ordinal);
        foreach (var (key, earlier) in known)
            pending[key] = (earlier.Changes.Select(change => change + " in an earlier call").ToList(), earlier.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            switch (item.Operation)
            {
                case LibraryMethods.OpenGlobalLibrary when index < plans.Count && plans[index].Effect is { AlreadyOpen: false }:
                    pending.Remove(LibraryCatalog.Key(item.Path)!);
                    break;
                case LibraryMethods.SaveGlobalLibrary:
                    pending.Remove(LibraryCatalog.Key(item.Library)!);
                    break;
                case LibraryMethods.CloseGlobalLibrary:
                {
                    var key = LibraryCatalog.Key(item.Library)!;
                    if (pending.TryGetValue(key, out var changes))
                    {
                        var listed = string.Join(", ", changes.Listed.Take(LibraryUnsavedChanges.MaxListedChanges));
                        var more = changes.Count > LibraryUnsavedChanges.MaxListedChanges ? $" and {changes.Count - LibraryUnsavedChanges.MaxListedChanges} more" : string.Empty;
                        yield return new FiredGuard(LibraryGuardDefinitions.CloseUnsavedChanges, item.OperationId,
                            $"{item.Library} has changes this server made and did not save: {listed}{more}. Close() offers no save-or-discard choice, "
                            + "so the close is refused: save the library first with save_global_library (an earlier item of this call or an earlier call), "
                            + "or close it in TIA Portal to decide there.");
                        pending.Remove(key);
                    }

                    break;
                }
                default:
                    foreach (var (key, _) in LibraryUnsavedChanges.ChangedGlobalLibraries(item))
                    {
                        var changes = pending.TryGetValue(key, out var found) ? found : (Listed: new List<string>(), Count: 0);
                        changes.Listed.Add($"{item.Operation} ({item.OperationId}) earlier in this call");
                        pending[key] = (changes.Listed, changes.Count + 1);
                    }

                    break;
            }
        }
    }

    public async Task<ItemReplan<LibraryWriteEffect>> ReplanAsync(LibraryOperationRequest item)
    {
        var result = await _call(LibraryMethods.Plan, LibraryCatalog.ToRequest(item)).ConfigureAwait(false);
        // A planning response can carry worker warnings too, e.g. a TIA Portal dialog an earlier item's lingering
        // confirmation scope answered late; the call reports them at its root.
        foreach (var warning in result.Warnings ?? Array.Empty<string>())
            if (!_planWarnings.Contains(warning, StringComparer.Ordinal)) _planWarnings.Add(warning);
        if (!result.Success)
            return ItemReplan<LibraryWriteEffect>.Fail(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                result.Error ?? "Library planning failed.");
        var plan = LibraryPayloadContract.DecodePlan(item, result.Payload);
        if (plan is null)
            return ItemReplan<LibraryWriteEffect>.Fail(WorkerFailureCategories.ProtocolError, "Library planning evidence did not match its typed payload contract.");
        var effect = new LibraryWriteEffect(item.Operation, plan.Library?.Library, plan.Target, plan.AlreadyOpen,
            plan.Consequences.Append(PortalConfirmationConsequence(LibraryCatalog.PortalConfirmationPolicy(item))).ToArray());
        _guards[item.OperationId] = Guards(item, plan);
        if (item.Operation == LibraryMethods.SetPortalSetting) _plannedSecurity[item.OperationId] = IsSecurityRelevant(plan);
        return ItemReplan<LibraryWriteEffect>.Ok(ItemPlan<LibraryWriteEffect>.Resolved(effect));
    }

    /// <summary>
    /// The classification of a set_portal_setting from the folder and setting names the worker resolved in planning, never
    /// the caller's text (F2); a plan without the resolved setting is security-relevant (fail safe).
    /// </summary>
    internal static bool IsSecurityRelevant(LibraryPlanInfo plan)
        => plan.Setting is not { } setting || setting.SecurityRelevant || LibraryPolicy.IsSecurityRelevantResolved(setting.FolderPath, setting.Name, anyNameUnreadable: false);

    public Task<WorkerCallResult> MutateAsync(LibraryOperationRequest item)
    {
        var request = LibraryCatalog.ToRequest(item);
        // The worker resolves the setting again and refuses the change when it is classified differently from this plan (F2).
        if (item.Operation == LibraryMethods.SetPortalSetting)
            request.PlannedSecurityRelevant = _plannedSecurity.TryGetValue(item.OperationId, out var planned) ? planned : true;
        return _call(LibraryMethods.ForOperation(item.Operation), request);
    }

    public StructuredOperationItem Project(LibraryOperationRequest item, WorkerCallResult result)
    {
        var projected = LibraryPayloadContract.ProjectWrite(item, result);
        _unsaved.Observe(Portal, item, result, projected);
        return projected;
    }

    /// <summary>The planned consequence that states how the item's TIA Portal confirmation dialogs are answered.</summary>
    internal static string PortalConfirmationConsequence(string policy)
        => policy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? "TIA Portal confirmation dialogs raised by this item are answered Yes and recorded (portalConfirmations: accept)."
            : "TIA Portal confirmation dialogs raised by this item are answered No, recorded and fail the item (portalConfirmations: decline).";

    public async Task<WriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => LibraryVerificationPolicy.WithWorkerEvidence(
            await new TrustedWriteVerifier<LibraryOperationRequest>(_items, new LibraryVerificationPolicy(_items, _call,
                batch.Operations.Where(o => o.Result is not null).ToDictionary(o => o.OperationId, o => o.Result!.Value, StringComparer.Ordinal), _planWarnings))
                .VerifyAsync(batch).ConfigureAwait(false), batch);

    public bool VerificationSucceeded(WriteVerification? verification) => verification?.Success == true;

    public LibraryGuardedWriteResponse Compose(WriteReport<LibraryWriteEffect, WriteVerification> report)
        => LibraryWritePayloadBudget.Apply(new(ToolName, ContractVersion, report.Phase, report.Success, report.Error,
            report.Warnings.Concat(_planWarnings).Distinct(StringComparer.Ordinal).ToArray(), report.Guards, report.Effects.Select(e => new LibraryWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));

    /// <summary>The informative guards one planned item fires; messages carry the worker's resolved consequences.</summary>
    internal static IReadOnlyList<FiredGuard> Guards(LibraryOperationRequest item, LibraryPlanInfo plan)
    {
        var guards = new List<FiredGuard>();
        void Fire(string id, string message) => guards.Add(new(id, item.OperationId, message));
        string Consequence(string fallback) => plan.Consequences.FirstOrDefault() ?? fallback;
        switch (item.Operation)
        {
            case LibraryMethods.OpenGlobalLibrary when plan.AlreadyOpen:
                Fire(LibraryGuardDefinitions.AlreadyOpen, Consequence($"{item.Path} is already open."));
                break;
            case LibraryMethods.OpenGlobalLibrary when item.Upgrade == true:
                Fire(LibraryGuardDefinitions.UpgradesInPlace, $"OpenWithUpgrade may upgrade {item.Path} in place on disk.");
                Fire(LibraryGuardDefinitions.WritesFiles, $"{item.Path} may be rewritten by the upgrade.");
                break;
            case LibraryMethods.OpenGlobalLibrary when item.OpenMode == "ReadWrite":
                Fire(LibraryGuardDefinitions.WritesFiles, $"{item.Path} is opened read-write; save_global_library overwrites it.");
                break;
            case LibraryMethods.RetrieveGlobalLibrary:
                Fire(LibraryGuardDefinitions.WritesFiles, string.Join(" ", plan.Consequences));
                break;
            case LibraryMethods.CreateGlobalLibrary:
            case LibraryMethods.SaveGlobalLibrary:
            case LibraryMethods.ArchiveGlobalLibrary:
            case LibraryMethods.SaveGlobalLibraryAs:
                Fire(LibraryGuardDefinitions.WritesFiles, Consequence(plan.Target));
                break;
            case LibraryMethods.CloseGlobalLibrary:
                Fire(LibraryGuardDefinitions.CloseUnsaved, Consequence($"Closing {item.Library}."));
                break;
            case LibraryMethods.SetPortalSetting:
                Fire(LibraryGuardDefinitions.PortalSetting, plan.Target);
                if (IsSecurityRelevant(plan))
                    Fire(LibraryGuardDefinitions.PortalSecuritySetting, $"{plan.Target}: security-relevant TIA Portal setting; check that the new value does not weaken certificate, communication or project protection.");
                break;
            case LibraryMethods.OpenSecondaryProject or LibraryMethods.RetrieveSecondaryProject:
                Fire(LibraryGuardDefinitions.SecondaryProject, Consequence(plan.Target));
                if (item.Upgrade == true || item.Operation == LibraryMethods.RetrieveSecondaryProject)
                    Fire(LibraryGuardDefinitions.WritesFiles, item.Upgrade == true ? $"{item.Path} may be upgraded on disk." : string.Join(" ", plan.Consequences));
                break;
            case LibraryMethods.DeleteMasterCopy:
            case LibraryMethods.DeleteTypeVersion:
            case LibraryMethods.DiscardTypeVersion:
            case LibraryMethods.DeleteType:
            case LibraryMethods.DeleteTypeFolder:
            case LibraryMethods.DeleteMasterCopyFolder:
            case LibraryMethods.CleanUpLibrary:
                Fire(LibraryGuardDefinitions.Deletes, $"{plan.Target}: {string.Join(" ", plan.Consequences)}".TrimEnd(' ', ':'));
                break;
            case LibraryMethods.InstantiateMasterCopy when item.MasterCopyMode == "Replace":
                Fire(LibraryGuardDefinitions.ReplacesObjects, plan.Consequences.FirstOrDefault(c => c.Contains("Replace", StringComparison.Ordinal)) ?? plan.Target);
                break;
            case LibraryMethods.InstantiateTypeVersion when !LibraryPolicy.IsProject(item.Library):
                Fire(LibraryGuardDefinitions.SyncsProjectLibrary, $"{plan.Target}: the version is synchronized into the project library first.");
                break;
            case LibraryMethods.UpdateProject or LibraryMethods.UpdateLibrary
                when item.ForceUpdateMode is null && item.DeleteUnusedVersionsMode is null && item.StructureConflictResolutionMode is null:
                Fire(LibraryGuardDefinitions.UpdateDefaults, Consequence(plan.Target));
                break;
            case LibraryMethods.UpdateProject or LibraryMethods.UpdateLibrary when item.DeleteUnusedVersionsMode == "AutomaticallyDelete":
                Fire(LibraryGuardDefinitions.Deletes, $"{plan.Target}: DeleteUnusedVersionsMode AutomaticallyDelete deletes unused versions.");
                break;
            case LibraryMethods.HarmonizeProject:
                Fire(LibraryGuardDefinitions.ChangesProject, Consequence(plan.Target));
                break;
            case LibraryMethods.ExportTypeVersion or LibraryMethods.ExportTypeVersionAsDocuments:
                Fire(LibraryGuardDefinitions.ExportOverwritesFiles, Consequence(plan.Target));
                break;
            case LibraryMethods.CreateVersionFromDocuments when item.CreateOption == "Override":
                Fire(LibraryGuardDefinitions.ReplacesObjects, "CreateOptions Override overwrites the type's current in-work or in-test version.");
                break;
        }

        if (!LibraryMethods.IsLifecycle(item.Operation) && item.Library is not null && !LibraryPolicy.IsProject(item.Library)
            && item.Operation is not (LibraryMethods.InstantiateMasterCopy or LibraryMethods.InstantiateTypeVersion or LibraryMethods.UpdateProject
                or LibraryMethods.HarmonizeProject or LibraryMethods.UpdateLibrary or LibraryMethods.ExportTypeVersion or LibraryMethods.ExportTypeVersionAsDocuments))
            Fire(LibraryGuardDefinitions.GlobalUnsaved, $"{item.Operation} changes the global library {item.Library}; save it with save_global_library.");
        if (item.Operation is LibraryMethods.CopyMasterCopy or LibraryMethods.UpdateLibrary && item.TargetLibrary is not null && !LibraryPolicy.IsProject(item.TargetLibrary))
            Fire(LibraryGuardDefinitions.GlobalUnsaved, $"{item.Operation} changes the global library {item.TargetLibrary}; save it with save_global_library.");
        return guards;
    }

    /// <summary>Libraries and project content an item reads, for dependency ordering.</summary>
    internal static IReadOnlyList<string> Touches(LibraryOperationRequest item)
    {
        var keys = new List<string>();
        if (LibraryCatalog.LibraryKey(item) is { } library) keys.Add(library);
        if (LibraryCatalog.Key(item.TargetLibrary) is { } target) keys.Add(target);
        // A capture from a secondary project depends on an item that opens (or closes) that project earlier in the call.
        if (LibraryCatalog.SecondaryKey(item.Source?.Project) is { } secondary) keys.Add(secondary);
        return keys;
    }

    /// <summary>Further keys an item changes: instantiation and updates change the project library (a global version is synchronized into it).</summary>
    private static IEnumerable<string> Changes(LibraryOperationRequest item)
        => item.Operation is LibraryMethods.InstantiateTypeVersion or LibraryMethods.UpdateProject or LibraryMethods.HarmonizeProject
            ? new[] { LibraryPolicy.ProjectLibrary } : Array.Empty<string>();

    private static int IndexOf(IReadOnlyList<LibraryOperationRequest> items, string operationId)
    {
        for (var index = 0; index < items.Count; index++)
            if (items[index].OperationId == operationId) return index;
        return -1;
    }

}
