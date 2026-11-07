using System.Text.Json;
using TiaMcpServer.Contracts.Files;
using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Network;

/// <summary>Structural validation of the hardware-module operations; pure and Siemens-free.</summary>
public static partial class NetworkOperationCatalog
{
    public const int MaxHardwareAttributes = 100;

    /// <summary>The <c>target.kind</c> that names a device itself (only for hardware-module operations).</summary>
    public const string DeviceTargetKind = "device";

    public static IReadOnlyList<string> CsvDelimiters { get; } = new[] { ",", ";", "\t" };

    /// <summary>Holder class: initialized on first use, independent of the partial-file field order.</summary>
    private static class HardwareSpecHolder
    {
        internal static readonly IReadOnlyDictionary<string, NetworkOperationSpec> Specs = new[]
        {
            new NetworkOperationSpec(HardwareModuleMethods.ListPlugLocations, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.FindModuleTypes, NetworkOperationCategory.Read, new[] { "typeIdentifier" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.FindContainerTypes, NetworkOperationCategory.Read, new[] { "typeIdentifier" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.ListIoAddresses, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.PlugDeviceItem, NetworkOperationCategory.Write, new[] { "target", "typeIdentifier", "positionNumber" }, new[] { "deviceItemName" }),
            new NetworkOperationSpec(HardwareModuleMethods.MoveDeviceItem, NetworkOperationCategory.Write, new[] { "target", "positionNumber" }, new[] { "destination" }),
            new NetworkOperationSpec(HardwareModuleMethods.CopyDeviceItem, NetworkOperationCategory.Write, new[] { "target", "positionNumber" }, new[] { "destination" }),
            new NetworkOperationSpec(HardwareModuleMethods.DeleteDeviceItem, NetworkOperationCategory.Write, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.ChangeDeviceItemType, NetworkOperationCategory.Write, new[] { "target", "typeIdentifier" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.SetHardwareAttributes, NetworkOperationCategory.Write, new[] { "target", "attributes" }, new[] { "attributeErrorPolicy", "attributeWriteMode" }),
            new NetworkOperationSpec(HardwareModuleMethods.SetIoAddress, NetworkOperationCategory.Write, new[] { "target", "address", "addressChanges" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.GetCustomIdentity, NetworkOperationCategory.Read, new[] { "target", "key" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.SetCustomIdentity, NetworkOperationCategory.Write, new[] { "target", "key", "value" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.RemoveCustomIdentity, NetworkOperationCategory.Write, new[] { "target", "key" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.ListPortConnections, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.ConnectPorts, NetworkOperationCategory.Write, new[] { "target", "partner" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.DisconnectPorts, NetworkOperationCategory.Write, new[] { "target", "partner" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.ListTransferAreas, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.CreateTransferArea, NetworkOperationCategory.Write, new[] { "target", "transferArea" }, new[] { "partner" }),
            new NetworkOperationSpec(HardwareModuleMethods.UpdateTransferArea, NetworkOperationCategory.Write, new[] { "target", "transferArea", "attributes" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.DeleteTransferArea, NetworkOperationCategory.Write, new[] { "target", "transferArea" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.DeleteIoSystem, NetworkOperationCategory.Write, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.DisconnectFromIoSystem, NetworkOperationCategory.Write, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareModuleMethods.ExportCax, NetworkOperationCategory.Write, new[] { "filePath" }, new[] { "deviceName", "logFilePath", "overwrite" }),
            new NetworkOperationSpec(HardwareModuleMethods.ImportCax, NetworkOperationCategory.Write, new[] { "filePath", "importOption" }, new[] { "logFilePath", "overwrite" }),
            // Package G hardware features (target: the device item that offers the feature).
            new NetworkOperationSpec(HardwareFeatureMethods.GetNodes, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.CreateAndConnectSubnet, NetworkOperationCategory.Write, new[] { "target", "newSubnetName" }, new[] { "nodeName" }),
            new NetworkOperationSpec(HardwareFeatureMethods.DisconnectFromSubnet, NetworkOperationCategory.Write, new[] { "target" }, new[] { "nodeName" }),
            new NetworkOperationSpec(HardwareFeatureMethods.GetIoController, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.GetImPartnerPort, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.ConnectImPort, NetworkOperationCategory.Write, new[] { "target", "partner" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.DisconnectImPort, NetworkOperationCategory.Write, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.GetPcInterfaceAssignment, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.AssignPcInterface, NetworkOperationCategory.Write, new[] { "target", "assignmentMode" }, new[] { "partner" }),
            new NetworkOperationSpec(HardwareFeatureMethods.GetModuleDescriptionUpdate, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.UpdateModuleDescription, NetworkOperationCategory.Write, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.ExportTelecontrolDataPoints, NetworkOperationCategory.Write, new[] { "target", "filePath" }, new[] { "overwrite" }),
            new NetworkOperationSpec(HardwareFeatureMethods.ImportTelecontrolDataPoints, NetworkOperationCategory.Write, new[] { "target", "filePath" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.ListCommunicationConnections, NetworkOperationCategory.Read, new[] { "target" }, None),
            new NetworkOperationSpec(HardwareFeatureMethods.CreateCommunicationConnection, NetworkOperationCategory.Write, new[] { "target", "partner", "connectionType" }, new[] { "nodeName", "partnerNodeName" }),
            new NetworkOperationSpec(HardwareFeatureMethods.DeleteCommunicationConnection, NetworkOperationCategory.Write, new[] { "target", "connectionIndex" }, new[] { "connectionName" }),
            new NetworkOperationSpec(HardwareModuleMethods.ExportIoMap, NetworkOperationCategory.Write, new[] { "filePath" }, new[] { "deviceName", "plcName", "includeTagMatches", "overwrite", "csvDelimiter", "rawCells" }),
        }.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, NetworkOperationSpec> HardwareModuleSpecs => HardwareSpecHolder.Specs;

    /// <summary>Operations whose target may also be a device (kind 'device').</summary>
    private static readonly IReadOnlySet<string> DeviceTargetOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        HardwareModuleMethods.ListPlugLocations, HardwareModuleMethods.PlugDeviceItem, HardwareModuleMethods.SetHardwareAttributes,
        HardwareModuleMethods.GetCustomIdentity, HardwareModuleMethods.SetCustomIdentity, HardwareModuleMethods.RemoveCustomIdentity,
        HardwareModuleMethods.ListPortConnections,
    };

    /// <summary>True for every hardware-module operation of network_read or network_write, export_io_map included.</summary>
    public static bool IsHardwareModuleOperation(string? operation)
        => operation is not null && HardwareModuleSpecs.ContainsKey(operation);

    private static void ValidateHardwareModule(NetworkOperationRequest operation, List<string> errors)
    {
        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'):";
        if (operation.Target is { } target)
        {
            if (operation.Operation == HardwareModuleMethods.DeleteIoSystem)
            {
                if (!string.Equals(target.Kind, NetworkObjectKinds.IoSystem, StringComparison.Ordinal))
                    errors.Add($"{prefix} 'target.kind' must be '{NetworkObjectKinds.IoSystem}' for {operation.Operation} (received '{target.Kind}'); "
                        + "name the IO system as read_hardware_config reports it (subnetId, number).");
                else ValidateSelectorShape(operation.Operation, operation.OperationId, target, errors);
            }
            else ValidateHardwareTarget(operation, target, "target", DeviceTargetOperations.Contains(operation.Operation), prefix, errors);
        }
        if (operation.Destination is { } destination)
            ValidateHardwareTarget(operation, destination, "destination", allowDevice: true, prefix, errors);
        if (operation.Partner is { } partner)
            ValidateHardwareTarget(operation, partner, "partner", allowDevice: false, prefix, errors);

        if (operation.AssignmentMode is { } assignmentMode && !HardwareFeatureNames.AssignmentModes.Contains(assignmentMode, StringComparer.Ordinal))
            errors.Add($"{prefix} 'assignmentMode' must be one of {string.Join(", ", HardwareFeatureNames.AssignmentModes)}.");
        if (operation.ConnectionType is { } connectionType && !HardwareFeatureNames.ConnectionTypes.Contains(connectionType, StringComparer.Ordinal))
            errors.Add($"{prefix} 'connectionType' must be one of {string.Join(", ", HardwareFeatureNames.ConnectionTypes)}.");
        if (operation.ConnectionIndex is < 0)
            errors.Add($"{prefix} 'connectionIndex' must not be negative.");
        if (operation.Operation is HardwareFeatureMethods.ExportTelecontrolDataPoints or HardwareFeatureMethods.ImportTelecontrolDataPoints
            && TiaMcpServer.Contracts.Plc.PlcFileTargets.PathShapeError("filePath", operation.FilePath) is { } fileError)
            errors.Add($"{prefix} {fileError}");

        if (operation.PositionNumber is < 0)
            errors.Add($"{prefix} 'positionNumber' must not be negative.");

        if (operation.TypeIdentifier is not null && !CatalogTypeIdentifier.IsCreatable(operation.TypeIdentifier))
            errors.Add($"{prefix} {CatalogTypeIdentifier.BuildValidationMessage(operation.TypeIdentifier)}");

        if (operation.DeviceItemName is not null && string.IsNullOrWhiteSpace(operation.DeviceItemName))
            errors.Add($"{prefix} 'deviceItemName' must not be blank when supplied; omit it to let TIA Portal choose the name.");

        if (operation.Attributes is { } attributes) ValidateAttributes(attributes, prefix, errors);

        if (operation.AttributeErrorPolicy is { } policy && !HardwareModuleMethods.AttributeErrorPolicies.All.Contains(policy, StringComparer.Ordinal))
            errors.Add($"{prefix} 'attributeErrorPolicy' value '{policy}' is not supported. Valid values: {string.Join(", ", HardwareModuleMethods.AttributeErrorPolicies.All)}.");

        if (operation.AttributeWriteMode is { } mode && !HardwareModuleMethods.AttributeWriteModes.All.Contains(mode, StringComparer.Ordinal))
            errors.Add($"{prefix} 'attributeWriteMode' value '{mode}' is not supported. Valid values: {string.Join(", ", HardwareModuleMethods.AttributeWriteModes.All)}.");

        if (operation.Address is { } address)
        {
            if (string.IsNullOrWhiteSpace(address.IoType) || !HardwareModuleMethods.AddressIoTypes.Contains(address.IoType, StringComparer.Ordinal))
                errors.Add($"{prefix} 'address.ioType' is required. Valid values: {string.Join(", ", HardwareModuleMethods.AddressIoTypes)}.");
            if (address.StartAddress is null) errors.Add($"{prefix} 'address.startAddress' is required.");
            else if (address.StartAddress < 0) errors.Add($"{prefix} 'address.startAddress' must not be negative.");
            if (address.Length is < 1) errors.Add($"{prefix} 'address.length' must be 1 or greater when supplied.");
        }

        if (operation.AddressChanges is { } changes)
        {
            if (changes.StartAddress is null && changes.ProcessImage is null && changes.IsochronousMode is null && changes.InterruptObNumber is null)
                errors.Add($"{prefix} 'addressChanges' must request at least one change.");
            if (changes.StartAddress is < 0) errors.Add($"{prefix} 'addressChanges.startAddress' must not be negative.");
            if (changes.ProcessImage is < 0) errors.Add($"{prefix} 'addressChanges.processImage' must not be negative.");
            if (changes.InterruptObNumber is < 0) errors.Add($"{prefix} 'addressChanges.interruptObNumber' must not be negative.");
        }

        if (operation.Operation == HardwareModuleMethods.ExportIoMap) ValidateExport(operation, prefix, errors);
        if (operation.TransferArea is { } area) ValidateTransferArea(operation, area, prefix, errors);
        if (operation.Operation is HardwareModuleMethods.ExportCax or HardwareModuleMethods.ImportCax) ValidateCax(operation, prefix, errors);
    }

    private static void ValidateCax(NetworkOperationRequest operation, string prefix, List<string> errors)
    {
        if (operation.FilePath is { } path && ValidateLocalPath("filePath", path, prefix, errors)
            && !path.EndsWith(CaxNames.Extension, StringComparison.OrdinalIgnoreCase))
            errors.Add($"{prefix} 'filePath' must end in {CaxNames.Extension} (AutomationML).");
        if (operation.LogFilePath is { } log && ValidateLocalPath("logFilePath", log, prefix, errors)
            && operation.FilePath is { } file && string.Equals(Path.GetFullPath(log), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            errors.Add($"{prefix} 'logFilePath' must differ from 'filePath'.");
        if (operation.ImportOption is { } option && !CaxNames.ImportOptions.Contains(option, StringComparer.Ordinal))
            errors.Add($"{prefix} 'importOption' value '{option}' is not supported. Valid values: {string.Join(", ", CaxNames.ImportOptions)}.");
        if (operation.DeviceName is not null && string.IsNullOrWhiteSpace(operation.DeviceName))
            errors.Add($"{prefix} 'deviceName' must not be blank when supplied; omit it to export the whole project.");
    }

    /// <summary>The shared lexical rules of a caller file path (as export_io_map applies them); true when the path passed.</summary>
    private static bool ValidateLocalPath(string member, string path, string prefix, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(path) && IsDevicePath(path))
        {
            errors.Add($"{prefix} '{member}': {LocalFilePaths.DevicePathReason} (received '{path}').");
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) || !IsAbsoluteFilePath(path))
        {
            errors.Add($"{prefix} '{member}' must be an absolute file path (received '{path}').");
            return false;
        }

        var shape = TiaMcpServer.Contracts.Plc.PlcFileTargets.PathShapeError(member, path);
        var refusal = LocalFilePaths.CheckCharacters(path, $"'{member}'") ?? LocalFilePaths.CheckSegments(path, $"'{member}'");
        if (shape is not null) errors.Add($"{prefix} {shape}");
        if (refusal is not null) errors.Add($"{prefix} {refusal}");
        return shape is null && refusal is null;
    }

    private static void ValidateTransferArea(NetworkOperationRequest operation, NetworkTransferAreaRequest area, string prefix, List<string> errors)
    {
        var multicastable = area.Collection == NetworkSystemNames.TransferAreaCollections.Multicastable;
        if (area.Collection is null || !NetworkSystemNames.TransferAreaCollections.All.Contains(area.Collection, StringComparer.Ordinal))
        {
            errors.Add($"{prefix} 'transferArea.collection' is required. Valid values: {string.Join(", ", NetworkSystemNames.TransferAreaCollections.All)}.");
            return;
        }

        if (area.Name is not null && string.IsNullOrWhiteSpace(area.Name)) errors.Add($"{prefix} 'transferArea.name' must be nonblank when supplied.");
        if (operation.Operation == HardwareModuleMethods.CreateTransferArea)
        {
            if (area.Index is not null) errors.Add($"{prefix} 'transferArea.index' is not valid for {operation.Operation}.");
            if (area.Type is null || !NetworkSystemNames.TransferAreaTypes.Contains(area.Type, StringComparer.Ordinal))
                errors.Add($"{prefix} 'transferArea.type' is required. Valid values: {string.Join(", ", NetworkSystemNames.TransferAreaTypes)}.");
            if (area.PositionNumber is < 0) errors.Add($"{prefix} 'transferArea.positionNumber' must not be negative.");
            if (area.DataLength is < 1) errors.Add($"{prefix} 'transferArea.dataLength' must be 1 or greater.");
            if (area.TemplateIndex is < 0) errors.Add($"{prefix} 'transferArea.templateIndex' must not be negative.");
            if (multicastable)
            {
                if (operation.Partner is null) errors.Add($"{prefix} 'partner' (the device item providing the partner NetworkInterface) is required for a multicastable transfer area.");
                if (area.PositionNumber is not null) errors.Add($"{prefix} 'transferArea.positionNumber' is valid only for collection 'transferAreas'.");
                if (area.TemplateIndex is not null && (area.Name is not null || area.DataLength is not null))
                    errors.Add($"{prefix} 'transferArea.templateIndex' cannot be combined with name or dataLength (Create(MulticastableTransferArea, TransferAreaType) takes neither).");
                if (area.DataLength is not null && area.Name is null)
                    errors.Add($"{prefix} 'transferArea.dataLength' requires 'transferArea.name' (Create(NetworkInterface, TransferAreaType, String, Int32)).");
            }
            else
            {
                if (operation.Partner is not null) errors.Add($"{prefix} 'partner' is valid only for collection 'multicastable'.");
                if (area.Name is null) errors.Add($"{prefix} 'transferArea.name' is required for collection 'transferAreas' (TransferAreaComposition.Create(String, ...)).");
                if (area.DataLength is not null || area.TemplateIndex is not null)
                    errors.Add($"{prefix} 'transferArea.dataLength' and 'transferArea.templateIndex' are valid only for collection 'multicastable'.");
            }

            return;
        }

        if (area.Index is null) errors.Add($"{prefix} 'transferArea.index' is required for {operation.Operation}.");
        else if (area.Index < 0) errors.Add($"{prefix} 'transferArea.index' must not be negative.");
        if (area.Name is null) errors.Add($"{prefix} 'transferArea.name' (the area's current name, as evidence) is required for {operation.Operation}.");
        if (area.Type is not null || area.PositionNumber is not null || area.DataLength is not null || area.TemplateIndex is not null)
            errors.Add($"{prefix} 'transferArea' takes only collection, index and name for {operation.Operation}.");
    }

    private static void ValidateHardwareTarget(NetworkOperationRequest operation, NetworkObjectTarget target, string member,
        bool allowDevice, string prefix, List<string> errors)
    {
        if (string.Equals(target.Kind, DeviceTargetKind, StringComparison.Ordinal))
        {
            if (!allowDevice)
            {
                errors.Add($"{prefix} '{member}.kind' must be '{NetworkObjectKinds.DeviceItem}' for {operation.Operation}; it acts on a device item.");
                return;
            }

            if (string.IsNullOrWhiteSpace(target.DeviceName))
                errors.Add($"{prefix} '{member}.deviceName' is required for kind '{DeviceTargetKind}'.");
            foreach (var (name, isSet) in AllSelectorFields)
            {
                if (name is "kind" or "deviceName" || !isSet(target)) continue;
                errors.Add($"{prefix} '{member}.{name}' is not applicable for kind '{DeviceTargetKind}'.");
            }

            return;
        }

        if (!string.Equals(target.Kind, NetworkObjectKinds.DeviceItem, StringComparison.Ordinal))
        {
            var valid = allowDevice ? $"'{DeviceTargetKind}' or '{NetworkObjectKinds.DeviceItem}'" : $"'{NetworkObjectKinds.DeviceItem}'";
            errors.Add($"{prefix} '{member}.kind' must be {valid} for {operation.Operation} (received '{target.Kind}').");
            return;
        }

        // Selector errors name 'target.'; a destination is reported under its own member name.
        var selectorErrors = new List<string>();
        ValidateSelectorShape(operation.Operation, operation.OperationId, target, selectorErrors);
        errors.AddRange(member == "target" ? selectorErrors : selectorErrors.Select(error => error.Replace("'target.", $"'{member}.")));
    }

    private static void ValidateAttributes(IReadOnlyList<HardwareAttributeAssignment> attributes, string prefix, List<string> errors)
    {
        if (attributes.Count == 0)
        {
            errors.Add($"{prefix} 'attributes' must not be empty.");
            return;
        }

        if (attributes.Count > MaxHardwareAttributes)
        {
            errors.Add($"{prefix} 'attributes' must not exceed {MaxHardwareAttributes} entries (received {attributes.Count}).");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            var member = $"{prefix} 'attributes[{index}]";
            if (attribute is null)
            {
                errors.Add($"{member}' must not be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(attribute.Name)) errors.Add($"{member}.name' is required and must be nonblank.");
            else if (!seen.Add(attribute.Name)) errors.Add($"{member}.name' duplicates attribute '{attribute.Name}'.");

            if (!TryConvertAttributeValue(attribute, out _, out var error)) errors.Add($"{member}': {error}");
        }
    }

    /// <summary>Converts a validated wire assignment into the typed worker value; the error names what is wrong.</summary>
    internal static bool TryConvertAttributeValue(HardwareAttributeAssignment attribute, out HardwareAttributeValueInfo? value, out string? error)
    {
        value = null;
        error = null;
        var kind = attribute.Kind;
        if (string.IsNullOrWhiteSpace(kind) || !HardwareModuleMethods.AttributeValueKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            error = $"'kind' is required. Valid values: {string.Join(", ", HardwareModuleMethods.AttributeValueKinds.All)}.";
            return false;
        }

        if (attribute.Value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            error = $"'value' is required for kind '{kind}'; writing null is not supported.";
            return false;
        }

        var converted = new HardwareAttributeValueInfo { Name = attribute.Name ?? string.Empty, Kind = kind };
        switch (kind)
        {
            case HardwareModuleMethods.AttributeValueKinds.String when element.ValueKind == JsonValueKind.String:
                converted.StringValue = element.GetString();
                break;
            case HardwareModuleMethods.AttributeValueKinds.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                converted.BooleanValue = element.GetBoolean();
                break;
            case HardwareModuleMethods.AttributeValueKinds.Integer when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var integer):
                converted.IntegerValue = integer;
                break;
            case HardwareModuleMethods.AttributeValueKinds.Number when element.ValueKind == JsonValueKind.Number
                && element.TryGetDouble(out var number) && !double.IsNaN(number) && !double.IsInfinity(number):
                converted.NumberValue = number;
                break;
            case HardwareModuleMethods.AttributeValueKinds.Enum when element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()):
                converted.EnumSymbol = element.GetString();
                break;
            default:
                error = kind switch
                {
                    HardwareModuleMethods.AttributeValueKinds.String => "'value' must be a JSON string for kind 'string'.",
                    HardwareModuleMethods.AttributeValueKinds.Boolean => "'value' must be true or false for kind 'boolean'.",
                    HardwareModuleMethods.AttributeValueKinds.Integer => "'value' must be an integer within the 64-bit range for kind 'integer'.",
                    HardwareModuleMethods.AttributeValueKinds.Number => "'value' must be a finite JSON number for kind 'number'.",
                    _ => "'value' must be the nonblank enum member name for kind 'enum'.",
                };
                return false;
        }

        value = converted;
        return true;
    }

    private static void ValidateExport(NetworkOperationRequest operation, string prefix, List<string> errors)
    {
        if (operation.FilePath is { } path)
        {
            if (!string.IsNullOrWhiteSpace(path) && IsDevicePath(path))
                errors.Add($"{prefix} 'filePath': {LocalFilePaths.DevicePathReason} (received '{path}').");
            else if (string.IsNullOrWhiteSpace(path) || !IsAbsoluteFilePath(path))
                errors.Add($"{prefix} 'filePath' must be an absolute file path (received '{path}').");
            else
            {
                // Two lexical rule sets serve export_io_map and each reports its own refusal: the PLC file-export
                // rules (PlcFileTargets: Windows device names, trailing dot or blank, '.'/'..' segments) and the shared
                // caller-file rules (LocalFilePaths: characters, alternate data streams, reserved names, segment ends).
                var shape = TiaMcpServer.Contracts.Plc.PlcFileTargets.PathShapeError("filePath", path);
                var refusal = CheckLocalFile(path);
                if (shape is not null)
                    errors.Add($"{prefix} {shape}");
                if (refusal is not null)
                    errors.Add($"{prefix} {refusal}");
                if (shape is null && refusal is null && !path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{prefix} 'filePath' must end in .csv.");
            }
        }

        if (operation.CsvDelimiter is { } delimiter && !CsvDelimiters.Contains(delimiter, StringComparer.Ordinal))
            errors.Add($"{prefix} 'csvDelimiter' must be ',', ';' or a tab character.");
        if (operation.DeviceName is not null && string.IsNullOrWhiteSpace(operation.DeviceName))
            errors.Add($"{prefix} 'deviceName' must not be blank when supplied.");
        if (operation.PlcName is not null && string.IsNullOrWhiteSpace(operation.PlcName))
            errors.Add($"{prefix} 'plcName' must not be blank when supplied.");
    }

    /// <summary>
    /// Absolute on the host OS, a Windows drive path or a UNC share (the server runs on Windows; tests run on Linux).
    /// Device paths are not accepted.
    /// </summary>
    internal static bool IsAbsoluteFilePath(string path)
        => !IsDevicePath(path)
            && (LocalFilePaths.IsUncShare(path) || !IsUncOrDevicePath(path) && (Path.IsPathFullyQualified(path)
                || path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')));

    /// <summary>A Win32 device or extended path (refused; <see cref="LocalFilePaths.DevicePathReason"/>).</summary>
    internal static bool IsDevicePath(string path) => LocalFilePaths.IsDevicePath(path);

    /// <summary>
    /// A UNC share (<c>\\server\share</c>, <c>//server/share</c>), a Win32 device/extended path (<c>\\?\</c>, <c>\\.\</c>) or the
    /// NT object path <c>\??\</c>, with either separator: the shared lexical rule (<see cref="LocalFilePaths.IsUncOrDevicePath"/>).
    /// </summary>
    internal static bool IsUncOrDevicePath(string path) => LocalFilePaths.IsUncOrDevicePath(path);

    /// <summary>
    /// The shared lexical rules beyond the root (<see cref="LocalFilePaths"/>): control, bidi and wildcard characters,
    /// alternate data streams, '.'/'..' segments, reserved device names and segments ending in a space or a dot. A mapped
    /// network drive passes. Null when acceptable.
    /// </summary>
    internal static string? CheckLocalFile(string path)
        => LocalFilePaths.CheckCharacters(path, "'filePath'") ?? LocalFilePaths.CheckSegments(path, "'filePath'");
}
