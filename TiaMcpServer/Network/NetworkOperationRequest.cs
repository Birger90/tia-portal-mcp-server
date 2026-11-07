using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Network;

/// <summary>
/// Strict request shape for one dedicated network operation. Only fields declared by the selected
/// operation are permitted by <see cref="NetworkOperationCatalog"/>.
///
/// <para>
/// Creation (<c>add_network_device</c>) stays flat because it names something that does not exist
/// yet. Configuration (<c>configure_network_device</c>) instead splits into an exact
/// <see cref="Target"/> — which existing object is being written — and <see cref="Changes"/> —
/// what to set on it. A null change member means "leave this alone"; there is no flat alias and no
/// compatibility converter, so a caller that still sends the legacy flat fields is rejected rather
/// than silently writing to a device-wide guess.
/// </para>
///
/// <para>
/// Phase 3 introspection adds two read operations: <c>list_network_objects</c> (paged discovery
/// driven by <see cref="ObjectKinds"/>) and <c>inspect_network_object</c> (attribute-level detail
/// driven by <see cref="Target"/>). The <see cref="Target"/> type is the same for both phases;
/// the selector shape enforced by the catalog differs by operation.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this network operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Network operation to run: a network_read read or a network_write write, as each tool description lists them.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Hardware catalog search query. Required by search_equipment_catalog.")]
    public string? Query { get; set; }

    [Description("Optional result cap for search_equipment_catalog; must be 1 or greater when supplied.")]
    public int? MaxResults { get; set; }

    [Description("Exact equipment catalog type identifier (OrderNumber:, GSD: or System:). Required by add_network_device, plug_device_item, change_device_item_type and find_container_types; find_module_types takes a partial identifier (OrderNumber: at least one part, GSD: at least two, System: at least one).")]
    public string? TypeIdentifier { get; set; }

    [Description("Name for the new network device (required by add_network_device) or a device filter: list_network_objects allows it only when every requested kind is deviceItem, networkInterface, node, or communicationConnection; read_hardware_config takes an optional ordinal-ignore-case filter on the station/root device name (devices[].name, not a PLC/CPU device-item name; plcName is only for tag matching): exactly one match reads only that device, zero or multiple matches report a non-fatal message and no devices.")]
    public string? DeviceName { get; set; }

    [Description("Optional device item name. add_network_device: defaults to deviceName when omitted. plug_device_item: the new item's name; omitted lets TIA Portal choose it. Blank when supplied is rejected.")]
    public string? DeviceItemName { get; set; }

    [Description("Optional PLC software name for read_hardware_config tag matching. Exact ordinal match; when omitted, tag matching uses a PLC only when exactly one PLC exists. Blank when supplied is rejected.")]
    public string? PlcName { get; set; }

    [Description("Optional read_hardware_config flag: when true, each device item carries structured ioDetails (addresses and channels). Defaults to false; detailed output can be large, so prefer deviceName filtering or split calls.")]
    public bool? IncludeIoDetails { get; set; }

    [Description("Optional read_hardware_config flag: when true, each channel carries tagMatches resolved against the selected PLC's tag tables. Requires includeIoDetails=true; defaults to false.")]
    public bool? IncludeTagMatches { get; set; }

    [Description("Exact existing object to configure, inspect or change. Required by configure_network_device, create_io_system (kind 'node': the CPU's PROFINET node with deviceName, nodeId and interfacePath as read_hardware_config reports it), inspect_network_object and every hardware-module operation. Hardware-module operations take kind 'deviceItem' (a selector from read_hardware_config or list_network_objects); list_plug_locations, plug_device_item (the container), set_hardware_attributes, get/set/remove_custom_identity and list_port_connections also take kind 'device' with only deviceName, the others need a deviceItem (connect_ports, disconnect_ports: the port; list_transfer_areas, create/update/delete_transfer_area, disconnect_from_io_system: the item providing the NetworkInterface). delete_io_system takes kind 'ioSystem' (subnetId, number, optional ioSystemIndex and ioSystemName as read_hardware_config reports the IO system).")]
    public NetworkObjectTarget? Target { get; set; }

    [Description("User-defined custom identity key of get_custom_identity, set_custom_identity and remove_custom_identity (CustomIdentityProvider of the target device or device item). Required by those; nonblank.")]
    public string? Key { get; set; }

    [Description("User-defined custom identity value of set_custom_identity (CustomIdentityProvider.Set(key, value)). Required by set_custom_identity; any string, also empty.")]
    public string? Value { get; set; }

    [Description("Settings to change on the target. Required by configure_network_device; at least one change must be requested.")]
    public NetworkDeviceChanges? Changes { get; set; }

    [Description("Optional definition of the PROFINET IO system create_io_system creates: { name } (nonblank, passed to IoController.CreateIoSystem). Omitted passes an empty name and the result reports the name TIA Portal assigned. Only valid for create_io_system.")]
    public NetworkIoSystemDefinition? IoSystem { get; set; }

    // ------------------------------------------------------------------
    // Phase 3 introspection fields
    // ------------------------------------------------------------------

    [Description("One or more network object kinds to enumerate. Required by list_network_objects. Valid values: deviceItem, networkInterface, node, subnet, ioSystem, communicationConnection.")]
    public IReadOnlyList<string>? ObjectKinds { get; set; }

    [Description("Maximum number of objects to return in one page (1–200). Optional for read_hardware_config and list_network_objects.")]
    public int? PageSize { get; set; }

    [Description("Opaque pagination cursor returned by a previous read_hardware_config or list_network_objects call. Optional for those operations.")]
    public string? Cursor { get; set; }

    [Description("Attribute names to read on the inspected object. Optional for inspect_network_object; must be non-empty when supplied and must contain at most 200 unique names.")]
    public IReadOnlyList<string>? AttributeNames { get; set; }

    // ------------------------------------------------------------------
    // Phase 4 subnet lifecycle fields
    // ------------------------------------------------------------------

    [Description("Definition of the subnet to create. Required by create_subnet.")]
    public NetworkSubnetDefinition? Subnet { get; set; }

    [Description("Settings to change on the targeted subnet. Required by update_subnet; at least one change must be requested.")]
    public NetworkSubnetChanges? SubnetChanges { get; set; }

    // ------------------------------------------------------------------
    // Hardware-module fields (racks, slots, parameters, I/O addresses)
    // ------------------------------------------------------------------

    [Description("Destination container (kind 'device' with deviceName, or kind 'deviceItem') for move_device_item and copy_device_item. Optional; omitted means the target's current parent (the item path without its last segment, or the device for a top-level item).")]
    public NetworkObjectTarget? Destination { get; set; }

    [Description("The partner of connect_ports and disconnect_ports (kind 'deviceItem', the other port device item as read_hardware_config or list_port_connections reports it; required) and of create_transfer_area in the multicastable collection (kind 'deviceItem', the device item providing the partner NetworkInterface; required there).")]
    public NetworkObjectTarget? Partner { get; set; }

    [Description("Package G: the node of the target interface for create_subnet_and_connect_node, disconnect_node_from_subnets and create_communication_connection (Node.Name as get_interface_nodes lists it); optional when the interface has one node.")]
    public string? NodeName { get; set; }

    [Description("Package G create_subnet_and_connect_node (required): the name of the subnet Node.CreateAndConnectToSubnet creates.")]
    public string? NewSubnetName { get; set; }

    [Description("Package G create_communication_connection: the node of the partner interface; optional when it has one node.")]
    public string? PartnerNodeName { get; set; }

    [Description("Package G assign_pc_interface (required): None, PcStation or SoftwarePlc (PcInterfaceAssignmentMode); with partner (the CPU device item) AssignInterface(mode, DeviceItem).")]
    public string? AssignmentMode { get; set; }

    [Description("Package G create_communication_connection (required): S7Connection, HmiConnection, TcpConnection, UdpConnection, IsoOnTcpConnection, IsoConnection, FdlConnection or PtpConnection (ConnectionComposition.Create<T>).")]
    public string? ConnectionType { get; set; }

    [Description("Package G delete_communication_connection (required): the 0-based index list_communication_connections reports.")]
    public int? ConnectionIndex { get; set; }

    [Description("Package G delete_communication_connection: the expected connection name at connectionIndex; a different one is refused (state_changed).")]
    public string? ConnectionName { get; set; }

    [Description("The transfer area of create_transfer_area, update_transfer_area and delete_transfer_area: { collection, index?, name?, type?, positionNumber?, dataLength?, templateIndex? }. Required by those three.")]
    public NetworkTransferAreaRequest? TransferArea { get; set; }

    [Description("Position number (slot) in the container. Required and nonnegative for plug_device_item, move_device_item and copy_device_item; list free positions with list_plug_locations.")]
    public int? PositionNumber { get; set; }

    [Description("Attributes to write with set_hardware_attributes and update_transfer_area (on the selected transfer area; read its attributes with list_transfer_areas): 1-100 entries with unique case-sensitive names, each { name, kind, value } where kind is string, boolean, integer, number or enum (value is the exact member name). Read names, access and supportedTypes first with inspect_network_object.")]
    public IReadOnlyList<HardwareAttributeAssignment>? Attributes { get; set; }

    [Description("Optional set_hardware_attributes answer to TIA Portal's AttributeDelegate: abort (default, fail closed) or ignore (skip a rejected attribute and apply the others; partial update).")]
    public string? AttributeErrorPolicy { get; set; }

    [Description("Optional set_hardware_attributes write mode: bulk (default, one SetAttributes call that orders dependent attributes) or sequential (one SetAttribute per attribute in caller order, stops at the first failure).")]
    public string? AttributeWriteMode { get; set; }

    [Description("The existing Address of the target device item to change with set_io_address: { ioType, startAddress, length? } as list_io_addresses reports it.")]
    public HardwareAddressSelector? Address { get; set; }

    [Description("What set_io_address changes on the selected Address: { startAddress?, processImage?, isochronousMode?, interruptObNumber? }; at least one member.")]
    public HardwareAddressChanges? AddressChanges { get; set; }

    [Description("Absolute path of the CSV file export_io_map writes (must end in .csv), of the AutomationML file export_cax writes and import_cax reads (must end in .aml; import_cax: the file must exist). Required by those; the directory must exist.")]
    public string? FilePath { get; set; }

    [Description("Optional export_io_map, export_cax and import_cax flag: true replaces an existing file (export_cax: the export is staged next to the target and moved over it; export_cax and import_cax: an existing logFilePath is replaced); omitted or false refuses to overwrite.")]
    public bool? Overwrite { get; set; }

    [Description("Optional absolute log file path of export_cax and import_cax: selects the documented CaxProvider overload with a log file (it returns only true/false; the details are in the log); omitted, the overload returning a TransferResult with state, counts and messages. An existing log file needs overwrite:true; device paths are refused, a link or network location on the way is reported.")]
    public string? LogFilePath { get; set; }

    [Description("import_cax: how the import merges with existing devices, a CaxImportOptions member: MoveToParkingLot, OverwriteTiaDevice or RetainTiaDevice. Required by import_cax.")]
    public string? ImportOption { get; set; }

    [Description("Optional export_io_map field separator: ',' (default), ';' or a tab character.")]
    public string? CsvDelimiter { get; set; }

    [Description("Optional export_io_map flag: true writes cell values exactly as read. Omitted or false prefixes a single quote to a value starting with =, +, -, @, a tab or a carriage return so spreadsheet programs do not evaluate it as a formula.")]
    public bool? RawCells { get; set; }
}

/// <summary>
/// Names exactly one existing network object. Used both by configuration (Phase 2) and
/// by introspection (Phase 3). The <c>kind</c> field selects the selector shape; which
/// additional fields are required and which are inapplicable is enforced by the catalog.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkObjectTarget
{
    [Description("Preferred node owner path from the device root to the item providing its NetworkInterface service. Nonempty; each segment requires nonblank name and nonnegative positionNumber, with optional nonblank typeIdentifier. Cannot be combined with itemPath.")]
    public IReadOnlyList<NetworkInterfacePathSegment>? InterfacePath { get; set; }

    [Description("Network object kind: deviceItem, networkInterface, node, subnet, ioSystem or communicationConnection. configure_network_device takes only 'node' or none; update_subnet and delete_subnet require exactly 'subnet'.")]
    public string? Kind { get; set; }

    [Description("Exact device name. Required for deviceItem, networkInterface, node, and communicationConnection kinds.")]
    public string? DeviceName { get; set; }

    [Description("Path through the device item hierarchy. Required and non-empty for deviceItem, networkInterface, and communicationConnection kinds; every segment requires a non-negative index and positionNumber plus nonblank name and typeIdentifier.")]
    public IReadOnlyList<NetworkDeviceItemPathSegment>? ItemPath { get; set; }

    [Description("Optional exact interface-name constraint for networkInterface kind or node kind with an owner path; must be nonblank when supplied.")]
    public string? InterfaceName { get; set; }

    [Description("Network interface type (e.g. PROFINET, PROFIBUS). Optional for networkInterface kind.")]
    public string? InterfaceType { get; set; }

    [Description("Network interface operating mode. Optional for networkInterface kind.")]
    public string? InterfaceOperatingMode { get; set; }

    [Description("Exact nodeId reported by read_hardware_config. Required for node kind and configure_network_device.")]
    public string? NodeId { get; set; }

    [Description("Optional nonnegative consistency index within the selected owner's nodes. Required with legacy itemPath for node kind; optional with interfacePath. Requires an owner path.")]
    public int? NodeIndex { get; set; }

    [Description("Exact subnetId reported by read_hardware_config. Required for subnet and ioSystem kinds.")]
    public string? SubnetId { get; set; }

    [Description("Non-negative IO system number within its subnet. Required for ioSystem kind.")]
    public int? Number { get; set; }

    [Description("Optional zero-based sibling index captured from the owning subnet's IO-system collection; used to disambiguate duplicate numbers and must be non-negative when supplied.")]
    public int? IoSystemIndex { get; set; }

    [Description("Optional captured IO system name evidence for ioSystem kind; used to disambiguate duplicate numbers and must be nonblank when supplied.")]
    public string? IoSystemName { get; set; }

    [Description("Non-negative connection index within the owning device item composition. Required for communicationConnection kind.")]
    public int? ConnectionIndex { get; set; }

    [Description("Connection type. Required for communicationConnection. Valid values: S7Connection, FdlConnection, IsoConnection, IsoOnTcpConnection, PtpConnection, TcpConnection, UdpConnection, HmiConnection.")]
    public string? ConnectionType { get; set; }

    [Description("Local connection name. Required for communicationConnection and must be nonblank.")]
    public string? LocalConnectionName { get; set; }

    [Description("Local connection identifier. Required for S7Connection, FdlConnection, IsoConnection, IsoOnTcpConnection, PtpConnection, TcpConnection, and UdpConnection; not applicable to HmiConnection.")]
    public string? LocalConnectionId { get; set; }
}

/// <summary>
/// One segment of the path through a device's item hierarchy. Strict — unknown fields
/// are rejected to prevent silent misidentification.
///
/// <para>
/// Carries the same four evidence fields as <see cref="TiaMcpServer.Contracts.Network.DeviceItemPathSegmentInfo"/>
/// so a selector embedded in a <c>read_hardware_config</c> result can be forwarded directly into an
/// inspect request without field transformation.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkDeviceItemPathSegment
{
    [Description("Zero-based sibling index within the parent device item composition.")]
    public int? Index { get; set; }

    [Description("Name of the module at this level of the device hierarchy.")]
    public string Name { get; set; } = string.Empty;

    [Description("Position number of the module at this level of the device hierarchy.")]
    public int? PositionNumber { get; set; }

    [Description("Type identifier of the module at this level of the device hierarchy.")]
    public string TypeIdentifier { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkInterfacePathSegment
{
    [Description("Exact ordinal name of the item at this owner-path level; required and nonblank.")]
    public string Name { get; set; } = string.Empty;
    [Description("Position number at this owner-path level; required and nonnegative.")]
    public int? PositionNumber { get; set; }
    [Description("Optional captured type identifier used as an extra consistency constraint; nonblank when supplied.")]
    public string? TypeIdentifier { get; set; }
}

/// <summary>What to set on the targeted node. Every member is optional; null means no change.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkDeviceChanges
{
    [Description("New IP address for the targeted node. Omit to leave it unchanged.")]
    public string? IpAddress { get; init; }

    [Description("New subnet mask for the targeted node. Omit to leave it unchanged.")]
    public string? SubnetMask { get; init; }

    [Description("New PROFINET device name for the targeted node. Omit to leave it unchanged.")]
    public string? PnDeviceName { get; init; }

    [Description("Optional boolean for the node's 'Generate PROFINET device name automatically' setting, written before pnDeviceName. Pass false together with pnDeviceName when the node generates its name automatically; otherwise that request fails before any change. Omit to leave it unchanged.")]
    public bool? PnDeviceNameAutoGeneration { get; init; }

    [Description("Subnet to connect the targeted node to. Omit to leave the connection unchanged.")]
    public NetworkSubnetTarget? Subnet { get; init; }

    [Description("IO system to attach the targeted node to. Omit to leave the attachment unchanged.")]
    public NetworkIoSystemTarget? IoSystem { get; init; }
}

/// <summary>Names exactly one existing subnet.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkSubnetTarget
{
    [Description("Exact subnetId reported by read_hardware_config for the subnet to connect to.")]
    public string? SubnetId { get; init; }
}

/// <summary>Names exactly one existing IO system by its subnet and its number within that subnet.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkIoSystemTarget
{
    [Description("Exact subnetId of the subnet that owns the IO system. Must match changes.subnet.subnetId when a subnet change is also requested.")]
    public string? SubnetId { get; init; }

    [Description("IO system number within that subnet, as reported by read_hardware_config.")]
    public int? Number { get; init; }
}

/// <summary>
/// What to create as a new subnet. Deliberately has no writable identifier: a subnet's
/// <c>subnetId</c> is assigned by Openness at creation time and cannot be dictated by the caller.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkSubnetDefinition
{
    [Description("Name for the new subnet. Required and must be nonblank.")]
    public string? Name { get; set; }

    [Description("Network type for the new subnet. Required. Valid values: Ethernet, Profibus.")]
    public string? NetworkType { get; set; }

    [Description("Highest station address (0-126). Applicable only to Profibus subnets; optional even then.")]
    public int? HighestAddress { get; set; }

    [Description("Bus transmission speed. Applicable only to Profibus subnets; optional even then.")]
    public string? TransmissionSpeed { get; set; }
}

/// <summary>What to create as a new PROFINET IO system; its number is assigned by TIA Portal and read back.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkIoSystemDefinition
{
    [Description("Name of the new IO system. Required and nonblank when ioSystem is supplied.")]
    public string? Name { get; set; }
}

/// <summary>
/// What to change on an existing subnet. Every member is optional; null means no change.
/// Deliberately has no writable identifier and no <c>networkType</c>: neither can be changed on an
/// existing subnet through this contract.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkSubnetChanges
{
    [Description("New name for the targeted subnet. Omit to leave it unchanged.")]
    public string? Name { get; set; }

    [Description("New highest station address (0-126) for the targeted subnet. Omit to leave it unchanged.")]
    public int? HighestAddress { get; set; }

    [Description("New bus transmission speed for the targeted subnet. Omit to leave it unchanged.")]
    public string? TransmissionSpeed { get; set; }
}
