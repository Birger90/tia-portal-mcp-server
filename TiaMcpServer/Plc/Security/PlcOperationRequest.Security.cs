using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Plc;

/// <summary>The CPU protection fields of a PLC operation; passwords only as password sources, never as values.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("set_plc_access_level (required): the CPU access level, exactly one of FullAccessIncludingFailsafe (\"Vollzugriff inkl. Fehlersicherheit (kein Schutz)\", F-CPUs), FullAccess (\"Vollzugriff (kein Schutz)\"), ReadAccess (\"Lesezugriff\"), HMIAccess (\"HMI-Zugriff\"), NoAccess (\"Kein Zugriff (kompletter Schutz)\"). The protected levels need passwords for the more open levels (FullAccess at least; FullAccessIncludingFailsafe on an F-CPU): give them in accessLevelPasswords or the server password map (purpose = the level name). None is the enum's unset value and is never set.")]
    public string? AccessLevel { get; set; }

    [Description("set_plc_access_level: passwords of access levels, set with PlcAccessLevelProvider.SetPassword(level, SecureString) after the level is set (a failed password call restores the previous level); every level needs its own password. Object: key = a level name (FullAccessIncludingFailsafe, FullAccess, ReadAccess, HMIAccess), value = where the password is, never the password: an environment variable NAME (\"NAME\" or { \"env\": \"NAME\" }) or { \"credential\": \"<prefix>...\" }, a Windows Credential Manager generic credential (prefix TiaMcp/ unless the server owner set TIA_MCP_CREDENTIAL_PREFIX). Levels more open than accessLevel that the call omits are taken from the server password map (purpose = the level name) when it names them.")]
    public PlcDownloadPasswordMap? AccessLevelPasswords { get; set; }

    [Description("set_plc_access_level: levels whose password is removed with PlcAccessLevelProvider.ResetPassword(level) after the level changes (FullAccessIncludingFailsafe, FullAccess, ReadAccess, HMIAccess).")]
    public string[]? ResetAccessLevelPasswords { get; set; }

    [Description("change_plc_confidential_data_password and change_block_write_protection_password (required): where the new confidential-data (master secret) or block write-protection password is, never the password: an environment variable NAME or { \"credential\": \"<prefix>...\" }.")]
    [JsonConverter(typeof(PlcSafetyPasswordVariableConverter))]
    public string? NewPasswordVariable { get; set; }

    [Description("compare_to_online, compare_hardware_to_online and get_station_fingerprint_data: online password configurations answered while the compare connects (e.g. OnlineReadAccessPassword of a protected CPU): key = the configuration type short name, value = where the password is (an environment variable NAME or { \"credential\": \"<prefix>...\" }), never the password. Entries the call omits come from the server password map (purpose = the type short name).")]
    public PlcDownloadPasswordMap? Passwords { get; set; }

    [Description("protect_plc_confidential_data: true uses ProtectAllPlcConfigurationWithPassword (with passwordVariable) or ProtectAllPlcConfiguration() (without; the additional protection of the downloadable configuration data); disable_plc_confidential_data_protection: true calls UnprotectAllPlcConfiguration() (removes only that additional protection). Default false.")]
    public bool? AllConfiguration { get; set; }
}
