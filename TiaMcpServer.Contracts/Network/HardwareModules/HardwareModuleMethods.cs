namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Worker method names of the hardware-module operations (rack/slot plugging, device-item
/// parameters, I/O addresses). Each public operation of <c>network_read</c>/<c>network_write</c>
/// uses the same name as its worker method; the two internal planning reads have no public
/// operation. The lists feed <c>OperationPolicyCatalog</c>, <c>ProjectCapabilityCatalog</c> and the
/// worker dispatch switch, so the three cannot drift.
/// </summary>
public static class HardwareModuleMethods
{
    /// <summary>Free plug locations of a device or device item (<c>HardwareObject.GetPlugLocations()</c>).</summary>
    public const string ListPlugLocations = "list_plug_locations";

    /// <summary>Module types matching a partial type identifier (<c>ModuleInformationProvider.FindModuleTypes</c>).</summary>
    public const string FindModuleTypes = "find_module_types";

    /// <summary>Container types of a module type (<c>ModuleInformationProvider.FindContainerTypes</c>).</summary>
    public const string FindContainerTypes = "find_container_types";

    /// <summary>The <c>Address</c> objects of one device item with their documented attributes.</summary>
    public const string ListIoAddresses = "list_io_addresses";

    /// <summary>Internal planning read: <c>CanPlugNew</c>/<c>CanPlugMove</c>/<c>CanPlugCopy</c>; never mutates.</summary>
    public const string ProbePlug = "probe_hardware_plug";

    /// <summary>Internal planning read: typed attributes of a device or device item, as inspect_network_object reads them.</summary>
    public const string ReadAttributes = "read_hardware_attributes";

    public const string PlugDeviceItem = "plug_device_item";
    public const string MoveDeviceItem = "move_device_item";
    public const string CopyDeviceItem = "copy_device_item";
    public const string DeleteDeviceItem = "delete_device_item";
    public const string ChangeDeviceItemType = "change_device_item_type";
    public const string SetHardwareAttributes = "set_hardware_attributes";
    public const string SetIoAddress = "set_io_address";

    /// <summary>
    /// Custom identities (GitHub issue #18; Siemens.Engineering.Base.xml, V21): <c>CustomIdentityProvider.Get(String)</c> of a
    /// device or device item, the provider found among its <c>GetServiceInfos()</c> at runtime.
    /// </summary>
    public const string GetCustomIdentity = "get_custom_identity";

    /// <summary><c>CustomIdentityProvider.Set(String, String)</c>, read back with <c>Get</c>.</summary>
    public const string SetCustomIdentity = "set_custom_identity";

    /// <summary><c>CustomIdentityProvider.Remove(String)</c>; a missing key (<c>CustomIdentityNotFoundException</c>) is target_not_found.</summary>
    public const string RemoveCustomIdentity = "remove_custom_identity";

    /// <summary>
    /// PROFINET port topology (GitHub issue #10; Siemens.Engineering.Base.xml, V21): every port device item of a device or device
    /// item (<c>NetworkPort</c> among its <c>GetServiceInfos()</c>) with <c>NetworkPort.ConnectedPorts</c> resolved to device items.
    /// </summary>
    public const string ListPortConnections = "list_port_connections";

    /// <summary><c>NetworkPort.ConnectToPort(NetworkPort)</c>, read back from <c>ConnectedPorts</c>.</summary>
    public const string ConnectPorts = "connect_ports";

    /// <summary><c>NetworkPort.DisconnectFromPort(NetworkPort)</c>, read back from <c>ConnectedPorts</c>.</summary>
    public const string DisconnectPorts = "disconnect_ports";

    /// <summary>
    /// IO systems and transfer areas (GitHub issue #11; Siemens.Engineering.Base.xml, V21): both transfer area compositions
    /// (<c>NetworkInterface.TransferAreas</c>, <c>.MulticastableTransferAreas</c>) of the interface of a device item.
    /// </summary>
    public const string ListTransferAreas = "list_transfer_areas";

    /// <summary><c>TransferAreaComposition.Create</c> or <c>MulticastableTransferAreaComposition.Create</c>, read back from the composition.</summary>
    public const string CreateTransferArea = "create_transfer_area";

    /// <summary><c>IEngineeringObject.SetAttribute</c> on one transfer area per requested attribute, read back with <c>GetAttribute</c>.</summary>
    public const string UpdateTransferArea = "update_transfer_area";

    /// <summary><c>TransferArea.Delete()</c> / <c>MulticastableTransferArea.Delete()</c>, read back from the composition.</summary>
    public const string DeleteTransferArea = "delete_transfer_area";

    /// <summary><c>IoSystem.Delete()</c> of the IO system (PROFINET IO system or DP master system) a subnet lists.</summary>
    public const string DeleteIoSystem = "delete_io_system";

    /// <summary><c>IoConnector.DisconnectFromIoSystem()</c> of the connected IO connector of a device item's interface.</summary>
    public const string DisconnectFromIoSystem = "disconnect_from_io_system";

    /// <summary>CAx data (GitHub issue #9; Siemens.Engineering.Step7.xml, V21): <c>CaxProvider.Export</c> of the project or one device to an .aml file.</summary>
    public const string ExportCax = "export_cax";

    /// <summary><c>CaxProvider.Import(FileInfo, [FileInfo,] CaxImportOptions)</c> of an .aml file into the project.</summary>
    public const string ImportCax = "import_cax";

    /// <summary>Host-only <c>network_write</c> operation: writes a CSV file, never calls the worker for the write.</summary>
    public const string ExportIoMap = "export_io_map";

    /// <summary>Public <c>network_read</c> operations served by a worker method of the same name.</summary>
    public static IReadOnlyList<string> PublicRead { get; } = new[]
    {
        ListPlugLocations, FindModuleTypes, FindContainerTypes, ListIoAddresses, GetCustomIdentity, ListPortConnections, ListTransferAreas,
    };

    /// <summary>Every read worker method, the two internal planning reads included (<c>Observe</c>).</summary>
    public static IReadOnlyList<string> Read { get; } = PublicRead.Concat(new[] { ProbePlug, ReadAttributes }).ToArray();

    /// <summary>Every mutating worker method (<c>ProjectMutation</c>), each also a public <c>network_write</c> operation.</summary>
    public static IReadOnlyList<string> Write { get; } = new[]
    {
        PlugDeviceItem, MoveDeviceItem, CopyDeviceItem, DeleteDeviceItem, ChangeDeviceItemType,
        SetHardwareAttributes, SetIoAddress, SetCustomIdentity, RemoveCustomIdentity, ConnectPorts, DisconnectPorts,
        CreateTransferArea, UpdateTransferArea, DeleteTransferArea, DeleteIoSystem, DisconnectFromIoSystem,
        ExportCax, ImportCax,
    };

    public static IReadOnlyList<string> All { get; } = Read.Concat(Write).ToArray();

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method) || IsWrite(method);

    /// <summary>Policy values of <c>set_hardware_attributes</c>: what the <c>AttributeDelegate</c> answers.</summary>
    public static class AttributeErrorPolicies
    {
        /// <summary><c>AttributeChoiceSelection.Abort</c> (default, fail closed).</summary>
        public const string Abort = "abort";

        /// <summary><c>AttributeChoiceSelection.Ignore</c>: the rejected attribute is skipped and the others are applied.</summary>
        public const string Ignore = "ignore";

        public static IReadOnlyList<string> All { get; } = new[] { Abort, Ignore };
    }

    /// <summary>Write modes of <c>set_hardware_attributes</c>.</summary>
    public static class AttributeWriteModes
    {
        /// <summary>One <c>SetAttributes(IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt;, AttributeDelegate)</c> call (default).</summary>
        public const string Bulk = "bulk";

        /// <summary>One <c>IEngineeringObject.SetAttribute</c> call per attribute in caller order, stopping at the first failure.</summary>
        public const string Sequential = "sequential";

        public static IReadOnlyList<string> All { get; } = new[] { Bulk, Sequential };
    }

    /// <summary>Operations whose feasibility <see cref="ProbePlug"/> evaluates.</summary>
    public static class ProbeOperations
    {
        public const string Plug = "plug";
        public const string Move = "move";
        public const string Copy = "copy";

        public static IReadOnlyList<string> All { get; } = new[] { Plug, Move, Copy };
    }

    /// <summary>Value kinds a caller may supply for a hardware attribute (the read-side vocabulary minus <c>null</c>).</summary>
    public static class AttributeValueKinds
    {
        public const string String = "string";
        public const string Boolean = "boolean";
        public const string Integer = "integer";
        public const string Number = "number";
        public const string Enum = "enum";

        public static IReadOnlyList<string> All { get; } = new[] { String, Boolean, Integer, Number, Enum };
    }

    /// <summary>The documented <c>AddressIoType</c> member names a caller may use to select an address.</summary>
    public static IReadOnlyList<string> AddressIoTypes { get; } = new[] { "Input", "Output", "Diagnosis", "Substitute", "None" };
}
