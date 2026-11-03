using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Plc;

/// <summary>The fail-safe (F-) PLC fields of a PLC operation; <see cref="PlcOperationCatalog"/> restricts them per operation.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("safety_login, set_safety_password (the new password) and revoke_safety_password (the current password), protect_plc_confidential_data (the confidential-data master secret), change_plc_confidential_data_password and disable_plc_confidential_data_protection (the current one; optional there: omitted, Unprotect() without a password is called), the block protection writes (set_block_know_how_protection: the know-how password; remove_block_know_how_protection: the current one; define_block_write_protection, set_block_write_protection, remove_block_write_protection and change_block_write_protection_password: the (current) write-protection password; omitted, the server password map's purpose KnowHowProtection or BlockWriteProtection for the block's PLC is used), set_plc_master_secret_online (the new master secret of the online PLC; required): where the password is, never the password itself: the NAME of an environment variable set in the MCP client configuration (a plain string or { \"env\": \"NAME\" }), or { \"credential\": \"TiaMcp/...\" }, the target of a Windows Credential Manager generic credential the user stored once with cmdkey /generic (target names must start with TiaMcp/ unless the server owner set TIA_MCP_CREDENTIAL_PREFIX). A value that is not a variable name or such a target, or names a variable that is not set, is redacted while the request is read and refused. Required by the three safety writes unless the server's password map file names a source for the PLC (purpose safety); the confidential-data operations take the map's purpose PlcMasterSecretPassword. set_display_password (required): the display password (DisplayProtection.SetPassword).")]
    [JsonConverter(typeof(PlcSafetyPasswordVariableConverter))]
    public string? PasswordVariable { get; set; }

    [Description("create_safety_runtime_group: the name of the new F-runtime group (RuntimeGroupComposition.Create). set_safety_runtime_group: the existing F-runtime group (RuntimeGroupComposition.Find). Required by both.")]
    public string? RuntimeGroupName { get; set; }

    [Description("create_safety_runtime_group: optional main safety block as a block path with the PLC name, e.g. PLC_1/Main_Safety (an F-FC alone, or an F-FB together with mainSafetyInstanceDbPath). Omitted: TIA Portal creates the group with Create(name), which generates its own main safety block. set_safety_runtime_group (required): the block to assign as the group's main safety block.")]
    public string? MainSafetyBlockPath { get; set; }

    [Description("create_safety_runtime_group, set_safety_runtime_group: the instance DB of an F-FB main safety block, as a block path with the PLC name; requires mainSafetyBlockPath.")]
    public string? MainSafetyInstanceDbPath { get; set; }
}

/// <summary>
/// Binds <c>passwordVariable</c> through the same redaction as plc_download's <c>passwords</c>: only a valid name of a set
/// variable survives the binding, so neither a pasted password nor the name of an unset variable can reach a response or
/// the audit.
/// </summary>
internal sealed class PlcSafetyPasswordVariableConverter : JsonConverter<string?>
{
    public override bool HandleNull => false;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return PasswordSourceArgument.Read(ref reader, Environment.GetEnvironmentVariable);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        PasswordSourceArgument.Write(writer, value);
    }
}
