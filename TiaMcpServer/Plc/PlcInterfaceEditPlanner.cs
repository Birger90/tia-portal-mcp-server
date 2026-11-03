using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>The document an interface edit imports, the hash of the export it was computed from, and what a re-read must show.</summary>
/// <remarks>
/// A document edit (<see cref="PlcBlockEditMethods.DocumentEdits"/>) carries <see cref="TextChanges"/> (network texts) or
/// <see cref="Expectations"/> (one per renamed member) instead of the single interface <see cref="Expectation"/>.
/// </remarks>
/// A SIMATIC SD network edit carries <see cref="Network"/> and <see cref="Format"/> sd.
internal sealed record PlcPlannedContent(string Content, string BaseHash, bool IsType, PlcInterfaceExpectation? Expectation,
    IReadOnlyList<PlcTextChangeInfo>? TextChanges = null, IReadOnlyList<PlcInterfaceExpectation>? Expectations = null,
    string Format = SourceFormatNames.Xml, PlcSdNetworkExpectation? Network = null);

/// <summary>One granular item resolved against fresh reads: its effect and guards, the planned document, or an error.</summary>
internal sealed record PlcEditResolution(
    PlcWriteEffect Effect,
    IReadOnlyList<FiredGuard> Guards,
    IReadOnlyList<CheckedPrecondition> Preconditions,
    PlcPlannedContent? Planned,
    WriteToolError? Error);

/// <summary>
/// Plans add/update/delete_interface_member and update_db_member from public reads. An interface edit
/// reads the block's identity (refusing know-how-protected blocks), exports the document in xml,
/// applies the edit with <see cref="SimaticMlInterfaceDocument"/> and keeps the result: the mutation
/// imports exactly that document through update_block_logic / update_type_content with the hash of
/// the export it was computed from, so a change in between fails with state_changed. A later edit of
/// the same object in the call previews against the earlier planned document and is re-planned
/// against the real state before its own mutation.
/// </summary>
internal static class PlcInterfaceEditPlanner
{
    public static async Task<PlcEditResolution> ResolveAsync(
        OpennessWorkerClient client,
        PlcOperationRequest item,
        PlcWriteEffect effect,
        string? dependsOn,
        Dictionary<string, (string Content, string OperationId)> plannedDocuments,
        PlcContentDiffBudget budget,
        PlcSafetyPlanning? safety = null)
    {
        var isType = item.TypePath is not null;
        var guards = new List<FiredGuard>();
        string key;
        string? interfaceHash = null;
        if (!isType)
        {
            var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetBlockInterface, new PlcBlockEditRequestInfo { BlockPath = item.BlockPath }).ConfigureAwait(false);
            if (!read.Success && dependsOn is not null && read.FailureCategory == WorkerFailureCategories.TargetNotFound)
                return Deferred(item, effect, guards);
            var (info, failure) = Decode<PlcBlockInterfaceInfo>(item, PlcBlockEditMethods.GetBlockInterface, read, "the block interface could not be read");
            if (failure is not null) return failure with { Effect = effect };
            if (info!.Block.IsKnowHowProtected != false)
                return Fail(effect, WorkerFailureCategories.TargetKindUnsupported, info.Block.IsKnowHowProtected == true
                    ? $"Operation '{item.OperationId}': '{info.Block.BlockPath}' is know-how protected; its interface is not edited."
                    : $"Operation '{item.OperationId}': the know-how protection of '{info.Block.BlockPath}' could not be read, so its interface is not edited.");
            if (info.Block.BlockType is "InstanceDB" or "ArrayDB")
                return Fail(effect, WorkerFailureCategories.TargetKindUnsupported, info.Block.BlockType == "InstanceDB"
                    ? $"Operation '{item.OperationId}': '{info.Block.BlockPath}' is an instance DB; its interface comes from FB '{info.Block.InstanceOfName}'. Edit the FB, or set start values with update_db_member."
                    : $"Operation '{item.OperationId}': '{info.Block.BlockPath}' is an array DB; its interface is not edited here.");
            effect = effect with { Target = effect.Target with { BlockPath = info.Block.BlockPath, Name = info.Block.Name } };
            key = "block:" + info.Block.BlockPath.ToUpperInvariant();
            interfaceHash = info.ContentHash;
        }
        else
        {
            key = "type:" + PlcTypeAddress.Parse(item.TypePath!).ToDisplayPath().ToUpperInvariant();
        }

        guards.Add(new FiredGuard(PlcGuardDefinitions.ChangesInterface, item.OperationId,
            $"Operation '{item.OperationId}': {item.Operation} re-imports '{(isType ? item.TypePath : effect.Target.BlockPath)}'; instance DBs, multi-instances and callers stay inconsistent until compiled, and TIA Portal may refuse a structural change of an FB that has instances."));

        string baseContent;
        string? baseHash = null;
        var preconditions = new List<CheckedPrecondition>();
        if (plannedDocuments.TryGetValue(key, out var prior))
        {
            baseContent = prior.Content;
        }
        else if (dependsOn is not null)
        {
            return Deferred(item, effect, guards);
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
        if (isType)
        {
            document = baseContent;
        }
        else if (!PlcBlockBundleText.TryGetSimaticMl(baseContent, out _, out document))
        {
            return Unverifiable(item, effect, guards, Contracts.Plc.Interface.SimaticMlExportNotes.MissingInPlanning);
        }

        SimaticMlInterfaceDocument parsed;
        PlcInterfaceEditOutcome outcome;
        try
        {
            parsed = SimaticMlInterfaceDocument.Parse(document);
            if (parsed.IsSafety && safety is not null)
            {
                // A fail-safe block: edited under TIA Portal's rule (a login only when a safety password is set; the
                // block guard plc_safety_login_required otherwise), with the acceptance guard; the worker re-checks.
                var (safetyGuards, _) = await safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' ({item.Operation} of '{effect.Target.BlockPath}')").ConfigureAwait(false);
                parsed.AllowSafety = true;
                guards.AddRange(safetyGuards);
            }

            outcome = parsed.Apply(Request(item));
        }
        catch (PlcInterfaceEditException exception)
        {
            return Fail(effect, exception.Category, $"Operation '{item.OperationId}': {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return Fail(effect, WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {exception.Message}");
        }

        var edited = parsed.Serialize();
        var content = isType ? edited : PlcBlockBundleText.ReplaceSimaticMl(baseContent, edited);
        plannedDocuments[key] = (content, item.OperationId);
        effect = effect with
        {
            Changes = outcome.Changes.Select(change => new PlcFieldChange(change.Field, change.Current, change.Requested)).ToArray(),
            ContentDiff = PlcContentDiff.Build(baseContent, content, budget),
        };
        return new PlcEditResolution(effect, guards, preconditions,
            baseHash is null ? null : new PlcPlannedContent(content, baseHash, isType, outcome.Expectation), null);
    }

    public static async Task<PlcEditResolution> ResolveDbMemberAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, string? dependsOn, PlcSafetyPlanning? safety = null)
    {
        var requested = DbChanges(item);
        var guards = new List<FiredGuard>();
        if (dependsOn is not null)
            return new PlcEditResolution(effect with { Changes = requested.Select(r => new PlcFieldChange(r.Field, null, r.Requested)).ToArray() },
                guards, Array.Empty<CheckedPrecondition>(), null, null);

        var withAttributes = requested.Any(r => r.Field != "startValue") ? true : (bool?)null;
        var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetDbValues,
            new PlcBlockEditRequestInfo { BlockPath = item.BlockPath, MemberPath = item.MemberPath, WithAttributes = withAttributes }).ConfigureAwait(false);
        if (!read.Success && read.FailureCategory is WorkerFailureCategories.TargetKindUnsupported or WorkerFailureCategories.ValidationError)
            return Fail(effect, read.FailureCategory!, $"Operation '{item.OperationId}': {read.Error}");
        var (values, failure) = Decode<PlcDbValuesInfo>(item, PlcBlockEditMethods.GetDbValues, read, "the current DB values could not be read");
        if (failure is not null) return failure with { Effect = effect };

        var path = PlcMemberPath.Parse(item.MemberPath!, allowIndices: true).ToFindPath();
        var exact = values!.Values.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
        var below = values.Values.Any(v => v.Kind != PlcDbValueKinds.Missing && PlcDbValueTree.IsPrefix(path, v.Path));
        if (exact is { Kind: PlcDbValueKinds.Missing } || exact is null && !below)
            return Fail(effect, WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': member '{path}' was not found in '{values.Block.BlockPath}'.");

        var current = exact is null || exact.Kind == PlcDbValueKinds.Missing ? null : exact;
        effect = effect with
        {
            Target = effect.Target with { BlockPath = values.Block.BlockPath, Name = values.Block.Name },
            Changes = requested.Select(r => new PlcFieldChange(r.Field, Current(current, r.Field), r.Requested)).ToArray(),
        };
        if (safety is not null && Contracts.Plc.Safety.PlcSafetyPolicy.IsFailSafeLanguage(values.Block.ProgrammingLanguage))
        {
            // An F-DB: TIA Portal's rule (login only when a safety password is set), with the acceptance guard.
            var (safetyGuards, _) = await safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' (update_db_member of '{values.Block.BlockPath}')").ConfigureAwait(false);
            guards.AddRange(safetyGuards);
        }

        return new PlcEditResolution(effect, guards, Array.Empty<CheckedPrecondition>(), null, null);
    }

    internal static IReadOnlyList<(string Field, string Requested)> DbChanges(PlcOperationRequest item)
    {
        var changes = new List<(string, string)>();
        if (item.StartValue is not null) changes.Add(("startValue", item.StartValue));
        if (item.ExternalAccessible is { } accessible) changes.Add(("externalAccessible", Flag(accessible)));
        if (item.ExternalVisible is { } visible) changes.Add(("externalVisible", Flag(visible)));
        if (item.ExternalWritable is { } writable) changes.Add(("externalWritable", Flag(writable)));
        if (item.Retain is { } retain) changes.Add(("retain", Flag(retain)));
        if (item.SetPoint is { } setPoint) changes.Add(("setPoint", Flag(setPoint)));
        return changes;
    }

    internal static string? Current(PlcDbValueInfo? value, string field) => field switch
    {
        "startValue" => value?.StartValue,
        "externalAccessible" => Flag(value?.Attributes?.ExternalAccessible),
        "externalVisible" => Flag(value?.Attributes?.ExternalVisible),
        "externalWritable" => Flag(value?.Attributes?.ExternalWritable),
        "retain" => Flag(value?.Attributes?.Retain),
        "setPoint" => Flag(value?.Attributes?.SetPoint),
        _ => null,
    };

    internal static PlcInterfaceEditRequest Request(PlcOperationRequest item) => new()
    {
        Kind = item.Operation switch
        {
            PlcBlockEditMethods.AddInterfaceMember => PlcInterfaceEditKind.Add,
            PlcBlockEditMethods.UpdateInterfaceMember => PlcInterfaceEditKind.Update,
            _ => PlcInterfaceEditKind.Delete,
        },
        Section = item.TypePath is not null ? "None" : item.Section!,
        Path = PlcMemberPath.Parse(item.MemberPath!, allowIndices: false),
        NewName = item.NewName,
        DataType = item.DataType,
        StartValue = item.StartValue,
        ClearStartValue = item.ClearStartValue == true,
        Comment = item.Comment,
        CommentCulture = item.CommentCulture,
        InsertBefore = item.InsertBefore,
    };

    /// <summary>Projects a granular worker read through its declared contract and decodes it.</summary>
    internal static (T? Value, PlcEditResolution? Failure) Decode<T>(PlcOperationRequest item, string operation, WorkerCallResult read, string what)
        where T : class
    {
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = operation }, read);
        if (projected.Result is { } result) return (CanonicalJson.Deserialize<T>(result.GetRawText()), null);
        var category = projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed;
        var message = $"Operation '{item.OperationId}': {what}: {projected.Failure?.Message}";
        return (null, category is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous or WorkerFailureCategories.ProtocolError
                or WorkerFailureCategories.ValidationError or WorkerFailureCategories.TargetKindUnsupported
            ? new PlcEditResolution(null!, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), null, new WriteToolError(category, message))
            : new PlcEditResolution(null!, new[] { new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId, message) },
                Array.Empty<CheckedPrecondition>(), null, null));
    }

    private static PlcEditResolution Deferred(PlcOperationRequest item, PlcWriteEffect effect, List<FiredGuard> guards)
        => new(effect, guards, Array.Empty<CheckedPrecondition>(), null, null);

    private static PlcEditResolution Unverifiable(PlcOperationRequest item, PlcWriteEffect effect, List<FiredGuard> guards, string reason)
        => new(effect, guards.Append(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
            $"Operation '{item.OperationId}': {reason}, so the edit cannot be planned.")).ToArray(), Array.Empty<CheckedPrecondition>(), null, null);

    private static PlcEditResolution Fail(PlcWriteEffect effect, string category, string message)
        => new(effect, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), null, new WriteToolError(category, message));

    private static string Flag(bool value) => value ? "true" : "false";

    private static string? Flag(bool? value) => value is { } flag ? Flag(flag) : null;
}
