namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// Parameters of one technology object worker method (<c>WorkerRequest.TechnologyObject</c>). The host
/// validated the shape; a method reads only the members it documents.
/// </summary>
public sealed class TechnologyObjectRequestInfo
{
    /// <summary>PLC software or device name; null addresses the only PLC (several fail <c>target_ambiguous</c>).</summary>
    public string? PlcName { get; set; }

    /// <summary>The technology object's name (case-insensitive, unique within the PLC's technology object tree).</summary>
    public string? Name { get; set; }

    /// <summary>Technology object group path ("/" or omitted for <c>PlcSoftware.TechnologicalObjectGroup</c>).</summary>
    public string? FolderPath { get; set; }

    public string? NewName { get; set; }

    /// <summary>create: the type identifier passed to <c>Create(name, type, version)</c>, e.g. <c>TO_PositioningAxis</c>.</summary>
    public string? TechnologyType { get; set; }

    /// <summary>create: the version text parsed with <see cref="Version"/> rules, e.g. <c>6.0</c>.</summary>
    public string? Version { get; set; }

    /// <summary>The full dot-notation parameter name as <c>TechnologicalParameterComposition.Find</c> takes it.</summary>
    public string? ParameterName { get; set; }

    /// <summary>get_technology_object_parameters: ordinal name prefix filter.</summary>
    public string? Prefix { get; set; }

    public int? Offset { get; set; }

    public int? Limit { get; set; }

    /// <summary>set_technology_object_parameter: the value as text, converted against the current value's runtime type.</summary>
    public string? Value { get; set; }

    /// <summary>The PLC tag table of a <c>PlcTag</c> value or connection (found by name anywhere in the PLC's tag table tree).</summary>
    public string? TableName { get; set; }

    public string? TagName { get; set; }

    /// <summary>One of <see cref="TechnologyObjectPolicy.Interfaces"/>.</summary>
    public string? ConnectionInterface { get; set; }

    /// <summary>Zero-based position in a sensor interface composition (<c>Item(Int32)</c>).</summary>
    public int? InterfaceIndex { get; set; }

    /// <summary>One of <see cref="TechnologyObjectPolicy.ConnectionKinds"/>: selects the documented <c>Connect</c> overload.</summary>
    public string? ConnectionKind { get; set; }

    /// <summary>Raw input bit address for the <c>Connect(Int32, ...)</c> overloads.</summary>
    public int? InputAddress { get; set; }

    /// <summary>Raw output bit address for the <c>Connect(..., Int32, ...)</c> overloads.</summary>
    public int? OutputAddress { get; set; }

    /// <summary>Exact <c>ConnectOption</c> member name, parsed on the live enum type.</summary>
    public string? ConnectOption { get; set; }

    /// <summary>Device item names from the device down to the module: [device, item, sub-item, ...] (ordinal, each level unique).</summary>
    public List<string>? ModulePath { get; set; }

    /// <summary>connectionKind modules: the output module, written like <see cref="ModulePath"/> (the input module).</summary>
    public List<string>? OutputModulePath { get; set; }

    /// <summary>connectionKind channel: <c>Channel.Number</c> within the module's <c>Channels</c>.</summary>
    public int? ChannelNumber { get; set; }

    /// <summary>connectionKind channel: optional exact <c>Channel.IoType</c> member name.</summary>
    public string? ChannelIoType { get; set; }

    /// <summary>connectionKind channel: optional exact <c>Channel.Type</c> member name.</summary>
    public string? ChannelType { get; set; }

    /// <summary>measuringInput with connectionKind module: the channel index of <c>Connect(DeviceItem, Int32)</c>.</summary>
    public int? ChannelIndex { get; set; }

    /// <summary>connectionKind dbMember: the DB member path of <c>Connect(String)</c>.</summary>
    public string? DbMemberPath { get; set; }

    /// <summary>export/import: an absolute local file path (no UNC or device path).</summary>
    public string? FilePath { get; set; }

    /// <summary>export: replace an existing file; import: <c>ImportOptions.Override</c>.</summary>
    public bool Overwrite { get; set; }

    /// <summary>update_technology_object_content: the edited SimaticML from get_technology_object_content.</summary>
    public string? Content { get; set; }

    public string? ExpectedContentHash { get; set; }
}

/// <summary><c>list_technology_objects</c>: every matching PLC's technology object groups and objects.</summary>
public sealed class TechnologyObjectInventoryInfo
{
    public List<TechnologyObjectPlcInfo> Plcs { get; set; } = new List<TechnologyObjectPlcInfo>();
}

public sealed class TechnologyObjectPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>User groups ("/Axes", "/Axes/Line1"); the system group "/" is not listed.</summary>
    public List<TechnologyObjectGroupInfo> Groups { get; set; } = new List<TechnologyObjectGroupInfo>();

    public List<TechnologyObjectInfo> TechnologyObjects { get; set; } = new List<TechnologyObjectInfo>();

    /// <summary>False when the object cap (<see cref="TechnologyObjectPolicy.MaxObjects"/>) stopped the walk.</summary>
    public bool Complete { get; set; } = true;
}

public sealed class TechnologyObjectGroupInfo
{
    public string Path { get; set; } = "/";

    public string Name { get; set; } = string.Empty;
}

/// <summary>One technology object (a <c>TechnologicalInstanceDB</c>).</summary>
public sealed class TechnologyObjectInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"/" for <c>PlcSoftware.TechnologicalObjectGroup</c>, otherwise the user group path.</summary>
    public string GroupPath { get; set; } = "/";

    /// <summary>The DB number (inherited <c>PlcBlock.Number</c>); null when unreadable.</summary>
    public int? Number { get; set; }

    /// <summary><c>OfSystemLibElement</c> as text: the system library element of the DB; null when unreadable.</summary>
    public string? SystemLibElement { get; set; }

    /// <summary><c>OfSystemLibVersion</c> as text: the version of that element; null when unreadable.</summary>
    public string? SystemLibVersion { get; set; }

    /// <summary>The runtime CLR type of the Openness object.</summary>
    public string? RuntimeType { get; set; }

    /// <summary>Informative members that could not be read (missing on this installation or throwing).</summary>
    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary><c>get_technology_object_parameters</c>: one page of a technology object's parameters.</summary>
public sealed class TechnologyObjectParametersInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string GroupPath { get; set; } = "/";

    /// <summary>The requested exact parameter (read with <c>Find</c>), or null for a listing.</summary>
    public string? ParameterName { get; set; }

    public string? Prefix { get; set; }

    /// <summary>Every parameter of the object (null for an exact <see cref="ParameterName"/> read).</summary>
    public int? TotalCount { get; set; }

    /// <summary>Parameters matching the prefix (or 1 for an exact read).</summary>
    public int MatchedCount { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; }

    /// <summary>The offset of the next page, or null when this page reaches the end.</summary>
    public int? NextOffset { get; set; }

    public List<TechnologyObjectParameterInfo> Parameters { get; set; } = new List<TechnologyObjectParameterInfo>();
}

/// <summary>One <c>TechnologicalParameter</c>: its name and its value typed by the value's runtime type.</summary>
public sealed class TechnologyObjectParameterInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="TechnologyObjectValueKinds"/>.</summary>
    public string ValueKind { get; set; } = TechnologyObjectValueKinds.Null;

    /// <summary>The runtime CLR type of the value; null for a null or unreadable value.</summary>
    public string? ValueType { get; set; }

    /// <summary>The value as invariant text (round-trip for reals, member name for enums, tag name for a PlcTag).</summary>
    public string? Value { get; set; }

    /// <summary>The enum's member names for an enum value (the first <see cref="TechnologyObjectPolicy.MaxEnumMembers"/>), otherwise empty.</summary>
    public List<string> EnumMembers { get; set; } = new List<string>();

    /// <summary>True when the enum has more members than <see cref="EnumMembers"/> lists; a name not listed is then checked by the worker.</summary>
    public bool EnumMembersTruncated { get; set; }

    /// <summary>True for a <c>[Flags]</c> enum: a write may combine exact member names separated by commas.</summary>
    public bool EnumFlags { get; set; }
}

/// <summary><c>get_technology_object_hardware_connection</c>: every documented connection provider the object offers.</summary>
public sealed class TechnologyObjectHardwareConnectionInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string GroupPath { get; set; } = "/";

    /// <summary>Full type names of every service <c>GetServiceInfos()</c> lists for the object (diagnostic, at most <see cref="TechnologyObjectPolicy.MaxServices"/>).</summary>
    public List<string> Services { get; set; } = new List<string>();

    public List<TechnologyObjectConnectionInterfaceInfo> Interfaces { get; set; } = new List<TechnologyObjectConnectionInterfaceInfo>();

    /// <summary>
    /// Connection providers GetServiceInfos() lists but GetService does not return (one reason each); their interfaces are
    /// missing from <see cref="Interfaces"/>. Empty normally. A listed-but-unretrievable service was seen live on V21 on
    /// 2026-11-02 (D13/D14, and K18: <c>EncoderHardwareConnectionProvider</c> of a TO_PositioningAxis 6.0); a read reports it
    /// instead of failing, and a connect or disconnect fails on it only when its interface needs that provider
    /// (<see cref="TechnologyObjectPolicy.ProvidersFor"/>).
    /// </summary>
    public List<string> UnavailableServices { get; set; } = new List<string>();
}

public sealed class TechnologyObjectConnectionInterfaceInfo
{
    /// <summary>One of <see cref="TechnologyObjectPolicy.Interfaces"/>.</summary>
    public string Interface { get; set; } = string.Empty;

    /// <summary>The documented provider type that exposes the interface.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The zero-based position in a sensor composition; null for a single interface.</summary>
    public int? Index { get; set; }

    public string? RuntimeType { get; set; }

    /// <summary><c>IsConnected</c>; null when unreadable or not documented for the interface (ident: see <see cref="TechnologyObjectConnectionEvidence.Connected"/>).</summary>
    public bool? IsConnected { get; set; }

    public List<TechnologyObjectConnectionPropertyInfo> Properties { get; set; } = new List<TechnologyObjectConnectionPropertyInfo>();

    /// <summary>Documented properties that are missing on the runtime type or threw.</summary>
    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>One documented property of a connection interface, rendered by the value's runtime type.</summary>
public sealed class TechnologyObjectConnectionPropertyInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="TechnologyObjectValueKinds"/> (deviceItem, channel and plcTag included).</summary>
    public string Kind { get; set; } = TechnologyObjectValueKinds.Null;

    public string? Value { get; set; }

    public string? RuntimeType { get; set; }

    /// <summary>For a device item (or a channel's device item): the names from the device down to it, usable as modulePath.</summary>
    public List<string>? ModulePath { get; set; }
}

/// <summary>The result of one technology object write, with the state the worker read back after the change.</summary>
public sealed class TechnologyObjectWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>The object's name after the operation (the new name after a rename); null for group operations and imports.</summary>
    public string? Name { get; set; }

    /// <summary>The group the object is in, or the group the operation created, deleted or renamed (its path after a rename).</summary>
    public string? GroupPath { get; set; }

    /// <summary>The object as read back after create, rename and update; null otherwise.</summary>
    public TechnologyObjectInfo? TechnologyObject { get; set; }

    /// <summary>set_technology_object_parameter: the value before the write.</summary>
    public TechnologyObjectParameterInfo? PreviousParameter { get; set; }

    /// <summary>set_technology_object_parameter: the value read back after the write (a fresh <c>Find</c>).</summary>
    public TechnologyObjectParameterInfo? Parameter { get; set; }

    /// <summary>connect/disconnect: the interface as read just before the call.</summary>
    public TechnologyObjectConnectionInterfaceInfo? PreviousInterface { get; set; }

    /// <summary>connect/disconnect: the interface read back after the call.</summary>
    public TechnologyObjectConnectionInterfaceInfo? Interface { get; set; }

    /// <summary>export: the written file (full path), its length and SHA-256 (lower-case hex).</summary>
    public string? FilePath { get; set; }

    public long? FileBytes { get; set; }

    public string? FileSha256 { get; set; }

    /// <summary>import/update: names of the objects the import reported or that appeared in the group.</summary>
    public List<string> ImportedObjects { get; set; } = new List<string>();

    /// <summary>import/update: the runtime type of <c>Import</c>'s return value (undocumented), or null.</summary>
    public string? ImportReturnedType { get; set; }
}
