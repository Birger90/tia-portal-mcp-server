using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Parameters of one CPU/project settings worker method (<c>WorkerRequest.PlcSettings</c>). Never carries a password, only
/// password-source tokens (an environment variable name or <c>credential:&lt;target&gt;</c>, see <see cref="PasswordSources"/>).
/// Each operation reads only the fields its definition names (<see cref="PlcSettingsCatalog"/>); the others stay null.
/// </summary>
public sealed class PlcSettingsRequestInfo
{
    /// <summary>PLC software or device name. get_cpu_settings: null reads every PLC; a CPU write: null addresses the only PLC.</summary>
    public string? PlcName { get; set; }

    /// <summary>Documented property names with their requested values as invariant text, in caller order.</summary>
    public List<PlcSettingValueInfo> Settings { get; set; } = new();

    /// <summary>A password-source token (environment variable name or <c>credential:&lt;target&gt;</c>), never a value.</summary>
    public string? PasswordVariable { get; set; }

    /// <summary>The name of the object the operation addresses (a syslog server, a web application, a user, a role, a test ...).</summary>
    public string? Name { get; set; }

    /// <summary>A new name (renames).</summary>
    public string? NewName { get; set; }

    /// <summary>A syslog server address (IP address or FQDN).</summary>
    public string? Address { get; set; }

    /// <summary>A port number.</summary>
    public int? Port { get; set; }

    /// <summary>A watch or force table name.</summary>
    public string? TableName { get; set; }

    /// <summary>The watch-and-force table group path of <see cref="TableName"/> (null: the root group).</summary>
    public string? FolderPath { get; set; }

    /// <summary>A <c>WatchAndForceTableAccess</c> member name (Read, Write).</summary>
    public string? Access { get; set; }

    /// <summary>True: the display submodule's service instead of the CPU's (watch/force table access rules).</summary>
    public bool Display { get; set; }

    /// <summary>An absolute file path (a display logo, a default HTML page, a certificate, a printout ...).</summary>
    public string? FilePath { get; set; }

    /// <summary>An absolute directory path (the HTML directory of user-defined web pages).</summary>
    public string? DirectoryPath { get; set; }

    /// <summary>True replaces existing files or blocks where the operation documents that.</summary>
    public bool Overwrite { get; set; }

    /// <summary>A culture name (e.g. de-DE).</summary>
    public string? Culture { get; set; }

    /// <summary>A 1-based position (the row of the multilingual support table, a condition ...).</summary>
    public int? Position { get; set; }

    /// <summary>The name of a device item in the CPU's device (IP accessibility: the communication module).</summary>
    public string? ModuleName { get; set; }

    /// <summary>A user name (web server, OPC UA, project or UMC user).</summary>
    public string? UserName { get; set; }

    /// <summary>Enum member names combined into one value (web server user permissions).</summary>
    public List<string>? Permissions { get; set; }

    /// <summary>A UMAC role name (custom or system role).</summary>
    public string? RoleName { get; set; }

    /// <summary>An engineering or device function right (name, or the Openness identifier of a device function right).</summary>
    public string? RightName { get; set; }

    /// <summary>The kind of user whose roles change: projectUser, umcUser or umcUserGroup.</summary>
    public string? UserKind { get; set; }

    /// <summary>A project user's alias name.</summary>
    public string? AliasName { get; set; }

    /// <summary>True activates, false deactivates (project user, UMC user/group, anonymous user).</summary>
    public bool? Active { get; set; }

    /// <summary>True activates, false deactivates the project user's runtime session timeout.</summary>
    public bool? RuntimeSessionTimeout { get; set; }

    /// <summary>The group of a custom device function right.</summary>
    public string? Group { get; set; }

    /// <summary>A <c>CertificateUsage</c> member name (the template of a new certificate).</summary>
    public string? Usage { get; set; }

    /// <summary>A certificate's <c>Id</c> (as get_cpu_certificates reports it), or the id of a certificate supported service.</summary>
    public string? CertificateId { get; set; }

    /// <summary>webserver or opcUaServer: the dynamic attribute a certificate is assigned to.</summary>
    public string? CertificateRole { get; set; }

    /// <summary>Subject alternative names as <c>Type:Value</c> (Type a <c>SubjectAlternativeNameType</c> member).</summary>
    public List<string>? SubjectAlternativeNames { get; set; }

    /// <summary>A <c>CertificateSupportedServiceName</c> member name.</summary>
    public string? ServiceType { get; set; }

    /// <summary>A <c>SafetyPrintoutFilePrinter</c> member name (null: MicrosoftPrintToPdf).</summary>
    public string? Printer { get; set; }

    /// <summary>The documentation layout of the safety printout (e.g. DocuInfo_ISO_A4_Portrait).</summary>
    public string? DocumentLayout { get; set; }

    /// <summary>A <c>SafetyPrintoutOption</c> member name (null: All).</summary>
    public string? PrintOption { get; set; }

    /// <summary>Safety Validation Assistant: the evaluation device (a device item name from DeviceQuery.EvaluationDevices()).</summary>
    public string? EvaluationDevice { get; set; }

    /// <summary>Safety Validation Assistant: the activation test name.</summary>
    public string? TestName { get; set; }

    /// <summary>Safety Validation Assistant: the safety function name.</summary>
    public string? FunctionName { get; set; }

    /// <summary>Safety Validation Assistant: the device item of a condition's signal (from ActivationTest.AvailableDevices()).</summary>
    public string? SignalDevice { get; set; }

    /// <summary>Safety Validation Assistant: a <c>SignalUsage</c> member name.</summary>
    public string? SignalUsage { get; set; }

    /// <summary>Safety Validation Assistant: the condition's signal name.</summary>
    public string? SignalName { get; set; }

    /// <summary>Safety Validation Assistant: <c>TraceConfiguration.IsTraced</c> of a safety function.</summary>
    public bool? Traced { get; set; }
}

/// <summary>One requested setting: the documented property (or attribute) name and the value as invariant text.</summary>
public sealed class PlcSettingValueInfo
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>One password a write used: its purpose, the source token and its kind (never the value).</summary>
public sealed class PlcSettingsPasswordUseInfo
{
    public string Purpose { get; set; } = string.Empty;

    /// <summary>The password-source token (an environment variable name or <c>credential:&lt;target&gt;</c>).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary><c>environment</c> or <c>credential</c>.</summary>
    public string Kind { get; set; } = string.Empty;
}

/// <summary>
/// One change of a write with the value read before and after it (<see cref="RuntimeValueInfo"/>, null where nothing was read,
/// e.g. a password). <c>Matched</c>: the after value equals the requested one (null when nothing comparable was read).
/// </summary>
public sealed class PlcSettingChangeInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Requested { get; set; }

    public RuntimeValueInfo? Before { get; set; }

    public RuntimeValueInfo? After { get; set; }

    public bool? Matched { get; set; }
}

/// <summary>A file the operation wrote, re-read at its place.</summary>
public sealed class PlcSettingsFileInfo
{
    public string Path { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public bool Overwritten { get; set; }
}

/// <summary>
/// The result of one settings write. <c>ReadBack</c>: <see cref="PlcSettingsNames.ReadBacks"/>; a readable after value that
/// differs from the requested one fails the item (postcondition_failed) instead of returning this.
/// </summary>
public sealed class PlcSettingsResultInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary>The PLC software name; null for a project-wide setting.</summary>
    public string? PlcName { get; set; }

    public string? DeviceName { get; set; }

    /// <summary>The object that offers the service (e.g. <c>device item 'PLC_1'</c>, <c>project</c>).</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>The service type full name, null when the operation uses an attribute of the device item.</summary>
    public string? Service { get; set; }

    /// <summary>The object of the service the write addressed (a server, a table, a user ...), null for the service itself.</summary>
    public string? Target { get; set; }

    /// <summary>The bound members called, in order, as text.</summary>
    public List<string> Calls { get; set; } = new();

    public List<PlcSettingChangeInfo> Changes { get; set; } = new();

    public string ReadBack { get; set; } = PlcSettingsNames.ReadBacks.NotAvailable;

    public List<PlcSettingsPasswordUseInfo> PasswordSources { get; set; } = new();

    public PlcSettingsFileInfo? File { get; set; }

    /// <summary>Names the call returned or created (generated blocks, a certificate id ...).</summary>
    public List<string> Items { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of the settings reads: one entry per CPU and the project-wide sections.</summary>
public sealed class PlcSettingsReadInfo
{
    public string Operation { get; set; } = string.Empty;

    public List<PlcSettingsPlcInfo> Plcs { get; set; } = new();

    /// <summary>The project-wide sections (password policies, project syslog servers, ...); empty when the read has none.</summary>
    public List<PlcSettingsSectionInfo> Project { get; set; } = new();
}

public sealed class PlcSettingsPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public List<PlcSettingsSectionInfo> Sections { get; set; } = new();
}

/// <summary>
/// One documented service (or attribute group) as read: whether it is offered and where, its documented properties (values
/// projected as text, uninterpreted) and its compositions. <c>Unavailable</c>: listed but not returned, or a read refusal.
/// </summary>
public sealed class PlcSettingsSectionInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The service type full name, null for an attribute group.</summary>
    public string? Service { get; set; }

    public bool Offered { get; set; }

    /// <summary>Where it was found (e.g. <c>device item 'Display'</c>), null when not offered.</summary>
    public string? Owner { get; set; }

    public string? Unavailable { get; set; }

    public List<PlcSettingPropertyInfo> Properties { get; set; } = new();

    public List<PlcSettingsCollectionInfo> Collections { get; set; } = new();

    /// <summary>Documented methods found bindable on the live service (e.g. <c>SetPassword(SecureString)</c>).</summary>
    public List<string> Members { get; set; } = new();

    public string? Note { get; set; }
}

public sealed class PlcSettingPropertyInfo
{
    public string Name { get; set; } = string.Empty;

    public RuntimeValueInfo Value { get; set; } = new();

    /// <summary>The live property has a public setter (attributes: <c>GetAttributeInfos()</c> says so); false when unknown.</summary>
    public bool Writable { get; set; }
}

public sealed class PlcSettingsCollectionInfo
{
    public string Name { get; set; } = string.Empty;

    public List<PlcSettingsItemInfo> Items { get; set; } = new();

    public bool Truncated { get; set; }

    /// <summary>Why the collection could not be read; null when it was.</summary>
    public string? Error { get; set; }
}

public sealed class PlcSettingsItemInfo
{
    /// <summary>The item's identifying value (a name, an address, an id); null when unreadable.</summary>
    public string? Key { get; set; }

    public List<PlcSettingPropertyInfo> Properties { get; set; } = new();

    /// <summary>Nested collections (e.g. the conditions of a safety function); empty when none.</summary>
    public List<PlcSettingsCollectionInfo> Collections { get; set; } = new();
}
