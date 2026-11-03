using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Plc;

/// <summary>
/// Result of a completed add/update/delete_interface_member: the edit went through
/// update_block_logic (a block: <see cref="ImportOutcome"/>) or update_type_content (a PLC data type:
/// <see cref="TypeImport"/>); the other member is null.
/// </summary>
public sealed record PlcInterfaceEditResult(
    string Format,
    string Section,
    string MemberPath,
    BlockImportOutcomeInfo? ImportOutcome,
    PlcTypeImportResultInfo? TypeImport);

/// <summary>
/// Result of a completed document edit (set_network_text, replace_network_text, rename_members, and the SD network edits): the
/// planned document went through update_block_logic (<see cref="ImportOutcome"/>) or update_type_content (<see cref="TypeImport"/>);
/// the changed texts or names are in the item's effect. <see cref="MainSafetyBlockRoute"/> is present only when an SD network
/// edit targeted the main safety block of an F-runtime group (route sdViaXmlForMainSafetyBlock, live 2026-11-02).
/// </summary>
public sealed record PlcDocumentEditResult(
    string Operation,
    string Format,
    BlockImportOutcomeInfo? ImportOutcome,
    PlcTypeImportResultInfo? TypeImport,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    TiaMcpServer.Contracts.Plc.Safety.PlcMainSafetyBlockRouteInfo? MainSafetyBlockRoute = null);

/// <summary>Decoders of the granular block editing payloads: exactly one declared type per operation.</summary>
internal static class PlcBlockEditPayloadContract
{
    public static bool HandlesWrite(string operation)
        => PlcBlockEditMethods.Write.Contains(operation, StringComparer.Ordinal)
            || PlcBlockEditMethods.PlannedEdits.Contains(operation, StringComparer.Ordinal);

    /// <exception cref="JsonException">The payload does not decode as the operation's one declared type.</exception>
    public static JsonElement DecodeRead(string operation, string payload) => operation switch
    {
        PlcBlockEditMethods.GetBlockInterface => CanonicalJson.NormalizeWorkerPayload<PlcBlockInterfaceInfo>(payload, info =>
        {
            Require(info.Block);
            Strings(info.Messages);
            Sections(info.Sections);
        }).Element,
        PlcBlockEditMethods.GetTypeInterface => CanonicalJson.NormalizeWorkerPayload<PlcTypeInterfaceInfo>(payload, info =>
        {
            Strings(info.Messages);
            Sections(info.Sections);
        }).Element,
        PlcBlockEditMethods.GetDbValues => CanonicalJson.NormalizeWorkerPayload<PlcDbValuesInfo>(payload, info =>
        {
            Require(info.Block);
            Strings(info.Messages);
            Require(info.Values);
            StandalonePayloadContract.RejectNullElements(info.Values);
        }).Element,
        PlcBlockEditMethods.GetDbSnapshot => DbSnapshot(operation, payload),
        _ => throw new JsonException($"No declared result contract for PLC operation '{operation}'."),
    };

    /// <summary>The operations whose failures carry <see cref="PlcBlockEditFailureInfo"/> evidence.</summary>
    public static bool HasFailureEvidence(string operation)
        => operation is PlcBlockEditMethods.MoveBlock or PlcBlockEditMethods.MoveTagTable or PlcBlockEditMethods.CopyBlock;

    /// <exception cref="JsonException">The evidence does not decode as <see cref="PlcBlockEditFailureInfo"/>.</exception>
    public static JsonElement DecodeFailureEvidence(string evidence)
        => CanonicalJson.NormalizeWorkerPayload<PlcBlockEditFailureInfo>(evidence, info =>
        {
            Strings(info.Messages);
            if (info.Recovery is { } recovery && (recovery.Directory is null || recovery.DocumentPath is null || recovery.Location is null))
                throw new JsonException("The recovery file evidence is incomplete.");
        }).Element;

    /// <exception cref="JsonException">The payload does not decode as the operation's one declared type.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest operation, string payload, WorkerCallResult workerResult)
    {
        switch (operation.Operation)
        {
            case PlcBlockEditMethods.UpdateDbMember:
                return CanonicalJson.NormalizeWorkerPayload<PlcDbMemberUpdateResultInfo>(payload, info =>
                {
                    Require(info.Block);
                    Require(info.Changes);
                    StandalonePayloadContract.RejectNullElements(info.Changes);
                }).Element;
            case PlcBlockEditMethods.MoveTagTable:
                return CanonicalJson.NormalizeWorkerPayload<PlcTagTableMoveResultInfo>(payload).Element;
            case PlcBlockEditMethods.CreateDbSnapshot:
                return DbSnapshot(operation.Operation, payload);
            case PlcBlockEditMethods.ReplaceTagComments:
                return CanonicalJson.NormalizeWorkerPayload<PlcTagCommentReplaceResultInfo>(payload, info =>
                {
                    if (info.Operation != PlcBlockEditMethods.ReplaceTagComments) throw new JsonException();
                    Require(info.Changes);
                    StandalonePayloadContract.RejectNullElements(info.Changes);
                    Strings(info.Messages);
                }).Element;
            case PlcBlockEditMethods.ExportOpcUaXml:
                return CanonicalJson.NormalizeWorkerPayload<PlcOpcUaExportResultInfo>(payload, info =>
                {
                    if (!string.Equals(info.Operation, operation.Operation, StringComparison.Ordinal)) throw new JsonException();
                    Strings(info.Messages);
                    if (info.FileSha256 is not { Length: 64 } sha || sha.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c)) || info.FileBytes < 0) throw new JsonException();
                    // plcName selects by PLC software or device name, so the resolved names are evidence, not compared with it.
                    if (string.IsNullOrWhiteSpace(info.PlcName) || string.IsNullOrWhiteSpace(info.DeviceName)) throw new JsonException();
                }).Element;
            case PlcBlockEditMethods.ExportTagTable:
            case PlcBlockEditMethods.ImportTagTable:
                return CanonicalJson.NormalizeWorkerPayload<PlcTagTableFileResultInfo>(payload, info =>
                {
                    if (!string.Equals(info.Operation, operation.Operation, StringComparison.Ordinal)) throw new JsonException();
                    Require(info.Tables);
                    Strings(info.Tables);
                    Strings(info.Messages);
                    if (info.FileSha256 is not { Length: 64 } sha || sha.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c)) || info.FileBytes < 0) throw new JsonException();
                    // The export names exactly the requested table; an import's names come from the worker's evidence.
                    if (operation.Operation == PlcBlockEditMethods.ExportTagTable
                        && (info.Tables.Count != 1 || !string.Equals(info.Tables[0], operation.TableName, StringComparison.OrdinalIgnoreCase)))
                        throw new JsonException();
                    if (operation.Operation == PlcBlockEditMethods.ImportTagTable && operation.TableName is { } expected
                        && !info.Tables.Contains(expected, StringComparer.OrdinalIgnoreCase))
                        throw new JsonException();
                }).Element;
            case PlcBlockEditMethods.RenameTags:
            case PlcBlockEditMethods.RenameObjects:
                return CanonicalJson.NormalizeWorkerPayload<PlcBatchRenameResultInfo>(payload, info =>
                {
                    if (!string.Equals(info.Operation, operation.Operation, StringComparison.Ordinal)) throw new JsonException();
                    Require(info.Items);
                    StandalonePayloadContract.RejectNullElements(info.Items);
                    Strings(info.Steps);
                    Strings(info.Messages);
                    // Exactly the requested renames, in order: the worker must not report objects the call did not name.
                    var requested = operation.Renames ?? new List<PlcNameRenameInfo>();
                    if (info.Items.Count != requested.Count
                        || info.Items.Zip(requested).Any(pair => !string.Equals(pair.First.OldName, pair.Second.Name, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(pair.First.NewName, pair.Second.NewName, StringComparison.Ordinal)))
                        throw new JsonException();
                }).Element;
            case PlcBlockEditMethods.AddInterfaceMember:
            case PlcBlockEditMethods.UpdateInterfaceMember:
            case PlcBlockEditMethods.DeleteInterfaceMember:
                return InterfaceEdit(operation, payload, workerResult);
            case var document when PlcBlockEditMethods.DocumentEdits.Contains(document, StringComparer.Ordinal):
                return DocumentEdit(operation, payload, workerResult);
            case var network when PlcBlockEditMethods.NetworkEdits.Contains(network, StringComparer.Ordinal):
                // A network edit's import: the typed update_block_logic (format sd) outcome; the changes are in the effect.
                return CanonicalJson.ToElement(new PlcDocumentEditResult(operation.Operation, SourceFormatNames.Sd, workerResult.BlockImportOutcome ?? throw new JsonException(), null,
                    PlcMainSafetyBlockRouteContract.DecodeOrNull(workerResult.PlcDownloadOutcome)));
            default:
                if (!PlcBlockEditMethods.Write.Contains(operation.Operation, StringComparer.Ordinal))
                    throw new JsonException($"No declared result contract for PLC write operation '{operation.Operation}'.");
                return CanonicalJson.NormalizeWorkerPayload<PlcBlockEditResultInfo>(payload, info =>
                {
                    Require(info.Block);
                    Strings(info.Messages);
                    if (info.MainSafetyBlockRoute is { } route)
                    {
                        // Only create_block_from_document takes the main safety block route.
                        if (operation.Operation != TiaMcpServer.Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument) throw new JsonException();
                        PlcMainSafetyBlockRouteContract.Check(route);
                    }
                }).Element;
        }
    }

    private static JsonElement InterfaceEdit(PlcOperationRequest operation, string payload, WorkerCallResult workerResult)
    {
        var section = operation.TypePath is not null ? "None" : operation.Section ?? string.Empty;
        if (operation.TypePath is not null)
        {
            var typeImport = CanonicalJson.NormalizeWorkerPayload<PlcTypeImportResultInfo>(payload).Value;
            return CanonicalJson.ToElement(new PlcInterfaceEditResult("xml", section, operation.MemberPath!, null, typeImport));
        }

        // The block route is update_block_logic: its typed outcome is the result, the worker text is not.
        var outcome = workerResult.BlockImportOutcome ?? throw new JsonException();
        return CanonicalJson.ToElement(new PlcInterfaceEditResult("xml", section, operation.MemberPath!, outcome, null));
    }

    /// <summary>A document edit's import: the typed update_block_logic outcome (a block) or the type import result.</summary>
    private static JsonElement DocumentEdit(PlcOperationRequest operation, string payload, WorkerCallResult workerResult)
        => operation.TypePath is not null
            ? CanonicalJson.ToElement(new PlcDocumentEditResult(operation.Operation, "xml", null, CanonicalJson.NormalizeWorkerPayload<PlcTypeImportResultInfo>(payload).Value))
            : CanonicalJson.ToElement(new PlcDocumentEditResult(operation.Operation, "xml", workerResult.BlockImportOutcome ?? throw new JsonException(), null));

    /// <summary>get_db_snapshot / create_db_snapshot: the worker must report the operation it ran.</summary>
    private static JsonElement DbSnapshot(string operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcDbSnapshotInfo>(payload, info =>
        {
            Require(info.Block);
            if (!string.Equals(info.Operation, operation, StringComparison.Ordinal)) throw new JsonException();
            Require(info.Values);
            StandalonePayloadContract.RejectNullElements(info.Values);
            Strings(info.TopLevelMembers);
            Strings(info.Messages);
            if (info.Values.Count > info.MatchedValues || info.MatchedValues > info.TotalValues) throw new JsonException();
        }).Element;

    private static void Sections(List<PlcInterfaceSectionInfo>? sections)
    {
        Require(sections);
        StandalonePayloadContract.RejectNullElements(sections!);
        foreach (var section in sections!) Members(section.Members);
    }

    private static void Members(List<PlcInterfaceMemberInfo>? members)
    {
        Require(members);
        StandalonePayloadContract.RejectNullElements(members!);
        foreach (var member in members!)
        {
            Require(member.Comments);
            StandalonePayloadContract.RejectNullElements(member.Comments);
            Require(member.Attributes);
            StandalonePayloadContract.RejectNullElements(member.Attributes);
            Strings(member.OtherElements);
            Members(member.Members);
        }
    }

    private static void Strings(List<string>? values)
    {
        Require(values);
        StandalonePayloadContract.RejectNullElements(values!);
    }

    private static void Require(object? value)
    {
        if (value is null) throw new JsonException();
    }
}
