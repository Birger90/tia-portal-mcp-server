namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Names one existing hardware object on the worker wire: the device itself when
/// <see cref="ItemPath"/> is null, otherwise the device item reached by the exact indexed path
/// (the same four-part segment evidence as a <c>deviceItem</c> selector).
/// </summary>
public sealed class HardwareObjectSelectorInfo
{
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Null names the device; a non-empty path names one device item.</summary>
    public List<DeviceItemPathSegmentInfo>? ItemPath { get; set; }
}

/// <summary>One typed attribute value a caller asked to write. Exactly the member named by <see cref="Kind"/> is set.</summary>
public sealed class HardwareAttributeValueInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="HardwareModuleMethods.AttributeValueKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? StringValue { get; set; }

    public bool? BooleanValue { get; set; }

    public long? IntegerValue { get; set; }

    public double? NumberValue { get; set; }

    /// <summary>The exact (ordinal) enum member name.</summary>
    public string? EnumSymbol { get; set; }
}

/// <summary>Selects one existing <c>Address</c> of a device item by its current evidence.</summary>
public sealed class HardwareAddressSelectorInfo
{
    /// <summary>The <c>AddressIoType</c> member name, e.g. <c>Input</c>.</summary>
    public string IoType { get; set; } = string.Empty;

    /// <summary>The current <c>StartAddress</c> in bytes.</summary>
    public int StartAddress { get; set; }

    /// <summary>Optional current <c>Length</c> in bytes as an extra constraint.</summary>
    public int? Length { get; set; }
}

/// <summary>What to change on the selected <c>Address</c>; null means unchanged.</summary>
public sealed class HardwareAddressChangesInfo
{
    /// <summary>Modeled <c>Address.StartAddress</c> (Int32, read/write).</summary>
    public int? StartAddress { get; set; }

    /// <summary>Dynamic attribute <c>ProcessImage</c> (int).</summary>
    public int? ProcessImage { get; set; }

    /// <summary>Dynamic attribute <c>IsochronousMode</c> (bool).</summary>
    public bool? IsochronousMode { get; set; }

    /// <summary>Dynamic attribute <c>InterruptObNumber</c> (long; S7-300/400 only per the documentation).</summary>
    public long? InterruptObNumber { get; set; }
}

/// <summary>
/// Worker request of every hardware-module method; only the members the method documents are read.
/// </summary>
public sealed class HardwareModuleRequestInfo
{
    /// <summary>The object acted on: the container for plug and list_plug_locations, the item for move/copy/delete/change type/addresses, the device or item for attributes.</summary>
    public HardwareObjectSelectorInfo? Target { get; set; }

    /// <summary>The destination container of move and copy (already defaulted by the host).</summary>
    public HardwareObjectSelectorInfo? Destination { get; set; }

    /// <summary>Catalog type identifier: plug, change type, find_container_types; partial identifier for find_module_types.</summary>
    public string? TypeIdentifier { get; set; }

    /// <summary>Name passed to <c>PlugNew</c>; an empty string lets TIA Portal choose the name.</summary>
    public string? Name { get; set; }

    public int? PositionNumber { get; set; }

    public List<HardwareAttributeValueInfo>? Attributes { get; set; }

    /// <summary>read_hardware_attributes: the names to read; null reads every attribute.</summary>
    public List<string>? AttributeNames { get; set; }

    public string? AttributeErrorPolicy { get; set; }

    public string? AttributeWriteMode { get; set; }

    public HardwareAddressSelectorInfo? Address { get; set; }

    public HardwareAddressChangesInfo? AddressChanges { get; set; }

    /// <summary>probe_hardware_plug: one of <see cref="HardwareModuleMethods.ProbeOperations"/>.</summary>
    public string? ProbeOperation { get; set; }

    /// <summary>get/set/remove_custom_identity: the user-defined key.</summary>
    public string? CustomIdentityKey { get; set; }

    /// <summary>set_custom_identity: the user-defined value.</summary>
    public string? CustomIdentityValue { get; set; }

    /// <summary>connect_ports / disconnect_ports: the partner port's device item; create_transfer_area (multicastable): the partner interface's device item.</summary>
    public HardwareObjectSelectorInfo? Partner { get; set; }

    /// <summary>create/update/delete_transfer_area: the collection and the area.</summary>
    public TransferAreaRequestInfo? TransferArea { get; set; }

    /// <summary>export_cax / import_cax: the absolute .aml file path.</summary>
    public string? FilePath { get; set; }

    /// <summary>export_cax / import_cax: optional absolute log file path (selects the documented overload with a log file).</summary>
    public string? LogFilePath { get; set; }

    /// <summary>export_cax: the device to export; null exports the project.</summary>
    public string? DeviceName { get; set; }

    /// <summary>export_cax: true lets TIA Portal write over an existing file.</summary>
    public bool? Overwrite { get; set; }

    /// <summary>import_cax: the <c>CaxImportOptions</c> member name.</summary>
    public string? CaxImportOption { get; set; }

    /// <summary>delete_io_system: the exact IO system selector (kind ioSystem, subnetId, number, optional ioSystemIndex/ioSystemName).</summary>
    public NetworkObjectSelectorInfo? IoSystem { get; set; }

    /// <summary>Package G: the node of the target interface (create_subnet_and_connect_node, disconnect_node_from_subnets, create_communication_connection); null when it has one.</summary>
    public string? NodeName { get; set; }

    /// <summary>Package G create_subnet_and_connect_node: the name of the new subnet.</summary>
    public string? NewSubnetName { get; set; }

    /// <summary>Package G create_communication_connection: the node of the partner interface; null when it has one.</summary>
    public string? PartnerNodeName { get; set; }

    /// <summary>Package G assign_pc_interface: the PcInterfaceAssignmentMode member name.</summary>
    public string? AssignmentMode { get; set; }

    /// <summary>Package G create_communication_connection: the documented connection type short name (S7Connection, ...).</summary>
    public string? ConnectionType { get; set; }

    /// <summary>Package G delete_communication_connection: the 0-based index list_communication_connections reported.</summary>
    public int? ConnectionIndex { get; set; }

    /// <summary>Package G delete_communication_connection: the expected name at that index (refused as state_changed when it differs).</summary>
    public string? ConnectionName { get; set; }
}

/// <summary>The identity of a device or device item as read from TIA Portal.</summary>
public sealed class HardwareObjectIdentityInfo
{
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Empty for the device itself; otherwise a path forwardable as a <c>deviceItem</c> selector.</summary>
    public List<DeviceItemPathSegmentInfo> ItemPath { get; set; } = new();

    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }

    /// <summary>Null for the device itself.</summary>
    public int? PositionNumber { get; set; }
}

public sealed class HardwarePlugLocationInfo
{
    /// <summary><c>PlugLocation.PositionNumber</c>; null when unreadable or not an Int32.</summary>
    public int? PositionNumber { get; set; }

    /// <summary><c>PlugLocation.Label</c>; null when unreadable.</summary>
    public string? Label { get; set; }
}

/// <summary>Result of list_plug_locations: <c>GetPlugLocations()</c> describes free slots.</summary>
public sealed class HardwarePlugLocationListInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public List<HardwarePlugLocationInfo> PlugLocations { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of find_module_types.</summary>
public sealed class HardwareModuleTypeListInfo
{
    public string PartialTypeIdentifier { get; set; } = string.Empty;

    public List<string> ModuleTypes { get; set; } = new();
}

/// <summary>Result of find_container_types (network view modules only, per the documentation).</summary>
public sealed class HardwareContainerTypeListInfo
{
    public string TypeIdentifier { get; set; } = string.Empty;

    public List<string> ContainerTypes { get; set; } = new();
}

/// <summary>
/// One level of the path from the selected device item down to the sub-item an address belongs to. A sub-item may
/// report no type identifier (live 2026-11-02: the S7-1500 DI 32 module's addresses sit on its sub-item <c>DI_32/DI_32</c>,
/// whose <c>TypeIdentifier</c> is null), which is why this is not a <c>deviceItem</c> selector segment.
/// </summary>
public sealed class HardwareSubItemSegmentInfo
{
    /// <summary>Zero-based sibling index within the parent's <c>DeviceItems</c> composition.</summary>
    public int Index { get; set; }

    /// <summary>The sub-item's name; null when unreadable.</summary>
    public string? Name { get; set; }

    /// <summary>The sub-item's position number; null when unreadable.</summary>
    public int? PositionNumber { get; set; }

    /// <summary>The sub-item's type identifier; null when TIA Portal reports none or it is unreadable.</summary>
    public string? TypeIdentifier { get; set; }
}

/// <summary>One <c>Address</c> of a device item; unreadable or inapplicable members stay null with a message.</summary>
public sealed class HardwareIoAddressInfo
{
    /// <summary>
    /// Zero-based position in this listing at read time: the selected item's own <c>Addresses</c> in composition order, or,
    /// when it has none, its sub-items' addresses depth-first (each sub-item's own addresses before its children's).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The path from the selected item to the sub-item that owns this address; empty when the address belongs to the selected
    /// item itself.
    /// </summary>
    public List<HardwareSubItemSegmentInfo> SubItemPath { get; set; } = new();

    public string? IoType { get; set; }

    public int? StartAddress { get; set; }

    public int? Length { get; set; }

    /// <summary>Dynamic <c>Context</c> (<c>AddressContext</c> member name).</summary>
    public string? Context { get; set; }

    public int? ProcessImage { get; set; }

    public bool? IsochronousMode { get; set; }

    public long? InterruptObNumber { get; set; }
}

public sealed class HardwareIoAddressListInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public List<HardwareIoAddressInfo> Addresses { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of the internal planning read probe_hardware_plug.</summary>
public sealed class HardwarePlugProbeInfo
{
    public string Operation { get; set; } = string.Empty;

    public HardwareObjectIdentityInfo Container { get; set; } = new();

    /// <summary>The item moved or copied; null for plug.</summary>
    public HardwareObjectIdentityInfo? Source { get; set; }

    public int PositionNumber { get; set; }

    /// <summary>The return value of <c>CanPlugNew</c>, <c>CanPlugMove</c> or <c>CanPlugCopy</c>.</summary>
    public bool CanPlug { get; set; }
}

/// <summary>Whether a requested value converts to a type the attribute declares, decided before any write.</summary>
public sealed class HardwareAttributeConversionInfo
{
    public string Name { get; set; } = string.Empty;

    public bool Success { get; set; }

    /// <summary>The CLR type the value converts to; null when the conversion fails.</summary>
    public string? TargetType { get; set; }

    /// <summary>Why the conversion fails, or why the attribute is not writable; null on success.</summary>
    public string? Error { get; set; }
}

/// <summary>Result of the internal planning read read_hardware_attributes.</summary>
public sealed class HardwareAttributeReadInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public List<NetworkAttributeInfo> Attributes { get; set; } = new();

    /// <summary>One verdict per requested attribute value (empty when no values were sent).</summary>
    public List<HardwareAttributeConversionInfo> Conversions { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of plug, move, copy, delete and change-type writes.</summary>
public sealed class HardwareDeviceItemMutationResultInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary>The container the item was plugged, moved or copied into; null for delete and change type.</summary>
    public HardwareObjectIdentityInfo? Container { get; set; }

    /// <summary>The existing item before the change (moved, copied, deleted or retyped); null for plug.</summary>
    public HardwareObjectIdentityInfo? Source { get; set; }

    /// <summary>The item as read back after the change; null for delete.</summary>
    public HardwareObjectIdentityInfo? Result { get; set; }

    /// <summary>
    /// Where plug, move and copy actually placed the item: <see cref="Container"/> itself, or the container's device when
    /// the container is a top-level rack or rail item and TIA Portal placed the module directly under the device
    /// (live 2026-11-02, TIA Portal V21). Null for delete and change type.
    /// </summary>
    public HardwareObjectIdentityInfo? PlacedUnder { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>What happened to one requested attribute.</summary>
public sealed class HardwareAttributeWriteOutcomeInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The converted value that was written, in the read-side typed form.</summary>
    public NetworkAttributeValueInfo Requested { get; set; } = new();

    /// <summary>The CLR type the request was converted to.</summary>
    public string TargetType { get; set; } = string.Empty;

    public NetworkAttributeValueInfo? Before { get; set; }

    public NetworkAttributeValueInfo? After { get; set; }

    /// <summary><c>applied</c> (read back equal), <c>ignored</c> (the delegate reported it under policy ignore) or <c>notApplied</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}

public sealed class HardwareAttributeWriteResultInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public string AttributeErrorPolicy { get; set; } = string.Empty;

    public string AttributeWriteMode { get; set; } = string.Empty;

    public List<HardwareAttributeWriteOutcomeInfo> Attributes { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

public sealed class HardwareIoAddressWriteResultInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public List<HardwareIoAddressInfo> Before { get; set; } = new();

    public List<HardwareIoAddressInfo> After { get; set; } = new();

    /// <summary>Indexes whose read-back differs from before, including implicitly coupled addresses.</summary>
    public List<int> ChangedIndexes { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}
