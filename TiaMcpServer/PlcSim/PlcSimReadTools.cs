using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.PlcSim;

[McpServerToolType]
public class PlcSimReadTools
{
    internal const string ToolName = "plcsim_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Read S7-PLCSIM Advanced virtual CPUs (S7-1500 instances) with up to 50 independent operations: probe_api (no fields; where the PLCSIM Advanced API was found and how every API member the server uses resolved - run it first), list_instances (no fields; registered instances, reflective projection), get_instance (instance: name, id, operatingState, operatingMode, scaleFactor, communicationInterface, cpuType; unavailable members are null with a warning), list_tags (instance, optional nameFilter and max 1-5000 default 500; reads IInstance.TagInfos and never refreshes the tag list: right after a download it was empty live until plcsim_write update_tag_list; every STagInfo member is projected reflectively, an array of objects such as Dimension as a JSON array of objects (an empty array as [], not live-tested); Offset is PLCSIM's value and was 0 for every member of an optimized DB live), read_tags (instance, tags [{name, dataType}] with dataType bool, int8, int16, int32, int64, uint8, uint16, uint32, uint64, float, double or string; a Byte/USInt tag needs uint8 (ReadInt8 on a Byte failed TypeMismatch live); per-tag results). instance is the exact, case-sensitive name from the PLCSIM Advanced Control Panel. Runs in a separate PLCSIM worker started on first use; never touches TIA Portal or the project binding. Requires S7-PLCSIM Advanced installed and licensed; API members are bound at runtime and a missing one fails plcsim_api_member_missing.")]
    public static async Task<CallToolResult> PlcSimRead(
        PlcSimWorkerClient plcSimClient,
        [Description("Ordered PLCSIM read operations, each with a unique operationId and an operation.")] PlcSimOperationRequest[] operations,
        CancellationToken cancellationToken = default)
    {
        var validation = PlcSimCatalog.Validate(operations, write: false);
        if (!validation.IsValid)
            return Error(validation.Category, validation.Error);
        var denied = PlcSimCatalog.ValidateAccessMode(operations, plcSimClient.AccessPolicy.Mode);
        if (denied.Count != 0)
            return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", denied));

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync<PlcSimOperationRequest>(operations,
            async item => PlcSimPayloadContract.Project(item,
                await plcSimClient.InvokeAsync(PlcSimCatalog.WorkerMethod(item.Operation), PlcSimCatalog.ToRequest(item)).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
        var bounded = BatchReadTool.ApplyBudget(batch, ToolName, PlcSimContractVersion.Current, RetryGuidance);
        return StructuredToolResult.Create(new OperationBatchReadResponse(ToolName, PlcSimContractVersion.Current,
            bounded.IsFullySuccessful, null, Array.Empty<string>(), bounded), isError: false);
    }

    internal static string RetryGuidance(StructuredOperationItem item)
        => item.Operation == "list_tags"
            ? "Narrow nameFilter or lower max, or re-run this operationId in its own plcsim_read call."
            : "Re-run this operationId in its own plcsim_read call; for one value above the limit, raise --max-item-chars and --max-document-chars.";

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, PlcSimContractVersion.Current, category, message);
}
