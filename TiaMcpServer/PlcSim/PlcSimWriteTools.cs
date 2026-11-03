using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.PlcSim;

[McpServerToolType]
public class PlcSimWriteTools
{
    internal const string ToolName = "plcsim_write";

    [McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcSimWriteResponse))]
    [Description("Control S7-PLCSIM Advanced virtual CPUs with up to 50 ordered operations: power_on, power_off, run, stop, memory_reset, update_tag_list (refreshes IInstance.TagInfos; an unverified source reports it can cold-restart the virtual CPU), write_tags (tags [{name, dataType, value}], dataType bool, int8, int16, int32, int64, float or double; string is read-only), set_scale_factor (value > 0). Every operation takes instance, the exact case-sensitive instance name. Creating, registering or unregistering instances is out of scope: create and configure instances in the PLCSIM Advanced Control Panel. Omitted dryRun or false executes; true returns the resolved plan without starting the worker or touching the runtime. Stops at the first failed item (write_tags also stops at its first failed tag) without rollback. Control results report the API return value (returnCheck: a negative ERuntimeErrorCode fails the item with plcsim_runtime_error, a positive one is a warning and later items still run) plus operatingState before/after; power_on and run warn when the instance is not on the PLCSIM softbus. No confirmation prompt; each call is audited. Runs in the separate PLCSIM worker; never touches TIA Portal or the project binding.")]
    public static async Task<CallToolResult> PlcSimWrite(
        PlcSimWorkerClient plcSimClient,
        PlcSimAudit audit,
        [Description("Ordered PLCSIM control operations, each with a unique operationId, operation and instance.")] PlcSimOperationRequest[] operations,
        [Description("True returns the resolved plan without touching the runtime; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var startedAt = audit.Now;
        var startTimestamp = audit.Timestamp;
        var mode = plcSimClient.AccessPolicy.Mode;

        CallToolResult Finish(PlcSimWriteResponse response, bool isError, IReadOnlyList<WriteAuditItem> items, bool actual)
        {
            var text = CanonicalJson.Serialize(response);
            audit.Append(ToolName, mode, actual, operations, response.Phase, text, items, startedAt, startTimestamp);
            return StructuredToolResult.CreateCanonical(text, isError);
        }

        IReadOnlyList<WriteAuditItem> NotRun()
            => operations.Select(item => PlcSimAudit.Item(item?.OperationId ?? string.Empty, item?.Operation ?? string.Empty,
                item?.Instance, OperationBatchStatus.Skipped, null, Array.Empty<string>(), null)).ToArray();

        var validation = PlcSimCatalog.Validate(operations, write: true);
        if (!validation.IsValid)
            return Finish(Rejection(dryRun, validation.Category, validation.Error), true, NotRun(), false);
        var denied = PlcSimCatalog.ValidateAccessMode(operations, mode);
        if (denied.Count != 0)
            return Finish(Rejection(dryRun, WorkerFailureCategories.AccessDenied, string.Join("\n", denied)), true, NotRun(), false);

        if (dryRun)
        {
            var plan = operations.Select(Plan).ToArray();
            return Finish(new PlcSimWriteResponse(ToolName, PlcSimContractVersion.Current, true, PlcSimPhases.Preview, true, null,
                Array.Empty<string>(), plan, null), false, NotRun(), false);
        }

        // Cancellation is honoured only before the first worker call: once a simulated CPU may have
        // changed, the call finishes its accounting and audit instead of disappearing.
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<StructuredOperationItem>();
        var durations = new List<long?>();
        var stopped = false;
        foreach (var item in operations)
        {
            if (stopped)
            {
                results.Add(new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Skipped, null, null, null,
                    StructuredOperationSkipReasons.EarlierOperationFailed, Array.Empty<string>()));
                durations.Add(null);
                continue;
            }

            var itemStart = audit.Timestamp;
            var worker = await plcSimClient.InvokeAsync(PlcSimCatalog.WorkerMethod(item.Operation), PlcSimCatalog.ToRequest(item)).ConfigureAwait(false);
            var projected = PlcSimPayloadContract.Project(item, worker);
            if (item.Operation == "update_tag_list" && !projected.Warnings.Contains(PlcSimWarnings.UpdateTagListColdRestart, StringComparer.Ordinal))
                projected = projected with { Warnings = new[] { PlcSimWarnings.UpdateTagListColdRestart }.Concat(projected.Warnings).ToArray() };
            results.Add(projected);
            durations.Add(audit.ElapsedMs(itemStart));
            stopped = projected.Status != OperationBatchStatus.Succeeded;
        }

        var batch = StructuredOperationBatchPayloadBudget.Apply(StructuredOperationBatch.FromItems(results),
            value => Applied(value), ToolName, RetryGuidance);
        var auditItems = batch.Operations.Select((item, index) => PlcSimAudit.Item(item.OperationId, item.Operation,
            operations[index].Instance, item.Status, item.Failure, item.Warnings, durations[index])).ToArray();
        return Finish(Applied(batch), false, auditItems, true);
    }

    private static PlcSimWriteResponse Applied(StructuredOperationBatch batch)
        => new(ToolName, PlcSimContractVersion.Current, batch.IsFullySuccessful, PlcSimPhases.Applied, false, null,
            Array.Empty<string>(), null, batch);

    private static PlcSimWriteResponse Rejection(bool dryRun, string category, string message)
        => new(ToolName, PlcSimContractVersion.Current, false, PlcSimPhases.Error, dryRun,
            new OperationBatchReadError(category, message), Array.Empty<string>(), null, null);

    internal static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(Rejection(false, category, message), isError: true);

    private static PlcSimPlannedOperation Plan(PlcSimOperationRequest item)
    {
        var request = PlcSimCatalog.ToRequest(item);
        if (request.Tags is not null)
        {
            // Show the typed values exactly as they will be written.
            foreach (var tag in request.Tags)
            {
                PlcSimValueCodec.TryParse(tag.DataType!, tag.Value, out var typed, out _);
                tag.Value = PlcSimValueCodec.ToJson(typed);
            }
        }

        var warnings = item.Operation == "update_tag_list" ? new[] { PlcSimWarnings.UpdateTagListColdRestart } : Array.Empty<string>();
        return new PlcSimPlannedOperation(item.OperationId, item.Operation, PlcSimCatalog.WorkerMethod(item.Operation), item.Instance!,
            PlcSimCatalog.ApiMembers(item), request, warnings);
    }

    private static string RetryGuidance(StructuredOperationItem item)
        => "Inspect the instance with plcsim_read get_instance or read_tags before retrying; this write may have been applied.";
}
