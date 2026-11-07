using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Plc;

[McpServerToolType]
public class PlcReadTools
{
    private const string ToolName = "plc_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description(PlcToolDescriptions.Read)]
    public static Task<CallToolResult> PlcRead(
        OpennessWorkerClient workerClient,
        [Description(PlcToolDescriptions.ReadOperationsParameter)] PlcOperationRequest[] operations,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(workerClient, operations, ToolName, PlcContractVersion.Current,
            PlcOperationCatalog.ValidateRead, PlcOperationCatalog.ValidateAccessMode,
            Execute(workerClient, operations, cancellationToken), RetryGuidance, cancellationToken,
            // describe_operations reads only the server's own catalog, so a call of nothing else needs no bound project.
            requiresBinding: operations is not { Length: > 0 } || !operations.All(item => PlcOperationReference.Handles(item?.Operation)));

    /// <summary>
    /// One executor per call, so the analysis items of the call share one evidence snapshot and its alarm workbook reads
    /// share the document budget (<see cref="PlcAlarms.PlcAlarmRequests.CallResultBudget"/>).
    /// </summary>
    private static Func<PlcOperationRequest, Task<StructuredOperationItem>> Execute(OpennessWorkerClient workerClient, IReadOnlyList<PlcOperationRequest?>? operations,
        CancellationToken cancellationToken)
    {
        var analysis = new PlcAnalysis.PlcAnalysisSession(workerClient);
        var workbookResultChars = PlcAlarms.PlcAlarmRequests.CallResultBudget(operations);
        return async operation => PlcOperationReference.Handles(operation.Operation)
            ? PlcOperationReference.Execute(operation)
            : PlcAddressOccupancyReader.Handles(operation.Operation)
            ? await PlcAddressOccupancyReader.ExecuteAsync(workerClient, operation, cancellationToken).ConfigureAwait(false)
            : PlcObjectFinder.Handles(operation.Operation)
                ? await PlcObjectFinder.ExecuteAsync(workerClient, operation, cancellationToken).ConfigureAwait(false)
            : PlcSdNetworkReader.Handles(operation.Operation)
                ? await PlcSdNetworkReader.ExecuteAsync(workerClient, operation, cancellationToken).ConfigureAwait(false)
            : PlcAnalysis.PlcAnalysisPolicy.Handles(operation.Operation)
                ? await PlcAnalysis.PlcAnalysisReader.ExecuteAsync(analysis, operation, cancellationToken).ConfigureAwait(false)
                : PlcPayloadContract.Project(operation,
                    await PlcWorkerInvoker.InvokeReadAsync(workerClient, operation, cancellationToken, workbookResultChars).ConfigureAwait(false));
    }

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => BatchReadTool.ApplyBudget(batch, ToolName, PlcContractVersion.Current,
            RetryGuidance, maxItemChars, maxDocumentChars);

    private static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        Contracts.Plc.PlcOperationHelpMethods.DescribeOperations =>
            $"Name fewer operationNames per item, or split the batch: re-run this operationId in its own {ToolName} call.",
        "get_block_content" or "get_type_content" or Contracts.Plc.SimaticSd.SimaticSdMethods.ListNetworks =>
            "Read a smaller object or drop withDependencies, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call. A single object above the limit needs the server restarted with "
                + "--max-item-chars (or TIA_MCP_MAX_ITEM_CHARS) at or above originalChars and --max-document-chars "
                + "(or TIA_MCP_MAX_DOCUMENT_CHARS) with headroom above it for the response envelope.",
        "list_tag_tables" =>
            "Narrow with plcName, folderPath or tableName (one table is the narrowest), or split the batch: "
                + $"re-run this operationId in its own {ToolName} call.",
        "list_download_targets" or "get_online_state" or "list_watch_and_force_tables" =>
            $"Narrow with plcName, or split the batch: re-run this operationId in its own {ToolName} call.",
        "get_db_values" =>
            $"Narrow with memberPath (a struct, multi-instance or array element), or split the batch: re-run this operationId in its own {ToolName} call.",
        "get_block_interface" or "get_type_interface" =>
            $"Split the batch: re-run this operationId in its own {ToolName} call; an interface above the limit needs the server restarted with --max-item-chars above originalChars.",
        PlcTagMoveMethods.ReadAddressOccupancy =>
            $"Narrow with plcName or areas, or set includeHardware false, or split the batch: re-run this operationId in its own {ToolName} call.",
        "compare_to_online" or "compare_software" or "compare_hardware" or "compare_hardware_to_online" =>
            "Lower maxElements or maxDepth, keep differencesOnly true, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call.",
        "read_text_list_workbook" or "read_alarm_text_workbook" or "read_supervision_workbook" =>
            $"Lower maxElements, read one sheet with sheet and page with startRow = nextStartRow (and startColumn = nextStartColumn for a cut row), narrow with textListNames or cultures, or split the batch: re-run this operationId in its own {ToolName} call.",
        "list_text_lists" =>
            $"Narrow with plcName, or split the batch: re-run this operationId in its own {ToolName} call.",
        "read_watch_table" or "read_force_table" =>
            $"Lower maxElements, or split the batch: re-run this operationId in its own {ToolName} call.",
        "get_watch_table_content" or "get_force_table_content" =>
            $"Split the batch: re-run this operationId in its own {ToolName} call. A single table above the limit needs the server restarted with "
                + "--max-item-chars (or TIA_MCP_MAX_ITEM_CHARS) at or above originalChars.",
        PlcSourceFolderMethods.FindObjects =>
            $"Lower limit and page with offset/nextOffset, narrow with plcName or kinds, or split the batch: re-run this operationId in its own {ToolName} call.",
        PlcSourceFolderMethods.ListExternalSources or PlcSourceFolderMethods.ScanAccessibleDevices or PlcSourceFolderMethods.ReadTagComments =>
            $"Narrow with plcName (read_tag_comments: tableName or folderPath; scan_accessible_devices: lower maxElements), or split the batch: re-run this operationId in its own {ToolName} call.",
        "list_technology_objects" =>
            $"Narrow with plcName and folderPath, or split the batch: re-run this operationId in its own {ToolName} call.",
        "get_technology_object_parameters" =>
            $"Lower limit, narrow with prefix or read one parameterName, or split the batch: re-run this operationId in its own {ToolName} call.",
        "get_technology_object_content" =>
            $"Split the batch: re-run this operationId in its own {ToolName} call. A single object above the limit needs the server restarted with "
                + "--max-item-chars (or TIA_MCP_MAX_ITEM_CHARS) at or above originalChars.",
        "get_call_tree" or "find_call_paths" or "find_unused_blocks" or "find_orphaned_instance_dbs" or "analyze_change_impact" =>
            "Lower maxElements or maxDepth, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call. Each result also carries a fixed evidence section of about 3,000 characters.",
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, PlcContractVersion.Current, category, message);
}
