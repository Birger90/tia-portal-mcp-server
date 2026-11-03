using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Files;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcAlarms;

/// <summary>
/// Read-back of the alarm, text list, supervision and ProDiag writes after the call: a text list update or delete re-lists
/// the PLC's text lists (<c>textList</c>), a cell update re-reads the edited cells of the same workbook (<c>cells</c>, with
/// the new workbook hash as content hash), an export re-hashes the written file (<c>file</c>) or every file of the created
/// directory (<c>directory</c>), an AssignedProDiagFB change is read again (<c>proDiagAssignment</c>; an unreadable value is
/// <c>notVerified</c>). An import is judged by the verification re-export the worker ran right after it inside the same
/// ExclusiveAccess (<see cref="PlcAlarmImportCheckInfo.Outcome"/>): <c>verified</c> is <c>importedRows</c> (or
/// <c>fileHash</c> for alarm classes), <c>contradicted</c> (an imported row is absent) is <c>notVerified</c> with success
/// false, <c>inconclusive</c> and <c>notReadBack</c> are <c>trusted</c> with a message naming why (and a worker warning).
/// A failed item reports the observed state.
/// </summary>
internal static class PlcAlarmVerification
{
    public const string TextListCheck = "textList";
    public const string CellsCheck = "cells";
    public const string FileCheck = "file";
    public const string DirectoryCheck = "directory";
    public const string ImportedRowsCheck = "importedRows";
    public const string FileHashCheck = "fileHash";
    public const string NotVerifiedCheck = "notVerified";
    public const string AssignmentCheck = "proDiagAssignment";

    public static bool HasReadBack(string operation)
        => operation is PlcAlarmMethods.UpdateTextList or PlcAlarmMethods.DeleteTextList or PlcAlarmMethods.SetProDiagAssignment or PlcAlarmMethods.ExportProDiagInfo
            || PlcAlarmMethods.CellUpdates.Contains(operation) || PlcAlarmMethods.FileExports.Contains(operation) || PlcAlarmMethods.FileImports.Contains(operation);

    /// <summary>
    /// Block and type changes that can change a PLC's alarm instance texts, supervisions or ProDiag assignments: a block may
    /// own alarms and supervisions (its interface, logic, name, number, group, software unit or instance DBs carry them, and a
    /// group or unit rename changes its blockPath), blocks generated from an external source replace such blocks, and a
    /// type's members may carry supervisions of every instance, so deleting a PLC data type group (its types) counts like a
    /// type content change.
    /// </summary>
    public static IReadOnlyList<string> AlarmChangingBlockOperations { get; } = new[]
    {
        "delete_block", "delete_block_group", "rename_block_group", "update_block_logic", "update_type_content", "rename_block", "renumber_block",
        "move_block", "copy_block", "create_block", "create_instance_db", "generate_blocks_from_source", "delete_software_unit", "rename_software_unit",
        "delete_type_group", "add_interface_member", "update_interface_member", "delete_interface_member", "update_db_member",
    };

    /// <summary>
    /// Tag and tag-table changes: a tag can carry AssignedProDiagFB (blocks.md), so its ProDiag FB supervises it; a later tag
    /// change therefore supersedes earlier assignment and supervision items (conservatively: whether a tag's supervisions are
    /// part of the FB's supervision workbook is not documented). Deleting a tag table group deletes its tables and renaming it
    /// changes their folder path, like delete_tag_table and move_tag_table. Alarm instance texts are not documented to
    /// reference tags (alarms.md names no tag), so tag changes do not supersede alarm-text items.
    /// </summary>
    public static IReadOnlyList<string> AssignmentChangingTagOperations { get; } = new[]
    {
        "delete_tag", "update_tag", "move_tag", "delete_tag_table", "move_tag_table", "delete_tag_table_group", "rename_tag_table_group",
    };

    /// <summary>
    /// The keys a later block, type or tag change offers to earlier alarm items of its PLC (conservatively, on the whole PLC):
    /// <see cref="AlarmChangingBlockOperations"/> supersede alarm-text, supervision and AssignedProDiagFB items,
    /// <see cref="AssignmentChangingTagOperations"/> supervision and AssignedProDiagFB items.
    /// </summary>
    public static IEnumerable<string> LaterBlockKeys(string operation, string scope)
        => AlarmChangingBlockOperations.Contains(operation, StringComparer.Ordinal)
            ? new[] { $"{scope}alarm\ntexts", $"{scope}alarm\nsupervision", $"{scope}alarm\nprodiag" }
            : AssignmentChangingTagOperations.Contains(operation, StringComparer.Ordinal)
                ? new[] { $"{scope}alarm\nsupervision", $"{scope}alarm\nprodiag" }
                : Array.Empty<string>();

    /// <summary>Hierarchical keys: a whole-workbook change of a PLC's text lists covers each of its lists; an export owns only its file.</summary>
    public static IReadOnlyList<string> ObjectKeys(PlcOperationRequest item, PlcTargetIdentity target, bool asLater)
    {
        var scope = $"{target.DeviceName}\n{target.PlcName}\n";
        if (PlcAlarmMethods.FileExports.Contains(item.Operation)) return new[] { FileKey(item.FilePath) };
        if (item.Operation == PlcAlarmMethods.ExportProDiagInfo) return new[] { FileKey(item.DirectoryPath) };
        return target.Kind switch
        {
            PlcWorkingState.TextListKind => new[] { $"{scope}alarm\ntextlists\n{target.Name}" }
                .Concat(item.NewName is null ? Array.Empty<string>() : new[] { $"{scope}alarm\ntextlists\n{item.NewName}" })
                // A later rename, range or comment change also changes the text list workbook.
                .Concat(asLater ? new[] { $"{scope}alarm\ntextlists\n#workbook" } : Array.Empty<string>()).ToArray(),
            PlcWorkingState.TextListWorkbookKind => new[] { asLater ? $"{scope}alarm\ntextlists" : $"{scope}alarm\ntextlists\n#workbook" },
            PlcWorkingState.AlarmTextsKind => new[] { $"{scope}alarm\ntexts" },
            PlcWorkingState.SupervisionsKind => new[] { SupervisionKey(scope, target.BlockPath) },
            PlcWorkingState.AlarmClassesKind => new[] { "project\nalarmclasses" },
            PlcWorkingState.SystemDiagnosticsSettingsKind => new[] { "project\nsystemdiagnosticssettings" },
            PlcWorkingState.ProDiagAssignmentKind => new[] { $"{scope}alarm\nprodiag\n{target.Name}" },
            _ => new[] { $"{scope}alarm\n{target.Kind}" },
        };
    }

    /// <summary>
    /// The verification key of an alarm export's file or directory, scope-free (the same file is one object whichever PLC
    /// the export names) and canonical: <c>file\n</c>, then the path with '/' as '\\' when it is drive-rooted, made full by
    /// <c>Path.GetFullPath</c> when it is rooted on this platform (otherwise unchanged: a Windows path on the Linux test host),
    /// without a trailing separator and upper-cased (Windows paths compare case-insensitively). Only the alarm exports own
    /// such keys: an item of another domain that writes the same file is keyed by its object, so the two do not supersede
    /// each other.
    /// </summary>
    public static string FileKey(string? path)
    {
        var text = path ?? string.Empty;
        if (LocalFilePaths.IsDriveRooted(text)) text = text.Replace('/', '\\');
        try
        {
            var rooted = Path.DirectorySeparatorChar == '\\' ? LocalFilePaths.IsAbsoluteLocal(text) : text.StartsWith("/", StringComparison.Ordinal);
            if (rooted) text = Path.GetFullPath(text);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
        }

        var trimmed = text.TrimEnd('\\', '/');
        return "file\n" + (trimmed.Length == 0 ? text : trimmed).ToUpperInvariant();
    }

    /// <summary>
    /// The key of a ProDiag FB's supervisions: the block path is one segment of the PLC's <c>alarm</c> namespace, not a block
    /// key, so it stays unaffected by how block keys are built; a later block change offers <c>alarm\nsupervision</c>
    /// (<see cref="LaterBlockKeys"/>), which prefixes it.
    /// </summary>
    public static string SupervisionKey(string scope, string? blockPath) => $"{scope}alarm\nsupervision\n{blockPath}";

    public static async Task<WriteOperationVerification> ReadBackAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect,
        StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results, IDictionary<string, string> contentHashes)
    {
        if (PlcAlarmMethods.FileExports.Contains(item.Operation)) return FileReadBack(item, failure, results);
        if (PlcAlarmMethods.FileImports.Contains(item.Operation)) return ImportReadBack(item, failure, results);
        if (item.Operation == PlcAlarmMethods.ExportProDiagInfo) return DirectoryReadBack(item, failure, results);
        if (item.Operation == PlcAlarmMethods.SetProDiagAssignment) return await AssignmentReadBackAsync(client, item, failure).ConfigureAwait(false);
        if (PlcAlarmMethods.CellUpdates.Contains(item.Operation)) return await CellsReadBackAsync(client, item, failure, contentHashes).ConfigureAwait(false);
        return await TextListReadBackAsync(client, item, effect, failure).ConfigureAwait(false);
    }

    private static async Task<WriteOperationVerification> TextListReadBackAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, StructuredOperationFailure? failure)
    {
        var call = await client.PlcAlarmAsync(PlcAlarmMethods.ListTextLists, new PlcAlarmRequestInfo { PlcName = item.PlcName ?? effect.Target.PlcName }).ConfigureAwait(false);
        List<PlcTextListInfo>? lists = null;
        if (call.Success)
        {
            try
            {
                lists = PlcAlarmPayloadContract.DecodeInventory(call.Payload).Plcs
                    .Where(p => p.PlcName == effect.Target.PlcName && p.DeviceName == effect.Target.DeviceName).SelectMany(p => p.UserTextLists).ToList();
            }
            catch (JsonException)
            {
            }
        }

        if (lists is null)
            return failure is null ? Fail(item, TextListCheck, $"Operation '{item.OperationId}': the text lists could not be re-read ({call.Error ?? "rejected payload"}).")
                : new(item.OperationId, false, TextListCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the text lists could not be re-read.");
        var original = effect.Target.Name ?? item.TextListName!;
        var final = item.Operation == PlcAlarmMethods.UpdateTextList ? item.NewName ?? original : original;
        var found = lists.FirstOrDefault(list => string.Equals(list.Name, final, StringComparison.OrdinalIgnoreCase));
        var old = lists.FirstOrDefault(list => string.Equals(list.Name, original, StringComparison.OrdinalIgnoreCase));
        if (item.Operation == PlcAlarmMethods.DeleteTextList)
        {
            var actualDelete = found is null ? $"{original} absent" : $"{original} present";
            if (failure is not null)
                return new(item.OperationId, false, TextListCheck, null, actualDelete, $"Operation '{item.OperationId}' failed with {failure.Category}; the current state is reported.");
            return new(item.OperationId, found is null, TextListCheck, $"{original} absent", actualDelete, found is null ? null
                : $"Operation '{item.OperationId}': user text list '{original}' is still listed. Inspect it with plc_read list_text_lists.");
        }

        var expected = Describe(final, item.ListRange, item.CommentCulture, item.Comment);
        var actual = found is null ? $"{final} absent" + (old is not null && !ReferenceEquals(old, found) ? $"; {original} present" : string.Empty)
            : Describe(found.Name, item.ListRange is null ? null : found.ListRange, item.CommentCulture,
                item.CommentCulture is null ? null : found.Comment?.FirstOrDefault(c => string.Equals(c.Culture, item.CommentCulture, StringComparison.OrdinalIgnoreCase))?.Text);
        if (failure is not null)
            return new(item.OperationId, false, TextListCheck, expected, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current state is reported.");
        var success = string.Equals(expected, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, TextListCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read text list does not show the requested state. Inspect it with plc_read list_text_lists.");
    }

    private static string Describe(string name, string? range, string? culture, string? comment)
        => name + (range is null ? string.Empty : $" listRange={range}") + (culture is null ? string.Empty : $" comment[{culture}]={PlcAlarmPolicy.Bound(comment ?? string.Empty, 200)}");

    private static async Task<WriteOperationVerification> CellsReadBackAsync(OpennessWorkerClient client, PlcOperationRequest item, StructuredOperationFailure? failure,
        IDictionary<string, string> contentHashes)
    {
        var read = PlcAlarmMethods.WorkbookReadFor(item.Operation)!;
        var call = await client.PlcAlarmAsync(read, PlcAlarmRequests.WorkbookProbe(item)).ConfigureAwait(false);
        PlcAlarmWorkbookInfo? workbook = null;
        if (call.Success)
        {
            try
            {
                workbook = PlcAlarmPayloadContract.DecodeWorkbook(read, call.Payload);
            }
            catch (JsonException)
            {
            }
        }

        if (workbook is null)
            return failure is null ? Fail(item, CellsCheck, $"Operation '{item.OperationId}': the workbook could not be re-read ({call.Error ?? "rejected payload"}).")
                : new(item.OperationId, false, CellsCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the workbook could not be re-read.");
        contentHashes[item.OperationId] = workbook.WorkbookHash;
        var edits = item.CellEdits!;
        var expected = string.Join("; ", edits.Select(edit => $"{edit.Sheet}!{edit.Cell}={PlcAlarmPolicy.Bound(edit.Value, 200)}"));
        var observed = edits.Select(edit => (Edit: edit, Probe: workbook.Probes.FirstOrDefault(p => p.Sheet == edit.Sheet && p.Cell == edit.Cell))).ToList();
        var actual = string.Join("; ", observed.Select(o => $"{o.Edit.Sheet}!{o.Edit.Cell}={PlcAlarmPolicy.Bound(o.Probe?.Value ?? "(unreadable)", 200)}"));
        if (failure is not null)
            return new(item.OperationId, false, CellsCheck, expected, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current cell values and workbook hash are reported.");
        var success = observed.All(o => o.Probe is not null && Same(o.Edit, o.Probe));
        return new(item.OperationId, success, CellsCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read workbook does not show every edited cell. Inspect it with plc_read {read}.");
    }

    private static bool Same(PlcAlarmCellEditRequest edit, PlcAlarmCellProbeInfo probe)
    {
        if (probe.Kind == PlcAlarmPolicy.CellKinds.Number && PlcAlarmPolicy.IsNumber(edit.Value) && PlcAlarmPolicy.IsNumber(probe.Value)
            && edit.Kind != PlcAlarmPolicy.CellKinds.String)
            return double.Parse(edit.Value!, CultureInfo.InvariantCulture) == double.Parse(probe.Value, CultureInfo.InvariantCulture);
        return string.Equals(probe.Value, edit.Value, StringComparison.Ordinal);
    }

    private static WriteOperationVerification FileReadBack(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        var path = item.FilePath!;
        string? actual = null;
        try
        {
            if (File.Exists(path)) actual = PlcAlarmFiles.Hash(path).Sha256;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            actual = $"(unreadable: {exception.Message})";
        }

        if (failure is not null)
            return new(item.OperationId, false, FileCheck, null, actual ?? "absent", $"Operation '{item.OperationId}' failed with {failure.Category}; the state of '{path}' is reported.");
        var reported = results.TryGetValue(item.OperationId, out var result) && result.TryGetProperty("sha256", out var sha) ? sha.GetString() : null;
        var success = reported is not null && string.Equals(reported, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, FileCheck, reported, actual ?? "absent", success ? null
            : $"Operation '{item.OperationId}': '{path}' does not hash to the exported bytes (it was changed or removed after the export).");
    }

    /// <summary>The worker's verification re-export of an import, from the item's result (<see cref="PlcAlarmImportCheckInfo.Outcome"/>).</summary>
    private static WriteOperationVerification ImportReadBack(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        PlcAlarmImportCheckInfo? check = null;
        if (results.TryGetValue(item.OperationId, out var result) && result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("importCheck", out var element) && element.ValueKind == JsonValueKind.Object)
            check = CanonicalJson.Deserialize<PlcAlarmImportCheckInfo>(element.GetRawText());
        if (failure is not null)
            return new(item.OperationId, false, NotVerifiedCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the import was not verified.");
        if (check is null)
            return new(item.OperationId, false, NotVerifiedCheck, null, null, $"Operation '{item.OperationId}': the import result carries no verification re-export.");
        var rows = check.Method == "rows";
        var expected = check.Method switch
        {
            "rows" => $"{check.ImportedRows} imported rows shown",
            "fileHash" => check.ImportedSha256,
            _ => null,
        };
        var actual = check.Method switch
        {
            "rows" => check.RowsNotShown is { } missing ? $"{check.ImportedRows - missing} shown, {missing} not shown" : null,
            "fileHash" => check.ReexportSha256,
            _ => null,
        };
        var hash = rows ? $" The re-export's workbookHash is {check.WorkbookHashAfter ?? "(none)"}." : string.Empty;
        return check.Outcome switch
        {
            PlcAlarmPolicy.ImportOutcomes.Verified => new(item.OperationId, true, rows ? ImportedRowsCheck : FileHashCheck, expected, actual, null),
            PlcAlarmPolicy.ImportOutcomes.Contradicted => new(item.OperationId, false, NotVerifiedCheck, expected, actual,
                $"Operation '{item.OperationId}': the import changed the project, but {check.Reason}{hash} Inspect the project before relying on the import."),
            PlcAlarmPolicy.ImportOutcomes.Inconclusive => new(item.OperationId, true, WriteVerificationChecks.Trusted, expected, actual,
                $"Operation '{item.OperationId}': the import's state is trusted; its verification is inconclusive: {check.Reason}{hash}"),
            PlcAlarmPolicy.ImportOutcomes.NotReadBack => new(item.OperationId, true, WriteVerificationChecks.Trusted, null, null,
                $"Operation '{item.OperationId}': the import's state is trusted; it is {check.Reason}"),
            _ => new(item.OperationId, false, NotVerifiedCheck, expected, actual, $"Operation '{item.OperationId}': the import check has no known outcome."),
        };
    }

    /// <summary>Re-hashes every file the directory export listed.</summary>
    private static WriteOperationVerification DirectoryReadBack(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        var directory = item.DirectoryPath!;
        if (failure is not null)
            return new(item.OperationId, false, DirectoryCheck, null, Directory.Exists(directory) ? "present" : "absent",
                $"Operation '{item.OperationId}' failed with {failure.Category}; the state of '{directory}' is reported.");
        var reported = results.TryGetValue(item.OperationId, out var result) && result.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
            ? CanonicalJson.Deserialize<List<PlcAlarmExportedFileInfo>>(files.GetRawText()) : null;
        List<PlcAlarmExportedFileInfo>? actual = null;
        try
        {
            if (Directory.Exists(directory)) actual = PlcAlarmFiles.HashTree(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlcAlarmException)
        {
        }

        string Describe(IEnumerable<PlcAlarmExportedFileInfo>? list) => list is null ? "absent" : string.Join("; ", list.Select(f => $"{f.Name}={f.Sha256}"));
        var success = reported is not null && actual is not null && Describe(reported) == Describe(actual);
        return new(item.OperationId, success, DirectoryCheck, PlcAlarmPolicy.Bound(Describe(reported), 2000), PlcAlarmPolicy.Bound(Describe(actual), 2000), success ? null
            : $"Operation '{item.OperationId}': '{directory}' does not hold exactly the exported files any more.");
    }

    /// <summary>Reads AssignedProDiagFB of the same target again.</summary>
    private static async Task<WriteOperationVerification> AssignmentReadBackAsync(OpennessWorkerClient client, PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var call = await client.PlcAlarmAsync(PlcAlarmMethods.GetProDiagAssignment, PlcAlarmRequests.From(item)).ConfigureAwait(false);
        PlcProDiagAssignmentInfo? read = null;
        if (call.Success)
        {
            try
            {
                read = PlcAlarmPayloadContract.DecodeAssignment(call.Payload);
            }
            catch (JsonException)
            {
            }
        }

        var actual = read is null ? null : read.Readable ? read.AssignedProDiagFb ?? "(none)" : "(unreadable)";
        if (failure is not null)
            return new(item.OperationId, false, AssignmentCheck, null, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current assignment is reported.");
        if (read is null)
            return Fail(item, AssignmentCheck, $"Operation '{item.OperationId}': the assignment could not be re-read ({call.Error ?? "rejected payload"}).");
        if (!read.Readable)
            return new(item.OperationId, false, NotVerifiedCheck, item.ProDiagFb, actual,
                $"Operation '{item.OperationId}': the write returned, but {read.Target} does not read back AssignedProDiagFB by FB name ({read.Message}). Inspect it in TIA Portal.");
        var success = string.Equals(read.AssignedProDiagFb, item.ProDiagFb, StringComparison.OrdinalIgnoreCase);
        return new(item.OperationId, success, AssignmentCheck, item.ProDiagFb, actual, success ? null
            : $"Operation '{item.OperationId}': {read.Target} reads back AssignedProDiagFB {actual}. Inspect it with plc_read get_prodiag_assignment.");
    }

    private static WriteOperationVerification Fail(PlcOperationRequest item, string check, string message) => new(item.OperationId, false, check, null, null, message);
}
