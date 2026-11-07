using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>Maps validated hardware-module requests onto their worker methods (export_io_map stays in the host).</summary>
public static class HardwareModuleInvoker
{
    public static bool Handles(string? operation) => NetworkOperationCatalog.IsHardwareModuleOperation(operation);

    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
    {
        if (operation.Operation == HardwareModuleMethods.ExportIoMap)
            return HardwareIoMapExport.ExecuteAsync(client, operation);
        HardwareModuleRequestInfo request;
        try
        {
            request = BuildRequest(operation);
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, exception.Message));
        }

        return client.HardwareModuleAsync(operation.Operation, request);
    }

    /// <summary>Planning read: CanPlugNew/CanPlugMove/CanPlugCopy for one plug, move or copy item.</summary>
    public static Task<WorkerCallResult> ProbePlugAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
    {
        var request = BuildRequest(operation);
        request.ProbeOperation = operation.Operation switch
        {
            HardwareModuleMethods.PlugDeviceItem => HardwareModuleMethods.ProbeOperations.Plug,
            HardwareModuleMethods.MoveDeviceItem => HardwareModuleMethods.ProbeOperations.Move,
            HardwareModuleMethods.CopyDeviceItem => HardwareModuleMethods.ProbeOperations.Copy,
            _ => throw new ArgumentException($"'{operation.Operation}' has no plug feasibility probe."),
        };
        return client.HardwareModuleAsync(HardwareModuleMethods.ProbePlug, request);
    }

    /// <summary>Planning read: the requested attributes' current typed values and conversion verdicts.</summary>
    public static Task<WorkerCallResult> ReadAttributesAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
    {
        var request = BuildRequest(operation);
        request.AttributeNames = request.Attributes?.Select(attribute => attribute.Name).ToList();
        request.AttributeErrorPolicy = null;
        request.AttributeWriteMode = null;
        return client.HardwareModuleAsync(HardwareModuleMethods.ReadAttributes, request);
    }

    /// <summary>Planning read: the current addresses of the target device item.</summary>
    public static Task<WorkerCallResult> ListAddressesAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
        => client.HardwareModuleAsync(HardwareModuleMethods.ListIoAddresses, new HardwareModuleRequestInfo { Target = Selector(operation.Target) });

    /// <exception cref="ArgumentException">An attribute value no longer converts (the catalog validated it first).</exception>
    internal static HardwareModuleRequestInfo BuildRequest(NetworkOperationRequest operation) => new()
    {
        Target = operation.Operation == HardwareModuleMethods.DeleteIoSystem ? null : Selector(operation.Target),
        Destination = operation.Operation is HardwareModuleMethods.MoveDeviceItem or HardwareModuleMethods.CopyDeviceItem
            ? Selector(operation.Destination) ?? ParentOf(operation.Target!)
            : null,
        TypeIdentifier = operation.TypeIdentifier,
        Name = operation.Operation == HardwareModuleMethods.PlugDeviceItem ? operation.DeviceItemName ?? string.Empty : null,
        PositionNumber = operation.PositionNumber,
        Attributes = operation.Attributes?.Select(attribute => NetworkOperationCatalog.TryConvertAttributeValue(attribute, out var value, out var error)
            ? value! : throw new ArgumentException($"Attribute '{attribute.Name}': {error}")).ToList(),
        AttributeErrorPolicy = operation.Operation == HardwareModuleMethods.SetHardwareAttributes
            ? operation.AttributeErrorPolicy ?? HardwareModuleMethods.AttributeErrorPolicies.Abort : null,
        AttributeWriteMode = operation.Operation == HardwareModuleMethods.SetHardwareAttributes
            ? operation.AttributeWriteMode ?? HardwareModuleMethods.AttributeWriteModes.Bulk : null,
        Address = operation.Address is { } address
            ? new HardwareAddressSelectorInfo { IoType = address.IoType!, StartAddress = address.StartAddress!.Value, Length = address.Length }
            : null,
        AddressChanges = operation.AddressChanges is { } changes
            ? new HardwareAddressChangesInfo
            {
                StartAddress = changes.StartAddress,
                ProcessImage = changes.ProcessImage,
                IsochronousMode = changes.IsochronousMode,
                InterruptObNumber = changes.InterruptObNumber,
            }
            : null,
        CustomIdentityKey = operation.Key,
        CustomIdentityValue = operation.Operation == HardwareModuleMethods.SetCustomIdentity ? operation.Value : null,
        Partner = Selector(operation.Partner),
        FilePath = operation.Operation is HardwareModuleMethods.ExportCax or HardwareModuleMethods.ImportCax
            or HardwareFeatureMethods.ExportTelecontrolDataPoints or HardwareFeatureMethods.ImportTelecontrolDataPoints ? operation.FilePath : null,
        NodeName = operation.NodeName,
        NewSubnetName = operation.NewSubnetName,
        PartnerNodeName = operation.PartnerNodeName,
        AssignmentMode = operation.AssignmentMode,
        ConnectionType = operation.ConnectionType,
        ConnectionIndex = operation.ConnectionIndex,
        ConnectionName = operation.ConnectionName,
        LogFilePath = operation.LogFilePath,
        DeviceName = operation.Operation == HardwareModuleMethods.ExportCax ? operation.DeviceName : null,
        // export_cax: the file and the log; import_cax: the log only (security review F4).
        Overwrite = operation.Operation is HardwareModuleMethods.ExportCax or HardwareModuleMethods.ImportCax or HardwareFeatureMethods.ExportTelecontrolDataPoints ? operation.Overwrite : null,
        CaxImportOption = operation.ImportOption,
        TransferArea = operation.TransferArea is { } area
            ? new TransferAreaRequestInfo
            {
                Collection = area.Collection ?? string.Empty, Index = area.Index, Name = area.Name, Type = area.Type,
                PositionNumber = area.PositionNumber, DataLength = area.DataLength, TemplateIndex = area.TemplateIndex,
            }
            : null,
        IoSystem = operation.Operation == HardwareModuleMethods.DeleteIoSystem && operation.Target is { } ioSystem
            ? new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.IoSystem, SubnetId = ioSystem.SubnetId, Number = ioSystem.Number,
                IoSystemIndex = ioSystem.IoSystemIndex, IoSystemName = ioSystem.IoSystemName,
            }
            : null,
    };

    /// <summary>Planning read: both transfer area compositions of the target interface (create/update/delete_transfer_area).</summary>
    public static Task<WorkerCallResult> ListTransferAreasAsync(OpennessWorkerClient client, NetworkObjectTarget target)
        => client.HardwareModuleAsync(HardwareModuleMethods.ListTransferAreas, new HardwareModuleRequestInfo { Target = Selector(target) });

    /// <summary>Planning read: the current links of the target port (connect_ports, disconnect_ports).</summary>
    public static Task<WorkerCallResult> ListPortsAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
        => client.HardwareModuleAsync(HardwareModuleMethods.ListPortConnections, new HardwareModuleRequestInfo { Target = Selector(operation.Target) });

    /// <summary>Planning read: the current custom identity of the key (set_custom_identity, remove_custom_identity).</summary>
    public static Task<WorkerCallResult> ReadCustomIdentityAsync(OpennessWorkerClient client, NetworkOperationRequest operation)
        => client.HardwareModuleAsync(HardwareModuleMethods.GetCustomIdentity, new HardwareModuleRequestInfo
        {
            Target = Selector(operation.Target),
            CustomIdentityKey = operation.Key,
        });

    /// <summary>A 'device' target names the device (null path); a 'deviceItem' target its exact indexed path.</summary>
    internal static HardwareObjectSelectorInfo? Selector(NetworkObjectTarget? target)
        => target is null ? null : new HardwareObjectSelectorInfo
        {
            DeviceName = target.DeviceName ?? string.Empty,
            ItemPath = target.Kind == NetworkOperationCatalog.DeviceTargetKind || target.ItemPath is null
                ? null
                : target.ItemPath.Select(Segment).ToList(),
        };

    /// <summary>The current parent of a device item: its path without the last segment, or the device itself.</summary>
    internal static HardwareObjectSelectorInfo ParentOf(NetworkObjectTarget target)
    {
        var path = target.ItemPath ?? throw new ArgumentException("A device item target needs an item path to derive its parent.");
        return new HardwareObjectSelectorInfo
        {
            DeviceName = target.DeviceName ?? string.Empty,
            ItemPath = path.Count <= 1 ? null : path.Take(path.Count - 1).Select(Segment).ToList(),
        };
    }

    private static DeviceItemPathSegmentInfo Segment(NetworkDeviceItemPathSegment segment) => new()
    {
        Index = segment.Index ?? throw new ArgumentException("A validated item-path segment is missing its index."),
        Name = segment.Name,
        PositionNumber = segment.PositionNumber ?? throw new ArgumentException("A validated item-path segment is missing its positionNumber."),
        TypeIdentifier = DeviceItemPathTypeIdentifier.Normalize(segment.TypeIdentifier),
    };
}
