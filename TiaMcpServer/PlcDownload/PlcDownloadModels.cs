using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.PlcDownload;

/// <summary>One plc_download call; the operation identity is supplied by the host, never the caller.</summary>
public sealed record PlcDownloadWriteItem : IOperationBatchItem
{
    public string OperationId => PlcDownloadMethods.Download;

    public string Operation => PlcDownloadMethods.Download;

    public string? PlcName { get; init; }

    public string PcInterfaceName { get; init; } = PlcDownloadPolicy.SimulationPcInterfaceName;

    public int PcInterfaceNumber { get; init; } = PlcDownloadPolicy.DefaultPcInterfaceNumber;

    /// <summary>
    /// True when the caller supplied <c>pcInterface</c>. An omitted one defaults to
    /// <c>{ PLCSIM, 1 }</c> and is refused as <c>target_ambiguous</c> when the mode offers several
    /// PC interfaces with that name.
    /// </summary>
    public bool PcInterfaceNumberExplicit { get; init; }

    public string? TargetInterface { get; init; }

    public string Mode { get; init; } = PlcDownloadPolicy.DefaultMode;

    public string Scope { get; init; } = PlcDownloadPolicy.Scopes.HardwareAndSoftware;

    public bool StartAfterDownload { get; init; } = true;

    /// <summary>Issue #16: null (normal download) or <c>primary</c>/<c>backup</c> (R/H system CPU through RHDownloadProvider).</summary>
    public string? RhTarget { get; init; }

    /// <summary>Caller decisions by configuration type short name, in ordinal key order.</summary>
    public IReadOnlyList<PlcDownloadDecisionInfo> Decisions { get; init; } = Array.Empty<PlcDownloadDecisionInfo>();

    /// <summary>Password configurations answered from environment variables (names only), in ordinal key order.</summary>
    public IReadOnlyList<PlcDownloadPasswordInfo> Passwords { get; init; } = Array.Empty<PlcDownloadPasswordInfo>();

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/>.</summary>
    public string PortalConfirmations { get; init; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>The caller's <c>trustedCertificates</c> as given (validated and normalized by <see cref="PlcTlsTrustPolicy.NormalizeList"/>).</summary>
    public IReadOnlyList<string> TrustedCertificates { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The <c>trustedCertificates</c> argument of plc_download, plc_tia_online and plc_station_upload: a JSON array of
/// strings, the SHA-256 fingerprints of every certificate the device presents, after the user checked them. A
/// non-array, a non-string or null element or a nested value is an argument-shape error; the fingerprint format,
/// repeats and the count are refused by domain validation (<see cref="PlcTlsTrustPolicy.NormalizeList"/>).
/// Fingerprints are not secrets: they appear in effects, prompts and the audit.
/// </summary>
[JsonConverter(typeof(PlcTrustedCertificateListConverter))]
public sealed class PlcTrustedCertificateList
{
    public PlcTrustedCertificateList(IReadOnlyList<string> entries) => Entries = entries;

    public IReadOnlyList<string> Entries { get; }
}

internal sealed class PlcTrustedCertificateListConverter : JsonConverter<PlcTrustedCertificateList>
{
    public override PlcTrustedCertificateList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of fingerprint strings.");
        var entries = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return new PlcTrustedCertificateList(entries);
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Every trustedCertificates entry must be a string.");
            entries.Add(reader.GetString()!);
        }

        throw new JsonException("Unterminated array.");
    }

    public override void Write(Utf8JsonWriter writer, PlcTrustedCertificateList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value.Entries) writer.WriteStringValue(entry);
        writer.WriteEndArray();
    }
}

/// <summary>The caller's PC interface selector; both members are required when it is supplied.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcDownloadPcInterfaceArgument
{
    [JsonRequired]
    [Description("PC interface name, exact and case-sensitive, as list_download_targets reports it. PLCSIM is the simulation interface; any other name is real hardware.")]
    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    [Description("PC interface number, as list_download_targets reports it (PLCSIM is usually 1).")]
    public int Number { get; set; }
}

/// <summary>
/// The <c>decisions</c> argument: a JSON object whose keys are configuration type short names and whose
/// values are an enum member name (string) or <c>true</c>. Anything else (another value type, a
/// duplicate key) is an argument-shape error; blank strings are refused by domain validation.
/// </summary>
[JsonConverter(typeof(PlcDownloadDecisionMapConverter))]
public sealed class PlcDownloadDecisionMap
{
    public PlcDownloadDecisionMap(IReadOnlyList<PlcDownloadDecisionInfo> entries) => Entries = entries;

    /// <summary>The entries in caller order; <see cref="PlcDownloadDecisionInfo.Check"/> marks a <c>true</c> value.</summary>
    public IReadOnlyList<PlcDownloadDecisionInfo> Entries { get; }
}

/// <summary>
/// The <c>passwords</c> argument: a JSON object whose keys are password configuration type short names
/// and whose values are password sources (<see cref="PasswordSourceArgument"/>): the NAME of an environment variable,
/// <c>{ "env": "NAME" }</c> or <c>{ "credential": "TiaMcp/..." }</c> (a Windows Credential Manager generic credential).
/// Any other value or a duplicate key is an argument-shape error. A value that is not a valid variable name may be a
/// password pasted by mistake, so it is replaced by <see cref="RedactedVariable"/> while binding:
/// it never reaches the pipeline, the response or the audit, and validation refuses the placeholder.
/// A valid name that is not set (or empty) in this process's environment, which the worker inherits,
/// may also be a pasted password, so it is replaced by <see cref="UnsetVariable"/> the same way: a name
/// reaches the response and the audit only when the variable it names is set.
/// </summary>
[JsonConverter(typeof(PlcDownloadPasswordMapConverter))]
public sealed class PlcDownloadPasswordMap
{
    /// <summary>Stands in for a value that is not an environment variable name; itself never a valid name.</summary>
    public const string RedactedVariable = "(redacted: not an environment variable name)";

    /// <summary>Stands in for a valid name whose variable is not set or is empty; itself never a valid name.</summary>
    public const string UnsetVariable = "(redacted: the named environment variable is not set)";

    /// <summary>The binding-time redaction of one value (shared by plc_download, plc_tia_online and plc_station_upload).</summary>
    public static string Redact(string? value, Func<string, string?> readEnvironment)
        => Contracts.Shared.PasswordSources.KindOf(value) == Contracts.Shared.PasswordSourceKind.Credential ? value!
            : !PlcDownloadPolicy.IsEnvironmentVariableName(value) ? RedactedVariable
            : string.IsNullOrEmpty(readEnvironment(value!)) ? UnsetVariable
            : value!;

    public PlcDownloadPasswordMap(IReadOnlyList<PlcDownloadPasswordInfo> entries) => Entries = entries;

    public IReadOnlyList<PlcDownloadPasswordInfo> Entries { get; }
}

internal sealed class PlcDownloadDecisionMapConverter : JsonConverter<PlcDownloadDecisionMap>
{
    public override PlcDownloadDecisionMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var entries = new List<PlcDownloadDecisionInfo>();
        foreach (var (key, valueKind, text) in PlcDownloadArgumentMaps.ReadObject(ref reader))
        {
            entries.Add(valueKind switch
            {
                JsonTokenType.String => new PlcDownloadDecisionInfo { Type = key, Selection = text },
                JsonTokenType.True => new PlcDownloadDecisionInfo { Type = key, Check = true },
                _ => throw new JsonException("A decision value must be an enum member name or true."),
            });
        }

        return new PlcDownloadDecisionMap(entries);
    }

    public override void Write(Utf8JsonWriter writer, PlcDownloadDecisionMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value.Entries)
        {
            if (entry.Check) writer.WriteBoolean(entry.Type, true);
            else writer.WriteString(entry.Type, entry.Selection);
        }

        writer.WriteEndObject();
    }
}

internal sealed class PlcDownloadPasswordMapConverter : JsonConverter<PlcDownloadPasswordMap>
{
    public override PlcDownloadPasswordMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an object.");
        var entries = new List<PlcDownloadPasswordInfo>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new PlcDownloadPasswordMap(entries);
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
            var key = reader.GetString()!;
            if (!keys.Add(key)) throw new JsonException("Duplicate key.");
            if (!reader.Read()) break;
            entries.Add(new PlcDownloadPasswordInfo { Type = key, Variable = PasswordSourceArgument.Read(ref reader, Environment.GetEnvironmentVariable) });
        }

        throw new JsonException("Unterminated object.");
    }

    public override void Write(Utf8JsonWriter writer, PlcDownloadPasswordMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value.Entries)
        {
            writer.WritePropertyName(entry.Type);
            PasswordSourceArgument.Write(writer, entry.Variable);
        }

        writer.WriteEndObject();
    }
}

internal static class PlcDownloadArgumentMaps
{
    /// <summary>
    /// Reads one flat JSON object of scalar values. Duplicate keys (ordinal) and nested values are
    /// rejected; exception messages never carry caller values.
    /// </summary>
    internal static List<(string Key, JsonTokenType Kind, string? Text)> ReadObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an object.");
        var entries = new List<(string, JsonTokenType, string?)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return entries;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
            var key = reader.GetString()!;
            if (!keys.Add(key)) throw new JsonException("Duplicate key.");
            if (!reader.Read()) break;
            var kind = reader.TokenType;
            if (kind is JsonTokenType.StartObject or JsonTokenType.StartArray) throw new JsonException("Values must be scalars.");
            entries.Add((key, kind, kind == JsonTokenType.String ? reader.GetString() : null));
        }

        throw new JsonException("Unterminated object.");
    }
}

/// <summary>
/// The resolved PLC, download path, caller answers and requested consequences of one plc_download.
/// <c>TrustedCertificates</c> are the normalized SHA-256 fingerprints a TLS certificate check of the connection may trust.
/// </summary>
public sealed record PlcDownloadEffects(
    string PlcName,
    string DeviceName,
    PlcDownloadPathInfo Path,
    bool TargetInterfaceSelectedAutomatically,
    string Scope,
    string DownloadOptions,
    bool StartAfterDownload,
    bool SimulationTarget,
    IReadOnlyList<PlcDownloadDecisionInfo> Decisions,
    IReadOnlyList<PlcDownloadPasswordInfo> Passwords,
    string PortalConfirmations,
    IReadOnlyList<string> TrustedCertificates)
{
    /// <summary>Issue #16: the R/H CPU (<c>primary</c> or <c>backup</c>) the download goes to; omitted for a normal download.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RhTarget { get; init; }
}

/// <summary>
/// The plc_download document: the shared write envelope plus typed effects and one typed result
/// outcome. There is no verification read: completion is never claimed (see
/// <see cref="PlcDownloadResultInfo.CompletionVerified"/>).
/// </summary>
public sealed record PlcDownloadWriteResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    string Phase,
    IReadOnlyList<WriteGuardReport> Guards,
    PlcDownloadEffects? Effects,
    StandaloneToolOutcome<PlcDownloadResultInfo>? Result);

/// <summary>
/// The plc_download guards. Three acknowledge guards: the human controls the download through the access
/// mode (read-write confirms every actual call, full satisfies by policy). They are allowed because
/// the domain confirms every call (lifecycle model), so they are deliberately not built with
/// <c>WriteGuardCatalog.NonInteractive</c>. The one block guard refuses a caller SafetyProgram (F-program)
/// decision for a download that does not go to PLCSIM.
/// </summary>
public static class PlcDownloadGuardDefinitions
{
    public const string ToHardware = "download_to_hardware";

    public const string StopsPlc = "download_stops_plc";

    /// <summary>
    /// A caller SafetyProgram decision, on PLCSIM or a real controller: acknowledge, with the acceptance notice. A real
    /// controller also fires <see cref="ToHardware"/>; there is no block (owner decision 2026-11-01: not stricter than
    /// TIA Portal, which downloads F-programs to F-CPUs).
    /// </summary>
    public const string IncludesSafetyProgram = "download_includes_safety_program";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new WriteGuardDefinition(ToHardware, WriteGuardSeverities.Acknowledge,
            "The download path does not use the PLCSIM PC interface: it downloads to a real controller, and the machine it controls may stop or move. "
            + "In read-write the per-call confirmation covers it; full satisfies it by policy."),
        new WriteGuardDefinition(StopsPlc, WriteGuardSeverities.Acknowledge,
            "The download may stop the CPU (StopModules is answered StopAll unless the caller decides otherwise) and restarts it afterwards only when startAfterDownload is true."),
        new WriteGuardDefinition(IncludesSafetyProgram, WriteGuardSeverities.Acknowledge,
            "The caller answers TIA Portal's F-program download configuration (SafetyProgram), so the download (to PLCSIM or a real controller) includes the safety program. "
            + Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice),
    });

    public static WriteGuardCatalog Catalog { get; } = new(Definitions);
}
