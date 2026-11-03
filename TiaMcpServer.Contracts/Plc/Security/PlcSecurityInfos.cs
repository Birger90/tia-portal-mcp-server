namespace TiaMcpServer.Contracts.Plc.Security;

/// <summary>
/// Parameters of one CPU protection worker method (<c>WorkerRequest.PlcSecurity</c>); never carries a password, only
/// password-source tokens (an environment variable name or <c>credential:&lt;target&gt;</c>, see <c>PasswordSources</c>).
/// </summary>
public sealed class PlcSecurityRequestInfo
{
    /// <summary>PLC software or device name. get_plc_security: null reads every PLC; the writes: null addresses the only PLC.</summary>
    public string? PlcName { get; set; }

    /// <summary>set_plc_access_level: the requested <c>PlcProtectionAccessLevel</c> member name (<see cref="PlcSecurityPolicy.SettableAccessLevels"/>).</summary>
    public string? AccessLevel { get; set; }

    /// <summary>
    /// protect_plc_confidential_data: the master secret; disable_plc_confidential_data_protection (optional) and
    /// change_plc_confidential_data_password: the current one. A password-source token.
    /// </summary>
    public string? PasswordVariable { get; set; }

    /// <summary>change_plc_confidential_data_password: the new master secret, a password-source token.</summary>
    public string? NewPasswordVariable { get; set; }

    /// <summary>set_plc_access_level: <c>SetPassword(level, ...)</c> for each entry (level name, password-source token).</summary>
    public List<PlcSecurityPasswordInfo> AccessLevelPasswords { get; set; } = new();

    /// <summary>set_plc_access_level: <c>ResetPassword(level)</c> for each level, after the level is set.</summary>
    public List<string> ResetAccessLevelPasswords { get; set; } = new();

    /// <summary>
    /// protect_plc_confidential_data: <c>ProtectAllPlcConfigurationWithPassword</c> (or <c>ProtectAllPlcConfiguration()</c>
    /// without a password); disable_plc_confidential_data_protection: <c>UnprotectAllPlcConfiguration()</c>.
    /// </summary>
    public bool AllConfiguration { get; set; }
}

/// <summary>One access level password: the level and where its password is (a password-source token).</summary>
public sealed class PlcSecurityPasswordInfo
{
    public string Level { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}

/// <summary>One password a write used: its purpose, the source token and its kind (never the value).</summary>
public sealed class PlcSecurityPasswordUseInfo
{
    /// <summary>An access level name, <c>PlcMasterSecretPassword</c> (current) or <c>PlcMasterSecretPassword:new</c>.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>The password-source token (an environment variable name or <c>credential:&lt;target&gt;</c>).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary><c>environment</c> or <c>credential</c>.</summary>
    public string Kind { get; set; } = string.Empty;
}

/// <summary>Result of get_plc_security: one entry per CPU that hosts PLC software.</summary>
public sealed class PlcSecurityReadInfo
{
    public List<PlcSecurityPlcInfo> Plcs { get; set; } = new();
}

public sealed class PlcSecurityPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Full type names of the services <c>GetServiceInfos()</c> lists for the CPU device item (diagnostic, sorted, capped).</summary>
    public List<string> Services { get; set; } = new();

    /// <summary>True when more than <see cref="PlcSecurityPolicy.MaxServices"/> services were listed and the rest omitted.</summary>
    public bool ServicesTruncated { get; set; }

    /// <summary><c>PlcAccessLevelProvider.PlcProtectionAccessLevel</c> (access level, "Zugriffsstufe").</summary>
    public PlcSecuritySettingInfo AccessLevel { get; set; } = new();

    /// <summary><c>PlcAccessControlConfigurationProvider.Configuration</c> (read only here).</summary>
    public PlcSecuritySettingInfo AccessControl { get; set; } = new();

    /// <summary>The <c>PlcMasterSecretConfigurator</c> service behind "Vertrauliche PLC-Konfigurationsdaten schützen".</summary>
    public PlcConfidentialDataInfo ConfidentialDataProtection { get; set; } = new();
}

/// <summary>One enum-valued CPU setting read through a documented service property.</summary>
public sealed class PlcSecuritySettingInfo
{
    /// <summary>The documented service type name looked up among the CPU's services.</summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>The documented property name.</summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>True when the CPU's <c>GetServiceInfos()</c> lists the service.</summary>
    public bool ServiceOffered { get; set; }

    /// <summary>The live service type's full name; null when the service is not offered.</summary>
    public string? ServiceType { get; set; }

    /// <summary>True when the property was found and read without an error.</summary>
    public bool Readable { get; set; }

    /// <summary>The enum member name; null unless <see cref="Readable"/>.</summary>
    public string? Value { get; set; }

    /// <summary>True when the live property has a public setter.</summary>
    public bool Writable { get; set; }

    /// <summary>The member names of the live property type (empty when the property is missing or not an enum).</summary>
    public List<string> AvailableValues { get; set; } = new();

    /// <summary>Why the value is missing or what to know about it; null when nothing to add.</summary>
    public string? Note { get; set; }
}

/// <summary>The confidential PLC configuration data protection service. No documented member reports its state.</summary>
public sealed class PlcConfidentialDataInfo
{
    /// <summary>The documented short type name (its namespace is not documented).</summary>
    public string Service { get; set; } = PlcSecurityPolicy.MasterSecretConfiguratorName;

    public bool ServiceOffered { get; set; }

    public string? ServiceType { get; set; }

    /// <summary>True when the documented <c>Protect(SecureString)</c> exists on the live service type (never called here).</summary>
    public bool ProtectBindable { get; set; }

    /// <summary>True when exactly the claimed <c>void Unprotect()</c> exists on the live service type.</summary>
    public bool UnprotectBindable { get; set; }

    /// <summary>Signatures of every public instance method named <c>Unprotect</c> on the live type, for diagnosis.</summary>
    public List<string> UnprotectCandidates { get; set; } = new();

    /// <summary>
    /// Public readable scalar properties of the live service and their values, uninterpreted (at most
    /// <see cref="PlcSecurityPolicy.MaxMembers"/>). Whether one of them states the protection is not documented.
    /// </summary>
    public List<PlcSecurityMemberInfo> ObservedMembers { get; set; } = new();

    public string? Note { get; set; }
}

/// <summary>One reflected public property and its value as text.</summary>
public sealed class PlcSecurityMemberInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The property type's full name.</summary>
    public string Type { get; set; } = string.Empty;

    public bool Readable { get; set; }

    /// <summary>The value as text (cut at <see cref="PlcSecurityPolicy.MaxText"/>); null when unreadable or null.</summary>
    public string? Value { get; set; }
}

/// <summary>Result of set_plc_access_level and disable_plc_confidential_data_protection.</summary>
public sealed class PlcSecurityWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary><see cref="PlcSecurityPolicy.Settings"/>: accessLevel or confidentialDataProtection.</summary>
    public string Setting { get; set; } = string.Empty;

    /// <summary>The live service type the member was bound on.</summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>The member that was invoked, as a signature (e.g. <c>PlcProtectionAccessLevel { set; }</c>, <c>void Unprotect()</c>).</summary>
    public string Member { get; set; } = string.Empty;

    /// <summary>set_plc_access_level: the requested level; null for the confidential-data operation.</summary>
    public string? Requested { get; set; }

    /// <summary>set_plc_access_level: the level read before the change; null when unreadable or not applicable.</summary>
    public string? Before { get; set; }

    public bool BeforeReadable { get; set; }

    /// <summary>set_plc_access_level: the level read back after the change; null when unreadable or not applicable.</summary>
    public string? After { get; set; }

    public bool AfterReadable { get; set; }

    /// <summary><see cref="PlcSecurityPolicy.ReadBacks"/>: matched, unreadable or notAvailable.</summary>
    public string ReadBack { get; set; } = string.Empty;

    /// <summary>disable_plc_confidential_data_protection: the observed service properties before and after (empty otherwise).</summary>
    public List<PlcSecurityMemberInfo> ObservedBefore { get; set; } = new();

    public List<PlcSecurityMemberInfo> ObservedAfter { get; set; } = new();

    /// <summary>Every Siemens member invoked, in order, as a signature (e.g. <c>void SetPassword(PlcProtectionAccessLevel, SecureString) [FullAccess]</c>).</summary>
    public List<string> Calls { get; set; } = new();

    /// <summary>The password sources the write used (tokens and kinds only); empty when it used none.</summary>
    public List<PlcSecurityPasswordUseInfo> PasswordSources { get; set; } = new();

    public string? Note { get; set; }
}
