using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcAlarms;

/// <summary>
/// The only decoder of the alarm, text list, supervision and ProDiag worker payloads. Every read declares one result type
/// (<see cref="PlcTextListInventoryInfo"/>, <see cref="PlcAlarmWorkbookInfo"/>, <see cref="PlcProDiagFbInfo"/> or
/// <see cref="PlcProDiagAssignmentInfo"/>), the planning read <see cref="PlcAlarmPlanInfo"/> and every write
/// <see cref="PlcAlarmWriteResultInfo"/>; a payload that does not decode, or contradicts the request, is a
/// <c>protocol_error</c> and is never echoed back. A write's TIA Portal confirmation evidence follows plc_download's rules:
/// the policy is the requested one, the list is capped and consistent with the count, and every answer is the policy's;
/// declined dialogs fail the item (the result is kept), accepted ones add a warning.
/// </summary>
public static class PlcAlarmPayloadContract
{
    public static JsonElement DecodeRead(string operation, string payload) => operation switch
    {
        PlcAlarmMethods.ListTextLists => CanonicalJson.NormalizeWorkerPayload<PlcTextListInventoryInfo>(payload, ValidateInventory).Element,
        PlcAlarmMethods.GetProDiagFb => CanonicalJson.NormalizeWorkerPayload<PlcProDiagFbInfo>(payload, ValidateFb).Element,
        PlcAlarmMethods.GetProDiagAssignment => CanonicalJson.NormalizeWorkerPayload<PlcProDiagAssignmentInfo>(payload, ValidateAssignment).Element,
        _ => CanonicalJson.NormalizeWorkerPayload<PlcAlarmWorkbookInfo>(payload, workbook => ValidateWorkbook(operation, workbook)).Element,
    };

    public static PlcTextListInventoryInfo DecodeInventory(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcTextListInventoryInfo>(payload, ValidateInventory).Value;

    public static PlcAlarmWorkbookInfo DecodeWorkbook(string operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcAlarmWorkbookInfo>(payload, workbook => ValidateWorkbook(operation, workbook)).Value;

    public static PlcProDiagAssignmentInfo DecodeAssignment(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcProDiagAssignmentInfo>(payload, ValidateAssignment).Value;

    public static PlcAlarmPlanInfo DecodePlan(string operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcAlarmPlanInfo>(payload, plan =>
        {
            if (plan.Operation != operation) throw new JsonException("The plan names another operation.");
            Require(plan.Bound);
            Require(plan.Messages);
            StandalonePayloadContract.RejectNullElements(plan.TextListNames);
            if (plan.Path is { } path && (path.FullPath is null || path.ReparseAncestors is null || path.ReparseAncestors.Any(a => a is null))) throw new JsonException();
        }).Value;

    public static JsonElement DecodeWrite(string operation, string payload, string? policy = null)
        => CanonicalJson.ToElement(DecodeWriteResult(operation, payload, policy));

    public static PlcAlarmWriteResultInfo DecodeWriteResult(string operation, string payload, string? policy = null)
        => CanonicalJson.NormalizeWorkerPayload<PlcAlarmWriteResultInfo>(payload, result => ValidateWrite(operation, result, policy)).Value;

    /// <summary>
    /// One write item's typed outcome. A failed worker call may carry the result collected until the failure (with its
    /// confirmation record); a succeeded one whose dialogs were declined fails with the result kept.
    /// </summary>
    public static StructuredOperationItem ProjectWrite(PlcOperationRequest item, WorkerCallResult result, Action<string> writeDiagnostic)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        var policy = item.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline;
        if (!result.Success)
        {
            var category = result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
            var failed = StructuredOperationItem.Failed(item, category, result.Error ?? $"PLC operation '{item.Operation}' failed.", warnings);
            if (result.PlcDownloadOutcome is null) return failed;
            try
            {
                return failed with { Result = DecodeWrite(item.Operation, result.PlcDownloadOutcome, policy) };
            }
            catch (JsonException)
            {
                StructuredPayloadProjection.TryWriteProtocolDiagnostic(item.Operation, writeDiagnostic);
                return failed with { Failure = failed.Failure! with { Message = failed.Failure.Message + " The worker's failure evidence did not match its declared contract and was rejected." } };
            }
        }

        PlcAlarmWriteResultInfo outcome;
        try
        {
            outcome = DecodeWriteResult(item.Operation, result.Payload, policy);
            RequireComplete(item.Operation, outcome);
        }
        catch (JsonException)
        {
            StructuredPayloadProjection.TryWriteProtocolDiagnostic(item.Operation, writeDiagnostic);
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }

        var element = CanonicalJson.ToElement(outcome);
        if (DeclinedConfirmations(item.Operation, outcome) is { } declined)
            return new(item.OperationId, item.Operation, OperationBatchStatus.Failed, element, declined, null, null, warnings);
        return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null,
            AcceptedConfirmationsWarning(item.Operation, outcome) is { } accepted ? warnings.Append(accepted).ToArray() : warnings);
    }

    /// <summary>plc_download's rule: dialogs answered No fail the item (the result is kept); null when none was declined.</summary>
    internal static StructuredOperationFailure? DeclinedConfirmations(string operation, PlcAlarmWriteResultInfo outcome)
        => outcome.PortalConfirmationsObserved == 0 || outcome.PortalConfirmationPolicy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? null
            : new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal raised {outcome.PortalConfirmationsObserved} confirmation dialog(s) during {operation} and this tool answered each one No "
                + $"(see this item's result.portalConfirmations{(outcome.PortalConfirmationsComplete ? string.Empty : $"; only the first {outcome.PortalConfirmations.Count} are listed")}). "
                + "The Siemens call returned, but declining may have changed what it did, so the item is not reported as successful. Inspect the result "
                + "with plc_read before retrying; to answer such dialogs Yes, retry with portalConfirmations: \"accept\".");

    internal static string? AcceptedConfirmationsWarning(string operation, PlcAlarmWriteResultInfo outcome)
        => outcome.PortalConfirmationsObserved > 0 && outcome.PortalConfirmationPolicy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? $"TIA Portal raised {outcome.PortalConfirmationsObserved} confirmation dialog(s) during {operation} and each was answered Yes "
                + "(portalConfirmations: accept); see this item's result.portalConfirmations."
            : null;

    private static void ValidateInventory(PlcTextListInventoryInfo inventory)
    {
        Require(inventory.ProjectLanguages);
        Require(inventory.ProjectLanguages.Active);
        Require(inventory.ProjectLanguages.Messages);
        Require(inventory.Plcs);
        foreach (var plc in inventory.Plcs)
        {
            Require(plc.UserTextLists);
            Require(plc.SystemTextLists);
            Require(plc.Messages);
            foreach (var list in plc.UserTextLists) CheckList(list, isSystem: false);
            foreach (var list in plc.SystemTextLists) CheckList(list, isSystem: true);
        }
    }

    private static void CheckList(PlcTextListInfo list, bool isSystem)
    {
        if (list.IsSystem != isSystem || list.Name is null) throw new JsonException("A text list is filed under the wrong kind.");
        Require(list.Unreadable);
        StandalonePayloadContract.RejectNullElements(list.Comment);
        if (list.Comment?.Any(item => item.Culture is null || item.Text is null) == true) throw new JsonException();
    }

    private static void ValidateWorkbook(string operation, PlcAlarmWorkbookInfo workbook)
    {
        if (workbook.Kind != PlcAlarmMethods.WorkbookKind(operation) || !PlcAlarmPolicy.IsWorkbookHash(workbook.WorkbookHash))
            throw new JsonException("The workbook contradicts the request.");
        Require(workbook.Cultures);
        StandalonePayloadContract.RejectNullElements(workbook.TextListNames);
        Require(workbook.Sheets);
        Require(workbook.Probes);
        Require(workbook.Messages);
        if (workbook.StartRow < 1 || workbook.Sheet is null && workbook.StartRow != 1) throw new JsonException();
        if (PlcAlarmPolicy.ParseColumn(workbook.StartColumn) is not { } startColumn || workbook.Sheet is null && startColumn != 1) throw new JsonException();
        foreach (var sheet in workbook.Sheets)
        {
            Require(sheet.Rows);
            if (sheet.Name is null || sheet.Rows.Count > sheet.RowCount || sheet.RowsTruncated != (sheet.NextStartRow is not null)) throw new JsonException();
            foreach (var row in sheet.Rows)
            {
                Require(row.Cells);
                if (row.Cells.Any(cell => cell.Cell is null || cell.Kind is null || cell.Value is null)) throw new JsonException();
                if (row.CellsTruncated != (row.NextStartColumn is not null) || row.NextStartColumn is not null && PlcAlarmPolicy.ParseColumn(row.NextStartColumn) is null)
                    throw new JsonException();
            }
        }

        if (workbook.Probes.Any(probe => probe.Sheet is null || probe.Cell is null || probe.Value is null || probe.Exists && !probe.SheetExists))
            throw new JsonException();
    }

    private static void ValidateFb(PlcProDiagFbInfo info)
    {
        Require(info.Attributes);
        Require(info.Unreadable);
        if (info.BlockPath is null || info.PlcName is null || info.BlockType is null || info.Attributes.Any(a => a.Name is null)) throw new JsonException();
        if (info.IsProDiag != (info.ProgrammingLanguage == PlcAlarmPolicy.ProDiagLanguage)) throw new JsonException();
    }

    private static void ValidateAssignment(PlcProDiagAssignmentInfo info)
    {
        if (info.TargetKind is null || info.Target is null || info.PlcName is null || !info.Readable && info.AssignedProDiagFb is not null) throw new JsonException();
    }

    private static void ValidateWrite(string operation, PlcAlarmWriteResultInfo result, string? policy)
    {
        if (result.Operation != operation) throw new JsonException("The result names another operation.");
        StandalonePayloadContract.RejectNullElements(result.AppliedMembers);
        StandalonePayloadContract.RejectNullElements(result.Messages);
        StandalonePayloadContract.RejectNullElements(result.Cultures);
        StandalonePayloadContract.RejectNullElements(result.TextListNames);
        StandalonePayloadContract.RejectNullElements(result.Edits);
        StandalonePayloadContract.RejectNullElements(result.UnrequestedChanges);
        StandalonePayloadContract.RejectNullElements(result.Files);
        if (result.TextListAfter is { } after) CheckList(after, isSystem: false);
        if (policy is not null)
            PlcOnlinePayloadContract.ValidatePortalConfirmations(policy, result.PortalConfirmationPolicy, result.PortalConfirmations,
                result.PortalConfirmationsObserved, result.PortalConfirmationsComplete);
        if (result.ImportCheck is { } check
            && (check.Method is not ("rows" or "fileHash" or "none") || !PlcAlarmPolicy.ImportOutcomes.All.Contains(check.Outcome)
                || check.Verified != (check.Outcome == PlcAlarmPolicy.ImportOutcomes.Verified) || check.Verified == (check.Reason is not null)
                || (check.Method == "none") != (check.Outcome == PlcAlarmPolicy.ImportOutcomes.NotReadBack)
                || check.Outcome == PlcAlarmPolicy.ImportOutcomes.Contradicted && check.Method != "rows"
                || check.NotShown?.Any(n => n is null) == true || !PlcAlarmMethods.FileImports.Contains(operation)))
            throw new JsonException("The import check is inconsistent.");
    }

    /// <summary>The stricter checks of a successful write (a failed one's evidence stops wherever the failure happened).</summary>
    public static void RequireComplete(string operation, PlcAlarmWriteResultInfo result)
    {
        if (PlcAlarmMethods.FileExports.Contains(operation) && (result.FilePath is null || result.Sha256 is null || result.Bytes is null || result.Overwritten is null))
            throw new JsonException("An export result names no written file.");
        if (PlcAlarmMethods.CellUpdates.Contains(operation)
            && (result.Edits is null || result.UnrequestedChanges is null || !PlcAlarmPolicy.IsWorkbookHash(result.WorkbookHashBefore) || !PlcAlarmPolicy.IsWorkbookHash(result.WorkbookHashAfter)))
            throw new JsonException("A cell update result carries no edits, unrequested-change list or workbook hashes.");
        if (PlcAlarmMethods.FileImports.Contains(operation) && (result.ImportCheck is null || result.Sha256 is null || result.Bytes is null))
            throw new JsonException("An import result carries no import check or no hash of the imported copy.");
        if (operation == PlcAlarmMethods.ExportProDiagInfo && (result.DirectoryPath is null || result.Files is not { Count: > 0 })) throw new JsonException("A directory export result names no files.");
        if (operation == PlcAlarmMethods.SetProDiagAssignment && (result.TargetKind is null || result.Target is null)) throw new JsonException("An assignment result names no target.");
    }

    private static void Require<T>(T? value) where T : class
    {
        if (value is null) throw new JsonException();
        if (value is System.Collections.IEnumerable items and not string && items.Cast<object?>().Any(item => item is null)) throw new JsonException();
    }
}
