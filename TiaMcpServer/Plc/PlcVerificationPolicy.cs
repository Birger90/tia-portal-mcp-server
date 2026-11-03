using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

public sealed partial class PlcVerificationPolicy(OpennessWorkerClient client, IReadOnlyDictionary<string, PlcWriteEffect> effects)
    : IWriteVerificationPolicy<PlcOperationRequest>
{
    /// <summary>Check names of the granular block editing read-backs.</summary>
    public const string InterfaceMemberCheck = "interfaceMember";
    public const string DbMemberCheck = "dbMember";
    public const string BlockCheck = "block";
    public const string TagTableCheck = "tagTable";

    private IReadOnlyDictionary<string, PlcPlannedContent> _planned = new Dictionary<string, PlcPlannedContent>();
    private IReadOnlyDictionary<string, JsonElement> _results = new Dictionary<string, JsonElement>();
    private readonly Dictionary<string, string> _contentHashes = new(StringComparer.Ordinal);

    public string ReadTool => "plc_read";

    /// <summary>The source, folder and unit operations do not read their objects back; say so instead of "verified there".</summary>
    public string? SupersessionEvidence(PlcOperationRequest item, PlcOperationRequest later)
        => PlcWorkingState.HandlesSourceFolder(later.Operation) && effects.TryGetValue(item.OperationId, out var earlier) && effects.TryGetValue(later.OperationId, out var latest)
            ? PlcWorkingState.SourceFolderSupersession(earlier.Target, later, latest.Target)
            : null;

    public bool HasReadBack(PlcOperationRequest item)
        => item.Operation is "update_block_logic" or "update_type_content" || PlcWorkingState.IsWatchContentUpdate(item.Operation)
            || PlcBlockEditMethods.PlannedEdits.Contains(item.Operation, StringComparer.Ordinal)
            || PlcBlockEditMethods.Write.Contains(item.Operation, StringComparer.Ordinal) && item.Operation is not (PlcBlockEditMethods.ExportTagTable or PlcBlockEditMethods.ExportOpcUaXml)
            || PlcAlarms.PlcAlarmVerification.HasReadBack(item.Operation)
            || item.Operation == Contracts.Project.ProjectCompilationSettingsMethods.Set
            || item.Operation == Contracts.Plc.Loadable.PlcLoadableMethods.GenerateLoadable
            || PlcBlockProtection.HasReadBack(item, _results)
            || PlcProjectLanguages.HasReadBack(item, _results)
            || Contracts.Plc.Settings.PlcSettingsCatalog.IsWrite(item.Operation)
            || Contracts.Plc.OpcUa.PlcOpcUaMethods.IsWrite(item.Operation)
            || Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsWorkerMethod(item.Operation)
            || HasTechnologyReadBack(item)
            || HandlesTagOrBlockReadBack(item) || HasAccessLevelReadBack(item) || HasFileOrSafetyReadBack(item);

    public static Task<PlcWriteVerification> VerifyAsync(OpennessWorkerClient client,
        IReadOnlyList<PlcOperationRequest> items, IReadOnlyDictionary<string, PlcWriteEffect> effects,
        StructuredOperationBatch batch)
        => VerifyAsync(client, items, effects, batch, new Dictionary<string, PlcPlannedContent>());

    internal static async Task<PlcWriteVerification> VerifyAsync(OpennessWorkerClient client,
        IReadOnlyList<PlcOperationRequest> items, IReadOnlyDictionary<string, PlcWriteEffect> effects,
        StructuredOperationBatch batch, IReadOnlyDictionary<string, PlcPlannedContent> planned,
        IReadOnlyDictionary<string, PlcV21PitfallPlan>? pitfalls = null, IReadOnlyDictionary<string, PlcMainSafetyLinkPlan>? safetyLinks = null)
    {
        var policy = new PlcVerificationPolicy(client, effects)
        {
            _planned = planned,
            _pitfalls = pitfalls ?? new Dictionary<string, PlcV21PitfallPlan>(),
            _safetyLinks = safetyLinks ?? new Dictionary<string, PlcMainSafetyLinkPlan>(),
            _results = batch.Operations.Where(o => o.Result is not null).ToDictionary(o => o.OperationId, o => o.Result!.Value, StringComparer.Ordinal),
        };
        return PlcWriteVerification.From(await new TrustedWriteVerifier<PlcOperationRequest>(items, policy).VerifyAsync(batch).ConfigureAwait(false),
            policy._contentHashes);
    }

    /// <summary>Hierarchical object keys: a container's key prefixes its members' keys.</summary>
    public IReadOnlyList<string> ObjectKeys(PlcOperationRequest item, bool asLater)
    {
        var target = effects[item.OperationId].Target;
        if (SideFileExports.Contains(item.Operation)) return new[] { SideFileKey(item.FilePath) };
        if (PlcWorkingState.IsAlarmOperation(item.Operation))
            return PlcAlarms.PlcAlarmVerification.ObjectKeys(item, target, asLater).Select(k => k.ToUpperInvariant()).Distinct().ToArray();
        var scope = $"{target.DeviceName}\n{target.PlcName}\n";
        // Folders are split into segments and leaves marked (PlcObjectKeys), so a folder or group key prefixes
        // everything inside it at any depth and a leaf never prefixes a same-named container.
        var table = PlcObjectKeys.LeafIn($"{scope}table", target.FolderPath, target.TableName);
        var keys = PlcWorkingState.HandlesSourceFolder(item.Operation) ? PlcWorkingState.SourceFolderObjectKeys(item, target, scope, asLater).ToArray() : target.Kind switch
        {
            PlcNameRules.Tag when item.Operation == PlcTagMoveMethods.MoveTag
                => new[] { $"{table}\n{target.Name}", $"{PlcObjectKeys.LeafIn($"{scope}table", NormalizeFolder(item.TargetFolderPath), item.TargetTableName)}\n{target.Name}" },
            PlcNameRules.Tag or PlcNameRules.UserConstant => new[] { $"{table}\n{target.Name}", $"{table}\n{item.NewName ?? target.Name}" },
            PlcNameRules.TagTable when item.Operation == PlcBlockEditMethods.MoveTagTable
                => new[] { table, PlcObjectKeys.LeafIn($"{scope}table", NormalizeFolder(item.TargetFolderPath), target.TableName) },
            PlcNameRules.TagTable => new[] { table },
            "Type" => MemberScoped(item, new[] { PlcObjectKeys.Leaf($"{scope}type", target.TypePath) }, asLater),
            // Watch/force tables share one namespace per group; a group key prefixes its tables' keys.
            PlcWorkingState.WatchTableKind or PlcWorkingState.ForceTableKind => new[] { PlcObjectKeys.LeafIn($"{scope}watch", target.FolderPath, target.TableName) },
            PlcWorkingState.WatchTableGroupKind => new[] { PlcObjectKeys.Container($"{scope}watch", target.FolderPath) },
            PlcWorkingState.PlcSoftwareKind => new[] { $"{scope}software" },
            // Each CPU protection setting is its own object; a later item on the same setting supersedes an earlier one.
            PlcWorkingState.PlcSecurityKind => new[] { $"{scope}security\n{target.Name}" },
            // The project's compilation settings are one project-wide object.
            PlcCompilationSettings.Kind => new[] { "project\ncompilationsettings" },
            // A loadable file is one file, whatever PLC wrote it (the key of the alarm file exports).
            PlcLoadable.Kind => new[] { PlcAlarms.PlcAlarmVerification.FileKey(item.FilePath) },
            // A block's know-how protection and its write protection are two objects; a later item on the same one supersedes.
            PlcBlockProtection.Kind => new[] { $"{scope}blockprotection\n{target.BlockPath}\n{target.Name}" },
            // The project's languages are one object; a text export is its file; each import owns its own key.
            PlcProjectLanguages.LanguagesKind or PlcProjectLanguages.TextsKind => PlcProjectLanguages.ObjectKeys(item, target),
            // A settings item verifies from its own worker read-back right after the call; it neither supersedes nor is superseded.
            PlcSettings.Kind => new[] { $"settings\n{item.OperationId}" },
            PlcOpcUa.Kind => PlcOpcUa.ObjectKeys(item, target, scope).ToArray(),
            PlcDeviceData.Kind => PlcDeviceData.ObjectKeys(item, scope).ToArray(),
            // A batch rename owns its own key: it neither supersedes nor is superseded by single-object items.
            PlcWorkingState.BatchKind => new[] { $"{scope}batch\n{item.OperationId}" },
            PlcWorkingState.TechnologyObjectKind or PlcWorkingState.TechnologyObjectGroupKind => TechnologyObjectKeys(item, target, scope).Concat(ImportedObjectKeys(item, scope)).ToArray(),
            // A fail-safe access write owns the PLC's safety access state; a runtime group create owns only its group.
            PlcSafetyPlanning.AdministrationKind => new[] { $"{scope}safetyaccess" },
            PlcSafetyPlanning.RuntimeGroupKind => new[] { $"{scope}safetyruntimegroup\n{target.Name}" },
            _ => BlockKeys(item, target, scope, asLater),
        };
        if (asLater) keys = keys.Concat(PlcAlarms.PlcAlarmVerification.LaterBlockKeys(item.Operation, scope)).ToArray();
        return keys.Select(k => k.ToUpperInvariant()).Distinct().ToArray();
    }

    /// <summary>
    /// K59 (live V21 2026-11-02: create_technology_object followed by a failed export_technology_object of the same name said
    /// "superseded by later operation"): an export writes a file outside the project and changes no project object, so it
    /// owns and offers only the key of the file it writes. It never supersedes an item on the exported object, and a later
    /// item on that object never supersedes the export's own file check; only a later write of the same file does. The other
    /// exports (export_source_file, the CSV exports, export_blocks, the alarm, text list and ProDiag exports) already own only
    /// their file or directory key (PlcWorkingState.SourceFolderObjectKeys, PlcAlarmVerification.ObjectKeys).
    /// </summary>
    internal static readonly IReadOnlySet<string> SideFileExports = new HashSet<string>(StringComparer.Ordinal)
    {
        TechnologyObjectMethods.Export, PlcBlockEditMethods.ExportTagTable, PlcBlockEditMethods.ExportOpcUaXml,
    };

    private static string SideFileKey(string? path) => "exportfile\n" + PlcAlarms.PlcAlarmVerification.FileKey(path);

    /// <summary>
    /// A block's path key. An undetermined <c>PLC/Name</c> content target (never resolved through the
    /// tree) also owns a name key and a PLC-wide group-delete key, and every later block item offers
    /// its name and every later group delete offers that key: a later item on the same block name, or
    /// any later group delete on the PLC (the target's folder is unknown), supersedes it. Canonical
    /// targets never own those keys, so their matching stays path-exact. Rename, copy and move also
    /// own the resulting block's path (a copy owns only that one).
    /// </summary>
    private static string[] BlockKeys(PlcOperationRequest item, PlcTargetIdentity target, string scope, bool asLater)
    {
        var path = target.BlockPath ?? item.BlockPath ?? "";
        var keys = new List<string>();
        // A group operation owns its key in the container namespace and, as a later item, also offers the group's
        // content prefix (PlcObjectKeys.ContainerOwn); a block is a marked leaf.
        if (target.Kind == PlcNameRules.BlockGroup)
        {
            keys.Add(PlcObjectKeys.ContainerOwn(scope, "block", path));
            if (asLater) keys.Add(PlcObjectKeys.Container($"{scope}block", path));
            if (asLater && item.Operation == "delete_block_group") keys.Add($"{scope}groupdelete");
            return keys.ToArray();
        }

        var block = PlcObjectKeys.Leaf($"{scope}block", path);
        // Interface edits and DB member writes own member keys below these (MemberScoped).
        if (item.Operation != PlcBlockEditMethods.CopyBlock) keys.Add(block);
        if (ResultPath(item, target) is { } result) keys.Add(PlcObjectKeys.Leaf($"{scope}block", result));
        // PlcWorkingState.Unresolved builds the 2-segment "PLC/Name" target from the caller's path.
        var undetermined = target.Kind == PlcNameRules.Block && path.Split('/').Length == 2;
        // Block names are unique per PLC across groups, so a later item that (re)creates the name anywhere, e.g. in another
        // group, supersedes an earlier delete (K19 pattern; the delete's read-back expects the old path absent).
        var ownsName = undetermined || item.Operation == "delete_block" && target.Kind == PlcNameRules.Block;
        if (asLater ? target.Kind == PlcNameRules.Block : ownsName) keys.Add($"{scope}blockname\n{target.Name}");
        if (asLater ? item.Operation == "delete_block_group" : undetermined) keys.Add($"{scope}groupdelete");
        return MemberScoped(item, keys, asLater).ToArray();
    }

    /// <summary>The suffix of a member-scoped key below its block or type key.</summary>
    private const string MemberSegment = "\nmember";

    /// <summary>
    /// The suffix of the whole-content key below a block or type key: what a later member edit offers to supersede an
    /// earlier whole-object item, without matching the member keys of earlier edits of other members.
    /// </summary>
    private const string ContentSegment = "\ncontent";

    /// <summary>
    /// Member-level supersession (K03, live report 2026-11-01: two interface edits on different members of one block marked
    /// the first <c>superseded</c>). An interface edit (add/update/delete_interface_member) and <c>update_db_member</c>
    /// own only the keys of their member (and of the renamed member for a rename), one segment per path level below
    /// the object's key, so a later write of the same member or of an enclosing member supersedes them, and a later
    /// edit of another member does not: each such item gets its own read-back. A later interface edit also offers the
    /// object's whole-content key, which every other (whole-object) item on the block or type owns, so an earlier
    /// update_block_logic, document or network edit is still superseded by it. A later whole-object item offers the
    /// object key itself, which prefixes every member key. <c>update_db_member</c> writes one attribute natively and,
    /// as before, offers only its member keys.
    /// </summary>
    private static IReadOnlyList<string> MemberScoped(PlcOperationRequest item, IReadOnlyList<string> objectKeys, bool asLater)
    {
        var interfaceEdit = PlcBlockEditMethods.InterfaceEdits.Contains(item.Operation, StringComparer.Ordinal);
        if (!interfaceEdit && item.Operation != PlcBlockEditMethods.UpdateDbMember)
            return asLater ? objectKeys : objectKeys.Concat(objectKeys.Select(key => key + ContentSegment)).ToArray();
        var members = MemberSuffixes(item);
        return objectKeys.SelectMany(key => members.Select(member => key + member)
                .Concat(asLater && interfaceEdit ? new[] { key + ContentSegment } : Array.Empty<string>()))
            .ToArray();
    }

    /// <summary>The member's key suffix and, for a rename, the renamed member's; no member path keys the whole member namespace.</summary>
    private static IReadOnlyList<string> MemberSuffixes(PlcOperationRequest item)
    {
        if (item.MemberPath is null) return new[] { MemberSegment };
        try
        {
            var path = PlcMemberPath.Parse(item.MemberPath, allowIndices: true);
            var suffixes = new List<string> { MemberSuffix(path) };
            if (item.NewName is { Length: > 0 } newName && !string.Equals(newName, path.Last.Name, StringComparison.OrdinalIgnoreCase))
                suffixes.Add(MemberSuffix(path.WithLastName(newName)));
            return suffixes;
        }
        catch (ArgumentException)
        {
            return new[] { MemberSegment + "\n" + item.MemberPath.ToUpperInvariant() };
        }
    }

    /// <summary>
    /// One newline-separated segment per path level, an index as a level of its own, so an enclosing member's key (an
    /// array's for its elements) prefixes it.
    /// </summary>
    private static string MemberSuffix(PlcMemberPath path)
        => MemberSegment + string.Concat(path.Segments.Select(segment => "\n" + segment.Name + (segment.Index is null ? "" : "\n[" + segment.Index + "]")));

    /// <summary>The block path a rename, copy or move produces, from the planned effect.</summary>
    private static string? ResultPath(PlcOperationRequest item, PlcTargetIdentity target)
    {
        var path = target.BlockPath ?? item.BlockPath ?? "";
        var parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : path;
        return item.Operation switch
        {
            PlcBlockEditMethods.RenameBlock => $"{parent}/{item.NewName}",
            PlcBlockEditMethods.CopyBlock => $"{(item.TargetGroupPath is null ? parent : CanonicalGroup(item.TargetGroupPath, target.PlcName))}/{item.NewName}",
            PlcBlockEditMethods.MoveBlock => $"{CanonicalGroup(item.TargetGroupPath!, target.PlcName)}/{target.Name}",
            _ => null,
        };
    }

    /// <summary>
    /// The item's read-back; for a block import whose target was the main safety block (or IDB) of F-runtime groups at
    /// planning, the groups are read again afterwards (E7): a reference that no longer names the block replaces the entry with a
    /// failed <c>safetyRuntimeGroup</c> check naming the repair (the worker already tried to restore it and warned).
    /// </summary>
    public async Task<WriteOperationVerification> ReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var entry = await ContentReadBackAsync(item, failure).ConfigureAwait(false);
        return _safetyLinks.TryGetValue(item.OperationId, out var links)
            ? await MainSafetyLinksReadBackAsync(item, links, entry).ConfigureAwait(false) ?? entry
            : entry;
    }

    private async Task<WriteOperationVerification> ContentReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        if (HasFileOrSafetyReadBack(item)) return FileOrSafetyReadBack(item);
        if (item.Operation == Contracts.Project.ProjectCompilationSettingsMethods.Set)
            return PlcCompilationSettings.Verify(item, failure, _results);
        if (item.Operation == Contracts.Plc.Loadable.PlcLoadableMethods.GenerateLoadable)
            return PlcLoadable.Verify(item, failure, _results);
        if (PlcBlockProtection.HasReadBack(item, _results))
            return PlcBlockProtection.Verify(item, _results);
        if (PlcProjectLanguages.HasReadBack(item, _results))
            return PlcProjectLanguages.Verify(item, _results);
        if (Contracts.Plc.Settings.PlcSettingsCatalog.IsWrite(item.Operation))
            return PlcSettings.Verify(item, failure, _results);
        if (Contracts.Plc.OpcUa.PlcOpcUaMethods.IsWrite(item.Operation))
            return PlcOpcUa.Verify(item, failure, _results);
        if (Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsWorkerMethod(item.Operation))
            return PlcDeviceData.Verify(item, failure, _results);
        if (PlcWorkingState.IsAlarmOperation(item.Operation))
            return await PlcAlarms.PlcAlarmVerification.ReadBackAsync(client, item, effects[item.OperationId], failure, _results, _contentHashes).ConfigureAwait(false);
        if (PlcBlockEditMethods.InterfaceEdits.Contains(item.Operation, StringComparer.Ordinal)) return await InterfaceReadBackAsync(item, failure).ConfigureAwait(false);
        if (PlcBlockEditMethods.NetworkEdits.Contains(item.Operation, StringComparer.Ordinal)) return await NetworkReadBackAsync(item, failure).ConfigureAwait(false);
        if (item.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument) return await CreatedBlockReadBackAsync(item, failure).ConfigureAwait(false);
        if (item.Operation == PlcBlockEditMethods.UpdateDbMember) return await DbMemberReadBackAsync(item, failure).ConfigureAwait(false);
        if (item.Operation == PlcBlockEditMethods.MoveTagTable) return await TagTableReadBackAsync(item, failure).ConfigureAwait(false);
        if (HandlesEditingReadBack(item.Operation)) return await EditingReadBackAsync(item, failure).ConfigureAwait(false);
        if (PlcBlockEditMethods.Write.Contains(item.Operation, StringComparer.Ordinal)) return await BlockReadBackAsync(item, failure).ConfigureAwait(false);
        if (HasTechnologyReadBack(item) && item.Operation != TechnologyObjectMethods.UpdateContent) return await TechnologyReadBackAsync(item, failure).ConfigureAwait(false);
        if (HandlesTagOrBlockReadBack(item)) return await TagOrBlockReadBackAsync(item).ConfigureAwait(false);
        if (HasAccessLevelReadBack(item)) return AccessLevelReadBack(item);

        // The caller's own path: the canonical path can be ambiguous across PLCs.
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = await PlcWritePlanner.ExportAsync(client, item, format, effects[item.OperationId].Target).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return Fail(item, "contentHash", $"Operation '{item.OperationId}': the written content could not be re-exported ({(export.Success ? "the export was empty" : export.Error)})."
                + (PlcExportConsistency.Carries(export.Error) ? " Its hash is not known until the block is compiled and exported again." : string.Empty));
        var hash = ContentHashes.Compute(format, export.Payload);
        if (S7dclConstructsReadBack(item, failure, export.Payload, hash) is { } constructs) return constructs;
        return new(item.OperationId, failure is null, "contentHash", null, hash, failure is null ? null
            : $"Operation '{item.OperationId}' failed with {failure.Category}; the current content hash is reported.");
    }

    /// <summary>Re-reads the interface: the member must show the edited state, and the new content hash is reported.</summary>
    private async Task<WriteOperationVerification> InterfaceReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var isType = item.TypePath is not null;
        var read = await client.PlcBlockEditAsync(isType ? PlcBlockEditMethods.GetTypeInterface : PlcBlockEditMethods.GetBlockInterface,
            new PlcBlockEditRequestInfo { BlockPath = item.BlockPath, TypePath = item.TypePath }).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = isType ? PlcBlockEditMethods.GetTypeInterface : PlcBlockEditMethods.GetBlockInterface }, read);
        if (projected.Result is not { } result)
            return Fail(item, InterfaceMemberCheck, $"Operation '{item.OperationId}': the interface could not be re-read ({projected.Failure?.Message}).");
        string hash;
        List<PlcInterfaceSectionInfo> sections;
        if (isType)
        {
            var type = CanonicalJson.Deserialize<PlcTypeInterfaceInfo>(result.GetRawText());
            (hash, sections) = (type.ContentHash, type.Sections);
        }
        else
        {
            var block = CanonicalJson.Deserialize<PlcBlockInterfaceInfo>(result.GetRawText());
            (hash, sections) = (block.ContentHash, block.Sections);
        }

        _contentHashes[item.OperationId] = hash;
        // A failed item is never reported as verified (K55): its entry only reports the state read afterwards.
        if (!_planned.TryGetValue(item.OperationId, out var planned))
            return new(item.OperationId, false, InterfaceMemberCheck, null, null, failure is null
                ? $"Operation '{item.OperationId}': no planned member state is known, so the edit is not verified; the current content hash is reported."
                : $"Operation '{item.OperationId}' failed with {failure.Category}; no planned member state is known; the current content hash is reported.");
        var check = planned.Expectation!.Check(sections);
        if (failure is not null)
            return new(item.OperationId, false, InterfaceMemberCheck, check.Expected, check.Actual,
                $"Operation '{item.OperationId}' failed with {failure.Category}; the current member state and content hash are reported.");
        return new(item.OperationId, check.Success, InterfaceMemberCheck, check.Expected, check.Actual, check.Success ? null
            : $"Operation '{item.OperationId}': the re-read interface does not show the requested state (TIA Portal may have normalized a literal). Inspect it with plc_read get_block_interface or get_type_interface.");
    }

    private async Task<WriteOperationVerification> DbMemberReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var requested = PlcInterfaceEditPlanner.DbChanges(item);
        var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetDbValues, new PlcBlockEditRequestInfo
        {
            BlockPath = item.BlockPath, MemberPath = item.MemberPath, WithAttributes = requested.Any(r => r.Field != "startValue") ? true : null,
        }).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = PlcBlockEditMethods.GetDbValues }, read);
        if (projected.Result is null && failure is null && WorkerReportedDbChanges(item) is { } observed
            && Unverifiable(item, DbMemberCheck, projected.Failure?.Message, observed.Expected, observed.Actual) is { } unverifiable)
            return unverifiable;
        if (projected.Result is not { } result)
            return Fail(item, DbMemberCheck, $"Operation '{item.OperationId}': the DB value could not be re-read ({projected.Failure?.Message}).");
        var values = CanonicalJson.Deserialize<PlcDbValuesInfo>(result.GetRawText());
        var path = PlcMemberPath.Parse(item.MemberPath!, allowIndices: true).ToFindPath();
        var value = values.Values.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
        var expected = string.Join(" ", requested.Select(r => $"{r.Field}={r.Requested}"));
        var actual = string.Join(" ", requested.Select(r => $"{r.Field}={PlcInterfaceEditPlanner.Current(value, r.Field) ?? "(unreadable)"}"));
        if (failure is not null)
            return new(item.OperationId, false, DbMemberCheck, expected, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current values are reported.");
        // Equal text without case and blanks counts; beyond that only an equal number in another spelling for an
        // integer or floating-point member (PlcStartValueLiterals). Any other different spelling fails.
        var dataType = value?.DataType;
        var success = value is not null && requested.All(r =>
        {
            var current = PlcInterfaceEditPlanner.Current(value, r.Field);
            return r.Field == "startValue"
                ? PlcStartValueLiterals.Equivalent(dataType, r.Requested, current)
                : PlcStartValueLiterals.SameText(current, r.Requested);
        });
        return new(item.OperationId, success, DbMemberCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read DB member does not show the requested values"
                + (dataType is null ? "" : $" (data type {dataType})")
                + ". Inspect it with plc_read get_db_values.");
    }

    private async Task<WriteOperationVerification> BlockReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        string? path = null;
        PlcBlockIdentityInfo? reported = null;
        if (failure is null && _results.TryGetValue(item.OperationId, out var result))
        {
            reported = CanonicalJson.Deserialize<PlcBlockEditResultInfo>(result.GetRawText()).Block;
            path = reported.BlockPath;
        }

        // A failed copy is checked where the copy would be (a leftover copy is what matters); other failures check the source.
        path = path is not null ? WithCallerPlc(path, item.BlockPath)
            : failure is null ? null
            : item.Operation == PlcBlockEditMethods.CopyBlock && ResultPath(item, effects[item.OperationId].Target) is { } copy ? WithCallerPlc(copy, item.BlockPath)
            : item.BlockPath;
        if (path is null) return Fail(item, BlockCheck, $"Operation '{item.OperationId}': the resulting block is unknown.");
        var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetBlockInterface, new PlcBlockEditRequestInfo { BlockPath = path }).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = PlcBlockEditMethods.GetBlockInterface }, read);
        if (failure is not null)
            return new(item.OperationId, false, BlockCheck, null, projected.Result is null ? $"{path} not readable ({projected.Failure?.Category})" : $"{path} exists",
                $"Operation '{item.OperationId}' failed with {failure.Category}; the state of '{path}' is reported. Inspect it with plc_read before retrying.");
        // The worker's result is its own read-back of the resulting block (name, type, number).
        if (projected.Result is null && Unverifiable(item, BlockCheck, projected.Failure?.Message, $"{reported!.BlockPath} {reported.BlockType} number={reported.Number?.ToString() ?? "(unreadable)"}",
                $"{reported.BlockPath} {reported.BlockType} number={reported.Number?.ToString() ?? "(unreadable)"}") is { } unverifiable)
            return unverifiable;
        if (projected.Result is not { } read2)
            return Fail(item, BlockCheck, $"Operation '{item.OperationId}': '{path}' could not be re-read ({projected.Failure?.Message}).");
        var info = CanonicalJson.Deserialize<PlcBlockInterfaceInfo>(read2.GetRawText());
        _contentHashes[item.OperationId] = info.ContentHash;
        var expected = $"{reported!.BlockPath} {reported.BlockType} number={reported.Number?.ToString() ?? "(unreadable)"}";
        var actual = $"{info.Block.BlockPath} {info.Block.BlockType} number={info.Block.Number?.ToString() ?? "(unreadable)"}";
        var success = string.Equals(info.Block.BlockPath, reported!.BlockPath, StringComparison.OrdinalIgnoreCase)
            && info.Block.Number == reported.Number && info.Block.BlockType == reported.BlockType
            && (item.Operation != PlcBlockEditMethods.CreateInstanceDb || string.Equals(info.Block.InstanceOfName, item.InstanceOfName, StringComparison.OrdinalIgnoreCase));
        return new(item.OperationId, success, BlockCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read block differs from what the operation reported. Inspect it with plc_read get_block_interface.");
    }

    private async Task<WriteOperationVerification> TagTableReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var target = NormalizeFolder(item.TargetFolderPath);
        var source = NormalizeFolder(item.FolderPath);
        var read = await client.ListTagTablesAsync(item.PlcName ?? effects[item.OperationId].Target.PlcName, tableName: item.TableName).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = "list_tag_tables" }, read);
        var tables = projected.Result is { } result
            ? CanonicalJson.Deserialize<PlcTagInventoryInfo>(result.GetRawText()).Plcs.SelectMany(p => p.Tables).ToList()
            : null;
        string Describe(string folder) => tables?.FirstOrDefault(t => string.Equals(NormalizeFolder(t.FolderPath), folder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Name, item.TableName, StringComparison.OrdinalIgnoreCase)) is { } table
            ? $"{folder}/{table.Name} tags={table.Tags.Count} userConstants={table.UserConstants.Count}"
            : $"{folder}/{item.TableName} absent";
        if (tables is null)
            return failure is null ? Fail(item, TagTableCheck, $"Operation '{item.OperationId}': the tag tables could not be re-read ({projected.Failure?.Message}).")
                : new(item.OperationId, false, TagTableCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the tag tables could not be re-read.");
        var actual = $"{Describe(source)}; {Describe(target)}";
        if (failure is not null)
            return new(item.OperationId, false, TagTableCheck, null, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current state is reported.");
        var moved = _results.TryGetValue(item.OperationId, out var moveResult) ? CanonicalJson.Deserialize<PlcTagTableMoveResultInfo>(moveResult.GetRawText()) : null;
        var expected = moved is null ? $"{target}/{item.TableName}" : $"{source}/{item.TableName} absent; {target}/{moved.TableName} tags={moved.TagCount} userConstants={moved.UserConstantCount}";
        var success = moved is not null && Describe(source).EndsWith(" absent", StringComparison.Ordinal)
            && string.Equals(Describe(target), $"{target}/{moved.TableName} tags={moved.TagCount} userConstants={moved.UserConstantCount}", StringComparison.OrdinalIgnoreCase);
        return new(item.OperationId, success, TagTableCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read tag tables differ from the move result. Inspect them with plc_read list_tag_tables.");
    }

    /// <summary>
    /// A canonical path re-addressed through the caller's PLC selector: the canonical path names the
    /// PLC software, which another PLC's device name may also match.
    /// </summary>
    private static string WithCallerPlc(string canonicalPath, string? callerPath)
    {
        var caller = callerPath?.Split('/');
        var segments = canonicalPath.Split('/');
        return caller is { Length: > 1 } && segments.Length > 1 ? caller[0] + "/" + string.Join("/", segments.Skip(1)) : canonicalPath;
    }

    private static string CanonicalGroup(string groupPath, string plcName)
    {
        try
        {
            var address = BlockAddress.Parse(groupPath.TrimEnd('/') + "/_");
            var segments = (address.UnitName is null ? new[] { plcName, "Blocks" } : new[] { plcName, "Units", address.UnitName, "Blocks" }).Concat(address.FolderPath);
            return string.Join("/", segments);
        }
        catch (ArgumentException)
        {
            return groupPath.TrimEnd('/');
        }
    }

    private static string NormalizeFolder(string? folderPath)
    {
        var segments = (folderPath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? "/" : "/" + string.Join("/", segments);
    }

    /// <summary>The check name of a write whose own worker read-back matched while the host's independent re-read was refused (K20).</summary>
    public const string UnverifiableCheck = "unverifiable";

    /// <summary>
    /// K20/K01: a completed write whose worker already read the change back, but whose host re-read failed only because TIA
    /// Portal refuses to export the now inconsistent object (the worker's <see cref="PlcExportConsistency"/> hint): the
    /// item is not failed; the entry is <c>unverifiable</c> with what the worker observed and the compile hint. Null when
    /// the re-read failed for another reason (that stays a failed verification).
    /// </summary>
    private static WriteOperationVerification? Unverifiable(PlcOperationRequest item, string check, string? readError, string? expected, string? workerObserved)
        => PlcExportConsistency.HintOf(readError) is { } hint
            ? new(item.OperationId, true, UnverifiableCheck, expected, workerObserved,
                $"Operation '{item.OperationId}': the write completed and the worker read it back{(workerObserved is null ? string.Empty : " (actual)")}, but the independent "
                + $"{check} re-read could not run: {hint}")
            : null;

    /// <summary>update_db_member's own read-back from its result: what was requested and what the worker observed afterwards.</summary>
    private (string Expected, string Actual)? WorkerReportedDbChanges(PlcOperationRequest item)
    {
        if (!_results.TryGetValue(item.OperationId, out var raw)) return null;
        var changes = CanonicalJson.Deserialize<PlcDbMemberUpdateResultInfo>(raw.GetRawText()).Changes;
        return changes.Count == 0 ? null
            : (string.Join(" ", changes.Select(c => $"{c.Attribute}={c.Requested}")), string.Join(" ", changes.Select(c => $"{c.Attribute}={c.Observed ?? "(unreadable)"}")));
    }

    private static WriteOperationVerification Fail(PlcOperationRequest item, string check, string message)
        => new(item.OperationId, false, check, null, null, message);
}
