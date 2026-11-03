using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Plc;

/// <summary>The fields of the settings family (issues #15, #13, #12, #14, #21, #25); name, newName, filePath, culture, position, passwordVariable and others are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("The documented properties to set, as an object of property name -> value (string, number or boolean), applied in the documented order and each read back. "
        + "set_account_locking: EnableLockUserAccountAtRunTime, NumberOfFailedLoginAttempts (1-99), LoginRetryInterval (1-30 min), LockAccountForDuration (1-60 min). "
        + "set_plc_password_policy (project): PasswordPolicyEnabled. set_legacy_plc_password_policy (project): PasswordPolicyEnabled, MinimumLength (5-8), MinimumNumericCharacterLength, MinimumSpecialCharacterLength (0-8), IncludesLowerCaseAndUpperCaseCharacters. "
        + "set_syslog: EnableSystemLogging, TransportProtocol (None, TLSServerAndClientAuthentication, TLSOnlyServerAuthentication, UDP) and the CPU attributes SysLogAutoAcceptClient, SysLogClientCertificateId, SysLogTrustedCertificateIds. "
        + "create_project_syslog_server/update_project_syslog_server: Address, Port, Tls, Comment. set_system_web_pages: EnableSystemWebPages. set_front_panel_display: UserDefinedLogoActivated, AdaptLogoActivated. "
        + "Values are converted to the live property type (enum member names, flags as \"A, B\", ISO 8601 dates); TIA Portal decides whether a value is accepted.")]
    public PlcSettingValues? Settings { get; set; }

    [Description("set_watch_table_access, set_force_table_access (required): the web server or display access of the table, a WatchAndForceTableAccess member: Read or Write (None is refused by TIA Portal; remove the rule with remove_watch_table_access/remove_force_table_access).")]
    public string? Access { get; set; }

    [Description("set_watch_table_access, remove_watch_table_access, set_force_table_access, remove_force_table_access: true uses the WatchAndForceTableAccessManager of the CPU's display submodule (PLC display) instead of the CPU's (web server). Default false.")]
    public bool? Display { get; set; }

    [Description("add_syslog_server, remove_syslog_server (required): the syslog server address (IP address or FQDN) of the CPU's SysLogServerConfiguration.")]
    public string? Address { get; set; }

    [Description("add_syslog_server (required): the syslog server port, 0-65535.")]
    public int? Port { get; set; }

    [Description("set_ip_accessibility (required): the name of the communication module (CP) of the CPU's device to assign to the CPU attribute PlcAccessCommunicationModule.")]
    public string? ModuleName { get; set; }
}

/// <summary>
/// The <c>settings</c> argument: a flat JSON object of property name -> scalar (string, number, true/false). A nested value,
/// null or a duplicate key is an argument-shape error. Values are kept as invariant text in caller order.
/// </summary>
[JsonConverter(typeof(PlcSettingValuesConverter))]
public sealed class PlcSettingValues
{
    public PlcSettingValues(IReadOnlyList<KeyValuePair<string, string>> entries) => Entries = entries;

    public IReadOnlyList<KeyValuePair<string, string>> Entries { get; }
}

internal sealed class PlcSettingValuesConverter : JsonConverter<PlcSettingValues>
{
    public override PlcSettingValues Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("settings must be an object.");
        var entries = new List<KeyValuePair<string, string>>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new PlcSettingValues(entries);
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
            var key = reader.GetString()!;
            if (!keys.Add(key)) throw new JsonException("Duplicate key.");
            if (!reader.Read()) break;
            var text = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString()!,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Number => reader.TryGetInt64(out var integer) ? integer.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
                _ => throw new JsonException("settings values must be strings, numbers or booleans."),
            };
            entries.Add(new KeyValuePair<string, string>(key, text));
        }

        throw new JsonException("Unterminated object.");
    }

    public override void Write(Utf8JsonWriter writer, PlcSettingValues value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value.Entries) writer.WriteString(entry.Key, entry.Value);
        writer.WriteEndObject();
    }
}
