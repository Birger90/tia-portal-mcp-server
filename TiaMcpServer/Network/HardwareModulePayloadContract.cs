using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Network;

/// <summary>
/// Declared result types of the hardware-module operations. Each operation decodes exactly one type
/// through the worker-payload reader (every member required, explicit nulls); these validators add
/// the null-element and request-consistency rules the reader cannot see.
/// </summary>
public static class HardwareModulePayloadContract
{
    public static bool Handles(string operation) => NetworkOperationCatalog.IsHardwareModuleOperation(operation);

    /// <summary>The CLR result type each operation declares (used by tests and schema documentation).</summary>
    public static Type ResultType(string operation) => operation switch
    {
        HardwareModuleMethods.ListPlugLocations => typeof(HardwarePlugLocationListInfo),
        HardwareModuleMethods.FindModuleTypes => typeof(HardwareModuleTypeListInfo),
        HardwareModuleMethods.FindContainerTypes => typeof(HardwareContainerTypeListInfo),
        HardwareModuleMethods.ListIoAddresses => typeof(HardwareIoAddressListInfo),
        HardwareModuleMethods.PlugDeviceItem or HardwareModuleMethods.MoveDeviceItem or HardwareModuleMethods.CopyDeviceItem
            or HardwareModuleMethods.DeleteDeviceItem or HardwareModuleMethods.ChangeDeviceItemType => typeof(HardwareDeviceItemMutationResultInfo),
        HardwareModuleMethods.SetHardwareAttributes => typeof(HardwareAttributeWriteResultInfo),
        HardwareModuleMethods.SetIoAddress => typeof(HardwareIoAddressWriteResultInfo),
        HardwareModuleMethods.GetCustomIdentity => typeof(HardwareCustomIdentityReadInfo),
        HardwareModuleMethods.SetCustomIdentity or HardwareModuleMethods.RemoveCustomIdentity => typeof(HardwareCustomIdentityWriteResultInfo),
        HardwareModuleMethods.ListPortConnections => typeof(HardwarePortListInfo),
        HardwareModuleMethods.ConnectPorts or HardwareModuleMethods.DisconnectPorts => typeof(HardwarePortConnectionResultInfo),
        HardwareModuleMethods.ListTransferAreas => typeof(HardwareTransferAreaListInfo),
        HardwareModuleMethods.CreateTransferArea or HardwareModuleMethods.UpdateTransferArea or HardwareModuleMethods.DeleteTransferArea
            => typeof(HardwareTransferAreaWriteResultInfo),
        HardwareModuleMethods.DeleteIoSystem => typeof(HardwareIoSystemDeleteResultInfo),
        HardwareModuleMethods.DisconnectFromIoSystem => typeof(HardwareIoConnectorDisconnectResultInfo),
        HardwareModuleMethods.ExportCax => typeof(HardwareCaxExportResultInfo),
        HardwareModuleMethods.ImportCax => typeof(HardwareCaxImportResultInfo),
        HardwareModuleMethods.ExportIoMap => typeof(IoMapExportResultInfo),
        var feature when HardwareFeatureMethods.IsFeature(feature) => typeof(HardwareFeatureResultInfo),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not a hardware-module operation."),
    };

    /// <exception cref="JsonException">The payload does not decode as the operation's declared type.</exception>
    public static JsonElement Decode(NetworkOperationRequest operation, string payload) => operation.Operation switch
    {
        HardwareModuleMethods.ListPlugLocations => CanonicalJson.NormalizeWorkerPayload<HardwarePlugLocationListInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            RejectNulls(value.PlugLocations, "plugLocations");
            RejectNulls(value.Messages, "messages");
        }).Element,
        HardwareModuleMethods.FindModuleTypes => CanonicalJson.NormalizeWorkerPayload<HardwareModuleTypeListInfo>(payload, value =>
        {
            if (value.PartialTypeIdentifier != operation.TypeIdentifier) throw new JsonException("The module-type query contradicts the request.");
            RejectNulls(value.ModuleTypes, "moduleTypes");
        }).Element,
        HardwareModuleMethods.FindContainerTypes => CanonicalJson.NormalizeWorkerPayload<HardwareContainerTypeListInfo>(payload, value =>
        {
            if (value.TypeIdentifier != operation.TypeIdentifier) throw new JsonException("The container-type query contradicts the request.");
            RejectNulls(value.ContainerTypes, "containerTypes");
        }).Element,
        HardwareModuleMethods.ListIoAddresses => CanonicalJson.NormalizeWorkerPayload<HardwareIoAddressListInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            ValidateAddresses(value.Addresses, "addresses");
            RejectNulls(value.Messages, "messages");
        }).Element,
        HardwareModuleMethods.PlugDeviceItem or HardwareModuleMethods.MoveDeviceItem or HardwareModuleMethods.CopyDeviceItem
            or HardwareModuleMethods.DeleteDeviceItem or HardwareModuleMethods.ChangeDeviceItemType
            => CanonicalJson.NormalizeWorkerPayload<HardwareDeviceItemMutationResultInfo>(payload, value => ValidateMutation(operation, value)).Element,
        HardwareModuleMethods.SetHardwareAttributes => CanonicalJson.NormalizeWorkerPayload<HardwareAttributeWriteResultInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            RejectNulls(value.Attributes, "attributes");
            RejectNulls(value.Messages, "messages");
            var requested = operation.Attributes?.Select(attribute => attribute.Name).ToArray() ?? Array.Empty<string?>();
            if (!value.Attributes.Select(attribute => attribute.Name).SequenceEqual(requested, StringComparer.Ordinal))
                throw new JsonException("The written attributes contradict the request.");
            if (value.Attributes.Any(attribute => attribute.Status is not ("applied" or "ignored" or "notApplied")))
                throw new JsonException("Unknown attribute write status.");
        }).Element,
        HardwareModuleMethods.SetIoAddress => CanonicalJson.NormalizeWorkerPayload<HardwareIoAddressWriteResultInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            ValidateAddresses(value.Before, "before");
            ValidateAddresses(value.After, "after");
            RejectNulls(value.Messages, "messages");
        }).Element,
        HardwareModuleMethods.GetCustomIdentity => CanonicalJson.NormalizeWorkerPayload<HardwareCustomIdentityReadInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            if (value.Key != operation.Key || string.IsNullOrWhiteSpace(value.ProviderType)) throw new JsonException("The custom identity read contradicts the request.");
            ValidateObservation(value.Observed, "observed");
        }).Element,
        HardwareModuleMethods.SetCustomIdentity or HardwareModuleMethods.RemoveCustomIdentity
            => CanonicalJson.NormalizeWorkerPayload<HardwareCustomIdentityWriteResultInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            var set = operation.Operation == HardwareModuleMethods.SetCustomIdentity;
            if (value.Operation != operation.Operation || value.Key != operation.Key || string.IsNullOrWhiteSpace(value.ProviderType)
                || value.RequestedValue != (set ? operation.Value : null))
                throw new JsonException("The custom identity write contradicts the request.");
            ValidateObservation(value.Before, "before");
            ValidateObservation(value.After, "after");
            // The worker reads the key back before it reports success.
            if (set ? value.After.State != CustomIdentityNames.States.Found || value.After.Value != operation.Value
                    : value.After.State == CustomIdentityNames.States.Found)
                throw new JsonException("The custom identity read-back contradicts the request.");
        }).Element,
        HardwareModuleMethods.ListPortConnections => CanonicalJson.NormalizeWorkerPayload<HardwarePortListInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            RejectNulls(value.Ports, "ports");
            RejectNulls(value.Messages, "messages");
            foreach (var port in value.Ports)
            {
                ValidateIdentity(port.Port, "ports[].port", null);
                if (port.InterfaceOwner is { } owner) ValidateIdentity(owner, "ports[].interfaceOwner", null);
                if (!NetworkPortNames.ReadStates.All.Contains(port.ConnectedPortsState, StringComparer.Ordinal))
                    throw new JsonException("'ports[].connectedPortsState' is not a known state.");
                RejectNulls(port.ConnectedPorts, "ports[].connectedPorts");
                RejectNulls(port.Messages, "ports[].messages");
                if (port.ConnectedPortsState == NetworkPortNames.ReadStates.Unreadable && port.ConnectedPorts.Count > 0)
                    throw new JsonException("An unreadable port lists partners.");
                foreach (var partner in port.ConnectedPorts)
                {
                    if (partner.Port is { } partnerPort) ValidateIdentity(partnerPort, "ports[].connectedPorts[].port", null);
                    if (string.IsNullOrWhiteSpace(partner.RuntimeType)) throw new JsonException("'ports[].connectedPorts[].runtimeType' must be nonblank.");
                }
            }
        }).Element,
        HardwareModuleMethods.ConnectPorts or HardwareModuleMethods.DisconnectPorts
            => CanonicalJson.NormalizeWorkerPayload<HardwarePortConnectionResultInfo>(payload, value =>
        {
            if (value.Operation != operation.Operation) throw new JsonException("The port connection result contradicts the request.");
            ValidateIdentity(value.Port, "port", operation.Target);
            ValidateIdentity(value.Partner, "partner", operation.Partner);
            RejectNulls(value.Messages, "messages");
            // The worker reads the link back before it reports success; null means ConnectedPorts was unreadable (trusted).
            var connect = operation.Operation == HardwareModuleMethods.ConnectPorts;
            if (value.ConnectedAfter is { } after && after != connect)
                throw new JsonException("The port connection read-back contradicts the request.");
        }).Element,
        HardwareModuleMethods.ListTransferAreas => CanonicalJson.NormalizeWorkerPayload<HardwareTransferAreaListInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            RejectNulls(value.Messages, "messages");
            ValidateAreas(value.TransferAreas, NetworkSystemNames.TransferAreaCollections.TransferAreas, "transferAreas");
            ValidateAreas(value.MulticastableTransferAreas, NetworkSystemNames.TransferAreaCollections.Multicastable, "multicastableTransferAreas");
        }).Element,
        HardwareModuleMethods.CreateTransferArea or HardwareModuleMethods.UpdateTransferArea or HardwareModuleMethods.DeleteTransferArea
            => CanonicalJson.NormalizeWorkerPayload<HardwareTransferAreaWriteResultInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            if (value.Operation != operation.Operation || value.Collection != operation.TransferArea?.Collection)
                throw new JsonException("The transfer area write contradicts the request.");
            RejectNulls(value.Messages, "messages");
            RejectNulls(value.Attributes, "attributes");
            if (value.Before is { } before) ValidateArea(before, value.Collection, "before");
            if (value.After is { } after) ValidateArea(after, value.Collection, "after");
            if (value.Partner is { } partner) ValidateIdentity(partner, "partner", operation.Partner);
            switch (operation.Operation)
            {
                case HardwareModuleMethods.CreateTransferArea when value.After is null || value.Before is not null:
                case HardwareModuleMethods.UpdateTransferArea when value.After is null || value.Before is null:
                case HardwareModuleMethods.DeleteTransferArea when value.After is not null || value.Before is null || value.CountAfter != value.CountBefore - 1:
                    throw new JsonException("The transfer area read-back contradicts the operation.");
            }

            if (operation.Operation == HardwareModuleMethods.UpdateTransferArea)
            {
                var requested = operation.Attributes?.Select(attribute => attribute.Name).ToArray() ?? Array.Empty<string?>();
                if (!value.Attributes.Select(attribute => attribute.Name).SequenceEqual(requested, StringComparer.Ordinal)
                    || value.Attributes.Any(attribute => attribute.Status != "applied"))
                    throw new JsonException("The written transfer area attributes contradict the request.");
            }
            else if (value.Attributes.Count > 0) throw new JsonException("Only update_transfer_area reports attributes.");
        }).Element,
        HardwareModuleMethods.DeleteIoSystem => CanonicalJson.NormalizeWorkerPayload<HardwareIoSystemDeleteResultInfo>(payload, value =>
        {
            if (!string.Equals(value.SubnetId, operation.Target?.SubnetId, StringComparison.Ordinal) || value.Number != operation.Target?.Number)
                throw new JsonException("The deleted IO system contradicts the request.");
            if (value.ConnectedIoDeviceCount is < 0) throw new JsonException("'connectedIoDeviceCount' must not be negative.");
            RejectNulls(value.Messages, "messages");
        }).Element,
        HardwareModuleMethods.DisconnectFromIoSystem => CanonicalJson.NormalizeWorkerPayload<HardwareIoConnectorDisconnectResultInfo>(payload, value =>
        {
            ValidateIdentity(value.Target, "target", operation.Target);
            if (!value.DisconnectedAfter || value.ConnectorIndex < 0) throw new JsonException("The IO connector read-back contradicts the request.");
            RejectNulls(value.Messages, "messages");
        }).Element,
        HardwareModuleMethods.ExportCax => CanonicalJson.NormalizeWorkerPayload<HardwareCaxExportResultInfo>(payload, value =>
        {
            var scope = operation.DeviceName is null ? CaxNames.Scopes.Project : CaxNames.Scopes.Device;
            if (value.Scope != scope || (operation.DeviceName is null) != (value.DeviceName is null)
                || value.DeviceName is { } name && !string.Equals(name, operation.DeviceName, StringComparison.OrdinalIgnoreCase))
                throw new JsonException("The CAx export scope contradicts the request.");
            ValidateCaxOutcome(value.Transfer, value.Succeeded, value.Log, operation.LogFilePath, value.Overload);
            // The worker re-reads and hashes the file before it reports success.
            if (value.File is null || value.File.Path != operation.FilePath || !value.File.Exists || string.IsNullOrWhiteSpace(value.File.Sha256))
                throw new JsonException("The exported CAx file contradicts the request.");
            RejectNulls(value.Messages, "messages");
        }).Element,
        HardwareModuleMethods.ImportCax => CanonicalJson.NormalizeWorkerPayload<HardwareCaxImportResultInfo>(payload, value =>
        {
            if (value.FilePath != operation.FilePath || value.ImportOption != operation.ImportOption)
                throw new JsonException("The CAx import contradicts the request.");
            ValidateCaxOutcome(value.Transfer, value.Succeeded, value.Log, operation.LogFilePath, value.Overload);
            foreach (var (list, member) in new[] { (value.DevicesBefore, "devicesBefore"), (value.DevicesAfter, "devicesAfter"), (value.AddedDevices, "addedDevices"),
                (value.RemovedDevices, "removedDevices"), (value.Messages, "messages") })
                RejectNulls(list, member);
        }).Element,
        var feature when HardwareFeatureMethods.IsFeature(feature) => CanonicalJson.NormalizeWorkerPayload<HardwareFeatureResultInfo>(payload, value =>
        {
            if (value.Operation != operation.Operation) throw new JsonException("The hardware feature result contradicts the request.");
            ValidateIdentity(value.Target, "target", operation.Target);
            RejectNulls(value.Calls, "calls");
            RejectNulls(value.Values, "values");
            RejectNulls(value.Items, "items");
            RejectNulls(value.Messages, "messages");
            if (value.ReadBack is not ("matched" or "notAvailable" or "unreadable")) throw new JsonException("'readBack' is not a known state.");
            if (feature == HardwareFeatureMethods.ExportTelecontrolDataPoints && (value.FilePath != operation.FilePath || value.FileSha256 is not { Length: 64 }))
                throw new JsonException("The data point export result does not name the written file.");
        }).Element,
        HardwareModuleMethods.ExportIoMap => CanonicalJson.NormalizeWorkerPayload<IoMapExportResultInfo>(payload, value =>
        {
            if (value.FilePath != operation.FilePath) throw new JsonException("The exported file contradicts the request.");
            RejectNulls(value.Messages, "messages");
        }).Element,
        _ => throw new JsonException($"No declared result contract for hardware-module operation '{operation.Operation}'."),
    };

    private static void ValidateMutation(NetworkOperationRequest operation, HardwareDeviceItemMutationResultInfo value)
    {
        if (value.Operation != operation.Operation) throw new JsonException("The mutation result contradicts the request.");
        RejectNulls(value.Messages, "messages");
        if (value.Container is { } container) ValidateIdentity(container, "container", null);
        if (value.Source is { } source) ValidateIdentity(source, "source", operation.Target);
        if (value.Result is { } result) ValidatePath(result.ItemPath, "result.itemPath");
        var needsSource = operation.Operation != HardwareModuleMethods.PlugDeviceItem;
        if (needsSource != (value.Source is not null)) throw new JsonException("The mutation source contradicts the operation.");
        if (operation.Operation == HardwareModuleMethods.DeleteDeviceItem && value.Result is not null)
            throw new JsonException("A deleted item has no result.");
        var places = operation.Operation is HardwareModuleMethods.PlugDeviceItem or HardwareModuleMethods.MoveDeviceItem or HardwareModuleMethods.CopyDeviceItem;
        if (places != (value.PlacedUnder is not null)) throw new JsonException("'placedUnder' contradicts the operation.");
        if (value.PlacedUnder is { } placedUnder)
        {
            ValidateIdentity(placedUnder, "placedUnder", null);
            // The item lands in the container, or (a rack-level container) directly under the container's device.
            var owner = value.Container ?? throw new JsonException("'placedUnder' requires 'container'.");
            var inContainer = SameIdentity(placedUnder, owner);
            var underDevice = owner.ItemPath.Count == 1 && placedUnder.ItemPath.Count == 0
                && string.Equals(placedUnder.DeviceName, owner.DeviceName, StringComparison.Ordinal);
            if (!inContainer && !underDevice) throw new JsonException("'placedUnder' is neither the container nor its device.");
            if (value.Result is { ItemPath.Count: > 0 } placed && placed.ItemPath.Count != placedUnder.ItemPath.Count + 1)
                throw new JsonException("'result.itemPath' contradicts 'placedUnder'.");
        }
    }

    private static bool SameIdentity(HardwareObjectIdentityInfo left, HardwareObjectIdentityInfo right)
        => string.Equals(left.DeviceName, right.DeviceName, StringComparison.Ordinal) && left.ItemPath.Count == right.ItemPath.Count
            && left.ItemPath.Zip(right.ItemPath).All(pair => pair.First.Index == pair.Second.Index && pair.First.Name == pair.Second.Name
                && pair.First.PositionNumber == pair.Second.PositionNumber && pair.First.TypeIdentifier == pair.Second.TypeIdentifier);

    private static void ValidateIdentity(HardwareObjectIdentityInfo identity, string member, NetworkObjectTarget? requested)
    {
        if (identity is null) throw new JsonException($"'{member}' is required.");
        if (string.IsNullOrWhiteSpace(identity.DeviceName)) throw new JsonException($"'{member}.deviceName' must be nonblank.");
        ValidatePath(identity.ItemPath, $"{member}.itemPath");
        if (requested is null) return;
        if (!string.Equals(identity.DeviceName, requested.DeviceName, StringComparison.OrdinalIgnoreCase))
            throw new JsonException($"'{member}.deviceName' contradicts the request.");
        var requestedPath = requested.Kind == NetworkOperationCatalog.DeviceTargetKind ? null : requested.ItemPath;
        if ((requestedPath?.Count ?? 0) != identity.ItemPath.Count) throw new JsonException($"'{member}.itemPath' contradicts the request.");
        for (var i = 0; i < identity.ItemPath.Count; i++)
        {
            var expected = requestedPath![i];
            var actual = identity.ItemPath[i];
            if (expected.Index != actual.Index || expected.Name != actual.Name || expected.PositionNumber != actual.PositionNumber
                || expected.TypeIdentifier != actual.TypeIdentifier)
                throw new JsonException($"'{member}.itemPath[{i}]' contradicts the request.");
        }
    }

    private static void ValidatePath(List<DeviceItemPathSegmentInfo> path, string member)
    {
        if (path is null) throw new JsonException($"'{member}' is required.");
        foreach (var segment in path)
        {
            if (segment is null || segment.Index < 0 || segment.PositionNumber < 0 || string.IsNullOrWhiteSpace(segment.Name))
                throw new JsonException($"'{member}[]' requires a nonnegative index and position and a nonblank name.");
        }
    }

    private static void ValidateAddresses(List<HardwareIoAddressInfo> addresses, string member)
    {
        RejectNulls(addresses, member);
        if (addresses.Select(address => address.Index).Distinct().Count() != addresses.Count)
            throw new JsonException($"'{member}[].index' must be unique.");
        if (addresses.Any(address => address.Index < 0 || address.StartAddress < 0 || address.Length < 0))
            throw new JsonException($"'{member}[]' values must not be negative.");
        foreach (var address in addresses)
        {
            RejectNulls(address.SubItemPath, $"{member}[].subItemPath");
            if (address.SubItemPath.Any(segment => segment.Index < 0 || segment.PositionNumber < 0
                || segment.Name is not null && string.IsNullOrWhiteSpace(segment.Name)
                || segment.TypeIdentifier is not null && string.IsNullOrWhiteSpace(segment.TypeIdentifier)))
                throw new JsonException($"'{member}[].subItemPath[]' requires a nonnegative index and position and nonblank optional name and type identifier.");
        }
    }

    private static void ValidateObservation(CustomIdentityObservationInfo observation, string member)
    {
        if (observation is null || !CustomIdentityNames.States.All.Contains(observation.State, StringComparer.Ordinal)
            || (observation.State == CustomIdentityNames.States.Found) != (observation.Value is not null))
            throw new JsonException($"'{member}' is not a valid custom identity observation.");
    }

    /// <summary>Exactly one of a TransferResult (no log file) or a Boolean (log file); neither may report failure on success.</summary>
    private static void ValidateCaxOutcome(CaxTransferResultInfo? transfer, bool? succeeded, CaxFileInfo? log, string? logFilePath, string overload)
    {
        if (string.IsNullOrWhiteSpace(overload)) throw new JsonException("'overload' must be nonblank.");
        if (logFilePath is null ? transfer is null || succeeded is not null || log is not null
                : transfer is not null || succeeded is null || log is null || log.Path != logFilePath)
            throw new JsonException("The CAx outcome contradicts the requested overload.");
        if (succeeded == false || transfer is not null && CaxRun.Failed(transfer))
            throw new JsonException("A failed CAx transfer was reported as success.");
        if (transfer is not null && (transfer.Messages is null || transfer.MessagesOmitted < 0)) throw new JsonException("'transfer.messages' is invalid.");
    }

    private static void ValidateAreas(List<HardwareTransferAreaInfo>? areas, string collection, string member)
    {
        if (areas is null) return;
        RejectNulls(areas, member);
        for (var index = 0; index < areas.Count; index++)
        {
            ValidateArea(areas[index], collection, $"{member}[{index}]");
            if (areas[index].Index != index) throw new JsonException($"'{member}[{index}].index' must be its position.");
        }
    }

    private static void ValidateArea(HardwareTransferAreaInfo area, string collection, string member)
    {
        if (area.Collection != collection || area.Index < 0 || string.IsNullOrWhiteSpace(area.RuntimeType))
            throw new JsonException($"'{member}' is not a valid transfer area of '{collection}'.");
        RejectNulls(area.Properties, $"{member}.properties");
        RejectNulls(area.Attributes, $"{member}.attributes");
        if (area.Properties.Any(property => string.IsNullOrWhiteSpace(property.Name) || property.Value is null
            || !RuntimeValueStates.All.Contains(property.Value.State, StringComparer.Ordinal)))
            throw new JsonException($"'{member}.properties[]' requires a name and a valid value.");
    }

    private static void RejectNulls<T>(List<T> values, string member)
    {
        if (values is null || values.Any(value => value is null)) throw new JsonException($"'{member}' must not contain null.");
    }
}
