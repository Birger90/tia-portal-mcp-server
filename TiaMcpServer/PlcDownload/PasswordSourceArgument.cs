using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.PlcDownload;

/// <summary>
/// Reads one password source argument, shared by every password input: a plain string (an environment variable NAME,
/// backward compatible), <c>{ "env": "VAR" }</c> or <c>{ "credential": "TiaMcp/..." }</c> (a Windows Credential Manager
/// generic-credential target). The result is the source token (<see cref="PasswordSources"/>) after the binding-time
/// redaction: an invalid name or target becomes <see cref="PlcDownloadPasswordMap.RedactedVariable"/>, an unset
/// environment variable <see cref="PlcDownloadPasswordMap.UnsetVariable"/>, so no caller value that might be a password
/// reaches the pipeline, a response or the audit. Exception messages never carry caller values.
/// </summary>
internal static class PasswordSourceArgument
{
    public const string EnvMember = "env";
    public const string CredentialMember = "credential";

    public static string Read(ref Utf8JsonReader reader, Func<string, string?> readEnvironment)
    {
        if (reader.TokenType == JsonTokenType.String) return PlcDownloadPasswordMap.Redact(reader.GetString(), readEnvironment);
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A password source is an environment variable name, { \"env\": \"<NAME>\" } or { \"credential\": \"TiaMcp/<target>\" }.");
        string? kind = null, text = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (kind is null) throw new JsonException("A password source object needs exactly one of env or credential.");
                return kind == EnvMember
                    ? PlcDownloadPasswordMap.Redact(text, readEnvironment)
                    : PasswordSources.CredentialTargetError(text) is null ? PasswordSources.CredentialToken(text!) : PlcDownloadPasswordMap.RedactedVariable;
            }

            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
            var name = reader.GetString();
            if (name is not (EnvMember or CredentialMember)) throw new JsonException("A password source object takes only env or credential.");
            if (kind is not null) throw new JsonException("A password source object needs exactly one of env or credential.");
            if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new JsonException("A password source value must be a name string.");
            kind = name;
            text = reader.GetString();
        }

        throw new JsonException("Unterminated password source object.");
    }

    /// <summary>Writes a token back in the form it was read (credential tokens as objects), so a round trip keeps the source.</summary>
    public static void Write(Utf8JsonWriter writer, string? token)
    {
        if (token is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (PasswordSources.CredentialTarget(token) is { } target)
        {
            writer.WriteStartObject();
            writer.WriteString(CredentialMember, target);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(token);
        }
    }

    /// <summary>The input schema of one password source (with null when <paramref name="nullable"/>).</summary>
    /// <summary>The credential target schema for the owner's active prefix (no pattern when every target is allowed).</summary>
    private static JsonObject CredentialSchema()
    {
        var schema = new JsonObject { ["type"] = "string" };
        if (!PasswordSources.AcceptsAnyCredentialTarget)
            schema["pattern"] = "^" + System.Text.RegularExpressions.Regex.Escape(PasswordSources.ActiveCredentialPrefix) + ".+";
        return schema;
    }

    public static JsonObject Schema(bool nullable)
    {
        var options = new JsonArray(
            new JsonObject { ["type"] = "string", ["pattern"] = "^[A-Za-z_][A-Za-z0-9_]*$", ["description"] = "Environment variable NAME" },
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { [EnvMember] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray(EnvMember),
                ["additionalProperties"] = false,
            },
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { [CredentialMember] = CredentialSchema() },
                ["required"] = new JsonArray(CredentialMember),
                ["additionalProperties"] = false,
            });
        if (nullable) options.Add(new JsonObject { ["type"] = "null" });
        return new JsonObject { ["anyOf"] = options };
    }
}
