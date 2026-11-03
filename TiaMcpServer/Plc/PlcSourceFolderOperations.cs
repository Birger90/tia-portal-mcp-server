using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using static TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods;

namespace TiaMcpServer.Plc;

/// <summary>
/// Worker dispatch and the only decoder of the external-source, group-rename, accessible-device and file-export
/// results. Each operation declares exactly one result type; a payload that does not decode as it, or that
/// contradicts the request, is a <c>protocol_error</c> and is never echoed back. The host-only operations
/// (<see cref="HostReads"/>, <see cref="HostWrites"/>) run in <see cref="PlcObjectFinder"/> and <see cref="PlcFileExports"/>.
/// </summary>
public static class PlcSourceFolderOperations
{
    public static bool IsWorkerRead(string? operation)
        => operation is not null && (ObserveWorkerReads.Contains(operation, StringComparer.Ordinal) || OnlineObserveWorkerReads.Contains(operation, StringComparer.Ordinal));

    public static Task<WorkerCallResult> InvokeReadAsync(OpennessWorkerClient client, PlcOperationRequest operation)
        => client.PlcSourceFolderAsync(operation.Operation, Request(operation));

    public static async Task<WorkerCallResult> InvokeWriteAsync(OpennessWorkerClient client, PlcOperationRequest operation, CancellationToken cancellationToken = default)
    {
        if (HostWrites.Contains(operation.Operation, StringComparer.Ordinal))
            return await PlcFileExports.ExecuteAsync(client, operation, cancellationToken).ConfigureAwait(false);
        var call = await client.PlcSourceFolderAsync(operation.Operation, Request(operation)).ConfigureAwait(false);
        // export_source_file writes at a caller path on this machine: report a mapped drive or link like the host exports.
        return call.Success && operation.Operation == ExportSourceFile && PlcFileExports.LocationWarning(Path.GetDirectoryName(operation.FilePath)) is { } warning
            ? call with { Warnings = call.Warnings.Append($"{operation.Operation}: {warning}").ToList() }
            : call;
    }

    /// <summary>The worker parameters of one operation; only the method's own fields are read by the worker.</summary>
    internal static PlcSourceFolderRequestInfo Request(PlcOperationRequest op) => new()
    {
        PlcName = op.PlcName,
        UnitName = op.UnitName,
        TableName = op.Operation == ReadTagComments ? op.TableName : null,
        SourceName = op.SourceName,
        FilePath = op.FilePath,
        GenerateOption = op.GenerateOption,
        TargetGroupPath = op.Operation == GenerateBlocksFromSource ? op.TargetGroupPath : null,
        BlockPaths = op.BlockPaths?.ToList(),
        TypePaths = op.TypePaths?.ToList(),
        WithDependencies = op.WithDependencies == true,
        Overwrite = op.Overwrite == true,
        GroupPath = op.Operation switch
        {
            RenameBlockGroup => op.BlockPath,
            RenameTypeGroup or CreateTypeGroup or DeleteTypeGroup => op.TypePath,
            _ => null,
        },
        FolderPath = op.FolderPath,
        NewName = op.NewName,
        Mode = op.Mode,
        PcInterfaceName = op.PcInterface?.Name,
        PcInterfaceNumber = op.PcInterface?.Number,
        MaxDevices = op.Operation == ScanAccessibleDevices ? op.MaxElements ?? PlcAccessibleDeviceProjection.DefaultMaxDevices : null,
    };

    /// <exception cref="JsonException">The payload does not decode as the operation's one declared type.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => operation.Operation switch
        {
            ListExternalSources => CanonicalJson.NormalizeWorkerPayload<PlcExternalSourceInventoryInfo>(payload, inventory =>
            {
                if (inventory.Plcs is null || inventory.Messages is null) throw new JsonException();
                StandalonePayloadContract.RejectNullElements(inventory.Plcs);
                StandalonePayloadContract.RejectNullElements(inventory.Messages);
                foreach (var plc in inventory.Plcs)
                {
                    if (plc.Sources is null) throw new JsonException();
                    StandalonePayloadContract.RejectNullElements(plc.Sources);
                }
            }).Element,
            ScanAccessibleDevices => CanonicalJson.NormalizeWorkerPayload<PlcAccessibleDeviceScanInfo>(payload, scan =>
            {
                if (scan.Devices is null) throw new JsonException();
                StandalonePayloadContract.RejectNullElements(scan.Devices);
                if (!string.Equals(scan.Mode, operation.Mode, StringComparison.Ordinal)
                    || !string.Equals(scan.PcInterfaceName, operation.PcInterface?.Name, StringComparison.Ordinal)
                    || scan.PcInterfaceNumber != operation.PcInterface?.Number
                    || scan.Devices.Count > scan.MaxDevices || scan.DeviceCount < scan.Devices.Count
                    || scan.Truncated != (scan.DeviceCount > scan.Devices.Count))
                    throw new JsonException("The scan result contradicts the request.");
            }).Element,
            ReadTagComments => CanonicalJson.NormalizeWorkerPayload<PlcTagCommentInventoryInfo>(payload, inventory =>
            {
                if (inventory.Plcs is null || inventory.Messages is null || inventory.Complete != (inventory.Messages.Count == 0)) throw new JsonException();
                StandalonePayloadContract.RejectNullElements(inventory.Plcs);
                StandalonePayloadContract.RejectNullElements(inventory.Messages);
                foreach (var plc in inventory.Plcs)
                {
                    if (plc.Tables is null) throw new JsonException();
                    StandalonePayloadContract.RejectNullElements(plc.Tables);
                    foreach (var table in plc.Tables)
                    {
                        if (table.Members is null) throw new JsonException();
                        StandalonePayloadContract.RejectNullElements(table.Members);
                        if (operation.TableName is not null && !string.Equals(table.TableName, operation.TableName, StringComparison.OrdinalIgnoreCase))
                            throw new JsonException("The comment read contradicts the requested tableName.");
                        foreach (var member in table.Members)
                        {
                            if (member.Kind is not ("Tag" or "UserConstant")) throw new JsonException();
                            if (member.Comment is { } comment) StandalonePayloadContract.RejectNullElements(comment);
                        }
                    }
                }
            }).Element,
            _ => throw new JsonException($"No declared result contract for PLC read operation '{operation.Operation}'."),
        };

    /// <exception cref="JsonException">The payload does not decode as the operation's one declared type.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest operation, string payload)
    {
        switch (operation.Operation)
        {
            case ImportExternalSource:
            case DeleteExternalSource:
                return CanonicalJson.NormalizeWorkerPayload<PlcExternalSourceMutationInfo>(payload, mutation =>
                {
                    if (mutation.Operation != operation.Operation || !mutation.ReadBackConfirmed
                        || operation.SourceName is not null && !string.Equals(mutation.SourceName, operation.SourceName.Trim(), StringComparison.Ordinal)
                        || operation.Operation == ImportExternalSource && !string.Equals(mutation.FilePath, operation.FilePath, StringComparison.Ordinal))
                        throw new JsonException("The external-source result contradicts the request.");
                }).Element;
            case GenerateBlocksFromSource:
                return CanonicalJson.NormalizeWorkerPayload<PlcSourceGenerationInfo>(payload, generation =>
                {
                    if (generation.Generated is null || !string.Equals(generation.SourceName, operation.SourceName?.Trim(), StringComparison.Ordinal)
                        || !string.Equals(generation.GenerateOption, operation.GenerateOption ?? "None", StringComparison.Ordinal))
                        throw new JsonException("The generation result contradicts the request.");
                    StandalonePayloadContract.RejectNullElements(generation.Generated);
                }).Element;
            case ExportSourceFile:
                return CanonicalJson.NormalizeWorkerPayload<PlcSourceFileExportInfo>(payload, export =>
                {
                    if (export.Objects is null || !string.Equals(export.FilePath, operation.FilePath, StringComparison.Ordinal) || !Sha256Text.IsMatch(export.Sha256 ?? string.Empty)
                        || export.Bytes < 0 || export.Overwritten && operation.Overwrite != true)
                        throw new JsonException("The source file result contradicts the request.");
                    StandalonePayloadContract.RejectNullElements(export.Objects);
                }).Element;
            case RenameBlockGroup:
            case RenameTypeGroup:
            case RenameTagTableGroup:
            case RenameWatchTableGroup:
            case RenameSoftwareUnit:
                return CanonicalJson.NormalizeWorkerPayload<PlcGroupRenameInfo>(payload, rename =>
                {
                    if (rename.Operation != operation.Operation || !string.Equals(rename.NewName, operation.NewName?.Trim(), StringComparison.Ordinal))
                        throw new JsonException("The group rename result contradicts the request.");
                }).Element;
            case CreateTypeGroup:
            case DeleteTypeGroup:
            case CreateTagTableGroup:
            case DeleteTagTableGroup:
            case CreateSoftwareUnit:
            case DeleteSoftwareUnit:
                return CanonicalJson.NormalizeWorkerPayload<PlcContainerMutationInfo>(payload, mutation =>
                {
                    var expectedKind = operation.Operation switch
                    {
                        CreateTypeGroup or DeleteTypeGroup => PlcSourceFolderKinds.TypeGroup,
                        CreateTagTableGroup or DeleteTagTableGroup => PlcSourceFolderKinds.TagTableGroup,
                        _ => PlcSourceFolderKinds.SoftwareUnit,
                    };
                    if (mutation.Operation != operation.Operation || mutation.Kind != expectedKind || string.IsNullOrEmpty(mutation.Name)
                        || expectedKind == PlcSourceFolderKinds.SoftwareUnit && !string.Equals(mutation.UnitName, mutation.Name, StringComparison.Ordinal)
                        || operation.Operation is CreateTypeGroup or CreateTagTableGroup or CreateSoftwareUnit
                            && !string.Equals(mutation.Name, RequestedLeaf(operation), StringComparison.OrdinalIgnoreCase))
                        throw new JsonException("The container result contradicts the request.");
                }).Element;
            case ExportTagTablesCsv:
            case ExportWatchTablesCsv:
            case ExportForceTablesCsv:
                return CanonicalJson.NormalizeWorkerPayload<PlcCsvExportResultInfo>(payload).Element;
            case ExportBlocks:
                return CanonicalJson.NormalizeWorkerPayload<PlcBlockExportResultInfo>(payload).Element;
            default:
                throw new JsonException($"No declared result contract for PLC write operation '{operation.Operation}'.");
        }
    }

    /// <summary>The name a create asks for: the last typePath or folderPath segment, or unitName.</summary>
    private static string? RequestedLeaf(PlcOperationRequest operation) => operation.Operation switch
    {
        CreateTypeGroup => operation.TypePath?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim(),
        CreateTagTableGroup => operation.FolderPath?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim(),
        _ => operation.UnitName?.Trim(),
    };

    /// <summary>The external sources of one planned PLC (matched by its software and device name exactly once) and whether the read was complete.</summary>
    internal static async Task<(PlcExternalSourcePlcInfo? Sources, bool Complete, Safety.Pipeline.WriteToolError? Error)> ReadSourcesAsync(
        OpennessWorkerClient client, PlcTagInventoryPlcInfo plc)
    {
        var request = new PlcOperationRequest { OperationId = "plan", Operation = ListExternalSources, PlcName = plc.PlcName };
        var call = await InvokeReadAsync(client, request).ConfigureAwait(false);
        if (!call.Success)
            return (null, false, new(call.FailureCategory ?? Contracts.Worker.WorkerFailureCategories.WorkerOperationFailed,
                $"The external sources of PLC '{plc.PlcName}' could not be read: {call.Error}"));
        try
        {
            var inventory = CanonicalJson.Deserialize<PlcExternalSourceInventoryInfo>(DecodeRead(request, call.Payload).GetRawText());
            var match = inventory.Plcs.Where(p => p.PlcName == plc.PlcName && p.DeviceName == plc.DeviceName).ToList();
            return match.Count == 1 ? (match[0], inventory.Complete, null)
                : (null, false, new(Contracts.Worker.WorkerFailureCategories.StateChanged,
                    $"The external-source inventory does not describe PLC '{plc.PlcName}' on device '{plc.DeviceName}' exactly once."));
        }
        catch (JsonException)
        {
            return (null, false, new(Contracts.Worker.WorkerFailureCategories.ProtocolError,
                "The list_external_sources payload did not match its declared result contract and was rejected."));
        }
    }

    private static readonly Regex Sha256Text = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
}
