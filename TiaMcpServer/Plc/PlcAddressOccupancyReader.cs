using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// read_address_occupancy and find_free_address: host computations over two existing reads, the PLC
/// tag inventory (<c>list_tag_tables</c>) and the hardware I/O map (<c>read_hardware_config</c> with
/// <c>includeIoDetails</c>). Both reads go through the ordinary gates; nothing is written.
/// </summary>
public static class PlcAddressOccupancyReader
{
    public static bool Handles(string? operation)
        => operation is PlcTagMoveMethods.ReadAddressOccupancy or PlcTagMoveMethods.FindFreeAddress;

    public static async Task<StructuredOperationItem> ExecuteAsync(OpennessWorkerClient client, PlcOperationRequest operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inventoryRequest = new PlcOperationRequest { OperationId = operation.OperationId, Operation = "list_tag_tables", PlcName = operation.PlcName };
        var inventoryItem = PlcPayloadContract.Project(inventoryRequest, await client.ListTagTablesAsync(operation.PlcName).ConfigureAwait(false));
        if (inventoryItem.Result is not { } inventoryElement)
            return StructuredOperationItem.Failed(operation, inventoryItem.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The PLC tag inventory could not be read: {inventoryItem.Failure?.Message}", inventoryItem.Warnings);
        cancellationToken.ThrowIfCancellationRequested();
        var inventory = CanonicalJson.Deserialize<PlcTagInventoryInfo>(inventoryElement.GetRawText());
        var plcs = inventory.Plcs.Where(plc => operation.PlcName is null
            || PlcNameRules.Comparer.Equals(plc.PlcName, operation.PlcName) || PlcNameRules.Comparer.Equals(plc.DeviceName, operation.PlcName)).ToArray();
        if (plcs.Length != 1)
            return StructuredOperationItem.Failed(operation,
                plcs.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                plcs.Length == 0
                    ? $"{(operation.PlcName is null ? "The project has no PLC" : $"PLC '{operation.PlcName}' matches no PLC")}{(inventory.IsComplete ? "." : "; the tag inventory is incomplete.")}"
                    : $"{plcs.Length} PLCs match ({string.Join(", ", plcs.Select(plc => $"'{plc.PlcName}'"))}). Name the PLC with plcName.",
                inventoryItem.Warnings);
        var plc = plcs[0];
        var messages = inventory.Messages.ToList();
        var warnings = inventoryItem.Warnings.ToList();

        var wantsHardware = operation.Operation == PlcTagMoveMethods.FindFreeAddress
            ? operation.RequireHardware == true
            : operation.IncludeHardware != false;
        List<PlcAddressOccupant>? hardware = null;
        if (wantsHardware)
        {
            var read = await client.ReadHardwareConfigAsync(includeIoDetails: true).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            warnings.AddRange(read.Warnings);
            if (!read.Success)
                return StructuredOperationItem.Failed(operation, read.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    $"The hardware I/O map could not be read: {read.Error}", warnings);
            try
            {
                var config = NetworkPayloadContract.DecodeHardwareConfig(read.Payload);
                hardware = PlcAddressSpace.Hardware(config, plc.DeviceName, messages);
                messages.AddRange(config.Messages);
                if (config.DiscoveryEvidence is not { Complete: true }) messages.Add("Hardware discovery is incomplete; some hardware addresses may be missing.");
            }
            catch (JsonException)
            {
                return StructuredOperationItem.Failed(operation, WorkerFailureCategories.ProtocolError,
                    "The hardware configuration payload did not match its declared result contract and was rejected.", warnings);
            }
        }

        object result = operation.Operation == PlcTagMoveMethods.ReadAddressOccupancy
            ? PlcAddressSpace.Occupancy(plc, inventory.IsComplete, operation.Areas ?? PlcAddressSpace.AreaNames, hardware, messages, cancellationToken)
            : PlcAddressSpace.FindFree(plc, inventory.IsComplete, operation.Area!, operation.DataType!, operation.StartByte ?? 0,
                operation.EndByte ?? PlcAddressSpace.DefaultEndByte, operation.AlignBytes ?? 1, operation.RequireHardware == true, hardware, messages,
                cancellationToken);
        return new StructuredOperationItem(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded,
            result is PlcAddressOccupancyResult occupancy ? CanonicalJson.ToElement(occupancy) : CanonicalJson.ToElement((PlcFreeAddressResult)result),
            Failure: null, Omission: null, SkipReason: null, warnings);
    }
}
