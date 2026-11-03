using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// Plans the host-side SimaticML document edits adopted from BetterTIA/writer (<see cref="PlcBlockEditMethods.DocumentEdits"/>)
/// like the interface edits of <see cref="PlcInterfaceEditPlanner"/>: the block's identity is read (know-how-protected
/// blocks refused, their export carries only the interface), the xml export (or an earlier planned document of the call)
/// is edited, and the mutation imports exactly that document through update_block_logic / update_type_content against
/// the export's hash. An edit that changes nothing is refused before anything runs, because re-importing an unchanged
/// block can still leave its instance DB and callers inconsistent.
/// </summary>
internal static partial class PlcDocumentEditPlanner
{
    public static async Task<PlcEditResolution> ResolveAsync(
        OpennessWorkerClient client,
        PlcOperationRequest item,
        PlcWriteEffect effect,
        string? dependsOn,
        Dictionary<string, (string Content, string OperationId)> plannedDocuments,
        PlcContentDiffBudget budget,
        PlcSafetyPlanning? safety)
    {
        var isType = item.TypePath is not null;
        var guards = new List<FiredGuard>();
        string key;
        string? interfaceHash = null;
        if (!isType)
        {
            var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetBlockInterface, new PlcBlockEditRequestInfo { BlockPath = item.BlockPath }).ConfigureAwait(false);
            if (!read.Success && dependsOn is not null && read.FailureCategory == WorkerFailureCategories.TargetNotFound)
                return Deferred(effect, guards);
            var (info, failure) = PlcInterfaceEditPlanner.Decode<PlcBlockInterfaceInfo>(item, PlcBlockEditMethods.GetBlockInterface, read, "the block identity could not be read");
            if (failure is not null) return failure with { Effect = effect };
            if (info!.Block.IsKnowHowProtected != false)
                return Fail(effect, WorkerFailureCategories.TargetKindUnsupported, info.Block.IsKnowHowProtected == true
                    ? $"Operation '{item.OperationId}': '{info.Block.BlockPath}' is know-how protected; its export carries only the interface, so it is not edited."
                    : $"Operation '{item.OperationId}': the know-how protection of '{info.Block.BlockPath}' could not be read, so it is not edited.");
            if (KindError(item, info.Block) is { } kindError) return Fail(effect, WorkerFailureCategories.TargetKindUnsupported, $"Operation '{item.OperationId}': {kindError}");
            effect = effect with { Target = effect.Target with { BlockPath = info.Block.BlockPath, Name = info.Block.Name } };
            key = "block:" + info.Block.BlockPath.ToUpperInvariant();
            interfaceHash = info.ContentHash;
        }
        else
        {
            key = "type:" + PlcTypeAddress.Parse(item.TypePath!).ToDisplayPath().ToUpperInvariant();
        }

        guards.Add(new FiredGuard(GuardFor(item), item.OperationId, GuardMessage(item, isType ? item.TypePath! : effect.Target.BlockPath!)));

        string baseContent;
        string? baseHash = null;
        var preconditions = new List<CheckedPrecondition>();
        if (plannedDocuments.TryGetValue(key, out var prior))
        {
            baseContent = prior.Content;
        }
        else if (dependsOn is not null)
        {
            return Deferred(effect, guards);
        }
        else
        {
            var export = isType
                ? await client.GetTypeContentAsync(item.TypePath!, format: SourceFormatNames.Xml).ConfigureAwait(false)
                : await client.GetBlockContentAsync(item.BlockPath!, format: SourceFormatNames.Xml).ConfigureAwait(false);
            if (!export.Success && export.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
                return Fail(effect, export.FailureCategory!, $"Operation '{item.OperationId}': {export.Error}");
            if (!export.Success || string.IsNullOrEmpty(export.Payload))
                return Unverifiable(item, effect, guards, $"the current document could not be exported ({(export.Success ? "the export was empty" : export.Error)})");
            baseContent = export.Payload;
            baseHash = ContentHashes.Compute(SourceFormatNames.Xml, baseContent);
            if (interfaceHash is not null && !string.Equals(interfaceHash, baseHash, StringComparison.Ordinal))
                return Fail(effect, WorkerFailureCategories.StateChanged, $"Operation '{item.OperationId}': '{effect.Target.BlockPath}' changed while the write was planned. Retry.");
            if (item.ExpectedContentHash is not null)
            {
                var check = ContentHashes.Check(item.ExpectedContentHash, SourceFormatNames.Xml, baseContent);
                if (!check.Success) return Fail(effect, check.ErrorCategory!, $"Operation '{item.OperationId}': {check.Message}");
                preconditions.Add(check.Evidence!);
            }
        }

        if (baseHash is null && item.ExpectedContentHash is not null)
            return Fail(effect, WorkerFailureCategories.ValidationError,
                $"Operation '{item.OperationId}': an earlier item of this call changes the same object, so expectedContentHash cannot describe the document this edit applies to. Omit it; the edit is re-planned against the real state before it runs.");

        string document;
        if (isType) document = baseContent;
        else if (!PlcBlockBundleText.TryGetSimaticMl(baseContent, out _, out document))
            return Unverifiable(item, effect, guards, Contracts.Plc.Interface.SimaticMlExportNotes.MissingInPlanning);

        SimaticMlInterfaceDocument parsed;
        DocumentEditOutcome outcome;
        try
        {
            parsed = SimaticMlInterfaceDocument.Parse(document);
            if (parsed.IsSafety && safety is not null)
            {
                // A fail-safe block: TIA Portal's rule (a login only when a safety password is set), with the acceptance guard.
                var (safetyGuards, _) = await safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' ({item.Operation} of '{effect.Target.BlockPath}')").ConfigureAwait(false);
                parsed.AllowSafety = true;
                guards.AddRange(safetyGuards);
            }

            outcome = Apply(item, parsed);
        }
        catch (PlcInterfaceEditException exception)
        {
            return Fail(effect, exception.Category, $"Operation '{item.OperationId}': {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return Fail(effect, WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {exception.Message}");
        }

        if (outcome.Changes.Count == 0)
            return Fail(effect, WorkerFailureCategories.ValidationError,
                $"Operation '{item.OperationId}': {item.Operation} changes nothing in '{(isType ? item.TypePath : effect.Target.BlockPath)}'{outcome.NothingChangedReason}; nothing is imported, because a re-import of an unchanged block can still leave its instance DB and callers inconsistent.");

        var edited = parsed.Serialize();
        var content = isType ? edited : PlcBlockBundleText.ReplaceSimaticMl(baseContent, edited);
        plannedDocuments[key] = (content, item.OperationId);
        foreach (var warning in outcome.Warnings)
            guards.Add(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId, $"Operation '{item.OperationId}': {warning}"));
        effect = effect with
        {
            Changes = outcome.Changes,
            ContentDiff = PlcContentDiff.Build(baseContent, content, budget),
        };
        return new PlcEditResolution(effect, guards, preconditions,
            baseHash is null ? null : new PlcPlannedContent(content, baseHash, isType, null, outcome.TextChanges, outcome.Expectations), null);
    }

    /// <summary>What one document edit changed: the effect's changes, the verification evidence and warnings.</summary>
    internal sealed record DocumentEditOutcome(
        IReadOnlyList<PlcFieldChange> Changes,
        IReadOnlyList<PlcTextChangeInfo>? TextChanges,
        IReadOnlyList<PlcInterfaceExpectation>? Expectations,
        IReadOnlyList<string> Warnings,
        string NothingChangedReason);

    private static DocumentEditOutcome Apply(PlcOperationRequest item, SimaticMlInterfaceDocument parsed)
    {
        switch (item.Operation)
        {
            case PlcBlockEditMethods.SetNetworkText:
            {
                var changes = SimaticMlNetworkTexts.SetNetworkText(parsed.Document, item.NetworkNumber!.Value, item.Title, item.Comment, item.Culture);
                return Texts(changes, " (the texts are already as requested)");
            }
            case PlcBlockEditMethods.ReplaceNetworkText:
            {
                var changes = SimaticMlNetworkTexts.ReplaceTexts(parsed.Document, item.Replacements!, item.Culture, item.TextScope ?? SimaticMlNetworkTexts.ScopeNetworks);
                return Texts(changes, " (no find matched a text in scope)");
            }
            case PlcBlockEditMethods.RenameMembers:
            {
                var renamed = SimaticMlMemberRenames.Apply(parsed, item.MemberRenames!, item.TypePath is not null);
                return new(renamed.Changes.Select(c => new PlcFieldChange(c.Field, c.Current, c.Requested)).ToArray(), null, renamed.Expectations,
                    renamed.Warnings, " (every member already carries the requested name)");
            }
            default:
                throw new ArgumentException($"'{item.Operation}' is not a document edit.");
        }
    }

    private static DocumentEditOutcome Texts(IReadOnlyList<PlcTextChangeInfo> changes, string none)
        => new(changes.Select(c => new PlcFieldChange($"{c.Where} [{c.Culture}]", c.Old, c.New)).ToArray(), changes, null, Array.Empty<string>(), none);

    private static string? KindError(PlcOperationRequest item, PlcBlockIdentityInfo block)
        => item.Operation is PlcBlockEditMethods.SetNetworkText or PlcBlockEditMethods.ReplaceNetworkText
            && block.BlockType is not ("OB" or "FB" or "FC")
            ? $"'{block.BlockPath}' is a {block.BlockType}; only OB, FB and FC have networks."
            : item.Operation == PlcBlockEditMethods.RenameMembers && block.BlockType is "InstanceDB" or "ArrayDB"
                ? $"'{block.BlockPath}' is an {(block.BlockType == "InstanceDB" ? $"instance DB of '{block.InstanceOfName}'; rename the FB's members" : "array DB; its interface is not edited here")}."
                : null;

    private static string GuardFor(PlcOperationRequest item)
        => item.Operation == PlcBlockEditMethods.RenameMembers ? PlcGuardDefinitions.ChangesInterface : PlcGuardDefinitions.ReimportsBlockForText;

    private static string GuardMessage(PlcOperationRequest item, string path)
        => item.Operation == PlcBlockEditMethods.RenameMembers
            ? $"rename_members re-imports '{path}' with the members renamed and the object's own accesses to them updated, as the TIA Portal editor does; accesses in other blocks (\"DB\".member, instance and multi-instance members, formal parameters at calls), HMI tags, watch tables and technology object parameters are not updated, and instance DBs, multi-instances and callers stay inconsistent until compiled. Read plc_read analyze_change_impact first."
            : $"{item.Operation} re-imports the whole block '{path}' (Openness has no setter for network texts); TIA Portal can mark its instance DB and its callers inconsistent even when only a text changed (reported by BetterTIA, not verified). Compile afterwards.";

    private static PlcEditResolution Deferred(PlcWriteEffect effect, List<FiredGuard> guards)
        => new(effect, guards, Array.Empty<CheckedPrecondition>(), null, null);

    private static PlcEditResolution Unverifiable(PlcOperationRequest item, PlcWriteEffect effect, List<FiredGuard> guards, string reason)
        => new(effect, guards.Append(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
            $"Operation '{item.OperationId}': {reason}, so the edit cannot be planned.")).ToArray(), Array.Empty<CheckedPrecondition>(), null, null);

    private static PlcEditResolution Fail(PlcWriteEffect effect, string category, string message)
        => new(effect, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), null, new WriteToolError(category, message));
}
