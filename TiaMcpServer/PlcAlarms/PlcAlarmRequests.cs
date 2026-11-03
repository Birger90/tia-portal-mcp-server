using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;

namespace TiaMcpServer.PlcAlarms;

/// <summary>The worker parameters of one validated alarm, text list, supervision or ProDiag operation.</summary>
public static class PlcAlarmRequests
{
    /// <summary>Room left in the response document for its envelope and the call's other items when workbook reads share it.</summary>
    public const int DocumentHeadroomChars = 20_000;

    /// <summary>
    /// The characters one workbook read's rows may fill: the effective value budget (--max-item-chars) minus
    /// <see cref="PlcAlarmPolicy.ResultHeadroomChars"/> for the rest of the item, and, when one plc_read call holds
    /// <paramref name="workbookReads"/> workbook reads, at most their equal share of the document budget
    /// (--max-document-chars) minus <see cref="DocumentHeadroomChars"/>, never below <see cref="PlcAlarmPolicy.MinResultChars"/>.
    /// The worker stops at this budget (every row, and every cell of a wide first row, that does not fit is paged), so a
    /// read's result does not exceed the value budget. The document budget can still drop whole results when the call's
    /// other items are large or so many reads share it that the floor applies (from 27 reads with the defaults);
    /// such a result is omitted with retry guidance, never cut.
    /// </summary>
    public static int ResultBudget(int workbookReads = 1)
    {
        var item = StructuredOperationBatchPayloadBudget.MaxItemChars - PlcAlarmPolicy.ResultHeadroomChars;
        var share = (StructuredOperationBatchPayloadBudget.MaxDocumentChars - DocumentHeadroomChars) / Math.Max(1, workbookReads) - PlcAlarmPolicy.ResultHeadroomChars;
        return Math.Max(PlcAlarmPolicy.MinResultChars, Math.Min(item, share));
    }

    /// <summary>The <see cref="ResultBudget"/> of each workbook read of one plc_read call, from the call's workbook reads.</summary>
    public static int CallResultBudget(IEnumerable<PlcOperationRequest?>? operations)
        => ResultBudget(operations?.Count(operation => operation?.Operation is { } name && PlcAlarmMethods.TemporaryExportMethods.Contains(name)) ?? 0);

    public static PlcAlarmRequestInfo From(PlcOperationRequest operation, IReadOnlyList<PlcAlarmCellRefInfo>? probes = null, int? maxRows = null,
        int? maxResultChars = null) => new()
    {
        PlcName = operation.PlcName,
        BlockPath = operation.BlockPath,
        TextListName = operation.TextListName,
        NewName = operation.NewName,
        ListRange = operation.ListRange,
        Comment = operation.Comment,
        CommentCulture = operation.CommentCulture,
        TextListNames = operation.TextListNames?.ToList(),
        Cultures = operation.Cultures?.ToList(),
        ExportOption = operation.ExportOption,
        ImportOption = operation.ImportOption,
        FilePath = operation.FilePath,
        Overwrite = operation.Overwrite == true,
        CellEdits = operation.CellEdits?.Select(edit => new PlcAlarmCellEditInfo
        {
            Sheet = edit.Sheet ?? string.Empty,
            Cell = edit.Cell ?? string.Empty,
            Expected = edit.Expected,
            Value = edit.Value,
            Kind = edit.Kind,
        }).ToList(),
        ExpectedWorkbookHash = operation.ExpectedWorkbookHash,
        MaxRows = maxRows ?? operation.MaxElements ?? PlcAlarmPolicy.DefaultMaxRows,
        Sheet = operation.Sheet,
        StartRow = operation.StartRow ?? 1,
        StartColumn = operation.StartColumn ?? "A",
        MaxResultChars = maxResultChars ?? ResultBudget(),
        Probes = probes?.ToList(),
        PortalConfirmations = PlcAlarmMethods.IsWriteMethod(operation.Operation)
            ? operation.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline : null,
        MemberPath = operation.MemberPath,
        TableName = operation.TableName,
        FolderPath = operation.FolderPath,
        Name = operation.Name,
        ProDiagFb = operation.ProDiagFb,
        DirectoryPath = operation.DirectoryPath,
    };

    /// <summary>The planning or verification read of a cell update: the same workbook, one row, the edited cells as probes.</summary>
    public static PlcAlarmRequestInfo WorkbookProbe(PlcOperationRequest operation)
    {
        var request = From(operation,
            (operation.CellEdits ?? Array.Empty<PlcAlarmCellEditRequest>()).Select(edit => new PlcAlarmCellRefInfo { Sheet = edit.Sheet ?? string.Empty, Cell = edit.Cell ?? string.Empty }).ToList(),
            maxRows: 1);
        request.CellEdits = null;
        request.ExpectedWorkbookHash = null;
        request.PortalConfirmations = null;
        request.Sheet = null;
        request.StartRow = 1;
        request.StartColumn = "A";
        return request;
    }

    /// <summary>The planning evidence request (plc_alarm_plan) of one write item.</summary>
    public static PlcAlarmRequestInfo Plan(PlcOperationRequest operation)
    {
        var request = From(operation);
        request.PlannedOperation = operation.Operation;
        request.CellEdits = null;
        return request;
    }
}
