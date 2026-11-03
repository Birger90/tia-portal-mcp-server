using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Cli;
using TiaMcpServer.Json;

namespace TiaMcpServer.PlcWebApi;

/// <summary>How the HTTPS server certificate of one configured target is checked.</summary>
public enum PlcWebApiCertificateValidation
{
    /// <summary>Normal operating-system validation (chain, name, validity).</summary>
    System,

    /// <summary>Accept exactly the pinned certificate (SHA-1 or SHA-256 fingerprint), nothing else.</summary>
    Thumbprint,

    /// <summary>No validation for this target only; every result carries a warning.</summary>
    None,
}

/// <summary>One validated PLC Web API target. Credentials are never stored here, only the names of their environment variables.</summary>
public sealed record PlcWebApiTarget(
    string Name,
    string Address,
    string? UserEnv,
    string? PasswordEnv,
    PlcWebApiCertificateValidation CertificateValidation,
    string? Thumbprint,
    TimeSpan RequestTimeout)
{
    public bool IsAnonymous => UserEnv is null;

    public string CertificateValidationName => PlcWebApiConfigLoader.ValidationName(CertificateValidation);
}

/// <summary>The configured targets, in file order; names are unique (ordinal).</summary>
public sealed class PlcWebApiConfiguration
{
    public static PlcWebApiConfiguration Empty { get; } = new(null, Array.Empty<PlcWebApiTarget>());

    public PlcWebApiConfiguration(string? sourcePath, IReadOnlyList<PlcWebApiTarget> targets)
    {
        SourcePath = sourcePath;
        Targets = targets;
    }

    /// <summary>The configuration file, or null when none was configured.</summary>
    public string? SourcePath { get; }

    public IReadOnlyList<PlcWebApiTarget> Targets { get; }

    public PlcWebApiTarget? Find(string? name)
        => name is null ? null : Targets.FirstOrDefault(target => string.Equals(target.Name, name, StringComparison.Ordinal));
}

/// <summary>The file shape; member names are camelCase and unknown members are refused.</summary>
internal sealed class PlcWebApiConfigFile
{
    [JsonRequired]
    public List<PlcWebApiTargetEntry?>? Targets { get; set; }
}

internal sealed class PlcWebApiTargetEntry
{
    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? UserEnv { get; set; }

    public string? PasswordEnv { get; set; }

    public PlcWebApiCertificateEntry? Certificate { get; set; }

    public int? RequestTimeoutSeconds { get; set; }
}

internal sealed class PlcWebApiCertificateEntry
{
    public string? Validation { get; set; }

    public string? Thumbprint { get; set; }
}

/// <summary>The outcome of loading a configuration file: the configuration, or every problem found.</summary>
public sealed record PlcWebApiConfigLoadResult(PlcWebApiConfiguration? Configuration, IReadOnlyList<string> Errors)
{
    public bool IsValid => Configuration is not null && Errors.Count == 0;
}

/// <summary>
/// Reads and validates the human-owned target file. Nothing here contacts a PLC or reads a credential
/// value: only the presence of the named environment variables is reported (by doctor).
/// </summary>
public static class PlcWebApiConfigLoader
{
    public const int MaxTargets = 64;
    public const int MaxNameLength = 64;
    public const int DefaultRequestTimeoutSeconds = 30;
    public const int MaxRequestTimeoutSeconds = 600;
    public const long MaxFileBytes = 1024 * 1024;

    public static IReadOnlyList<string> ValidationNames { get; } = Array.AsReadOnly(new[] { "system", "thumbprint", "none" });

    public static string ValidationName(PlcWebApiCertificateValidation validation) => validation switch
    {
        PlcWebApiCertificateValidation.System => "system",
        PlcWebApiCertificateValidation.Thumbprint => "thumbprint",
        PlcWebApiCertificateValidation.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(validation), validation, null),
    };

    public static PlcWebApiConfigLoadResult Load(string path, Func<string, string>? readAllText = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return Fail($"The PLC Web API configuration path must be absolute; got '{path}'.");
        string text;
        try
        {
            if (readAllText is null)
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Fail($"The PLC Web API configuration file '{path}' does not exist.");
                if (info.Length > MaxFileBytes) return Fail($"The PLC Web API configuration file '{path}' exceeds {MaxFileBytes} bytes.");
                text = File.ReadAllText(path);
            }
            else
            {
                text = readAllText(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return Fail($"The PLC Web API configuration file '{path}' could not be read: {exception.Message}");
        }

        return Parse(path, text);
    }

    public static PlcWebApiConfigLoadResult Parse(string? sourcePath, string text)
    {
        PlcWebApiConfigFile file;
        try
        {
            file = CanonicalJson.Deserialize<PlcWebApiConfigFile>(text);
        }
        catch (JsonException exception)
        {
            // Path only: the message may echo file content.
            return Fail($"The PLC Web API configuration is not valid JSON for its schema{(exception.Path is { Length: > 0 } p ? $" at {p}" : string.Empty)}. "
                + "Expected { \"targets\": [ { \"name\", \"address\", \"userEnv\"?, \"passwordEnv\"?, \"certificate\": { \"validation\", \"thumbprint\"? }, \"requestTimeoutSeconds\"? } ] } "
                + "with camelCase member names, no comments and no unknown members.");
        }

        var errors = new List<string>();
        var entries = file.Targets ?? new List<PlcWebApiTargetEntry?>();
        if (file.Targets is null) errors.Add("'targets' must be an array.");
        if (entries.Count > MaxTargets) errors.Add($"At most {MaxTargets} targets are supported; got {entries.Count}.");
        var targets = new List<PlcWebApiTarget>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var prefix = $"targets[{index}]";
            if (entry is null)
            {
                errors.Add($"{prefix} must be an object.");
                continue;
            }

            var before = errors.Count;
            if (!IsValidName(entry.Name))
                errors.Add($"{prefix}.name must be 1 to {MaxNameLength} characters without control characters or leading/trailing spaces.");
            else if (!names.Add(entry.Name!))
                errors.Add($"{prefix}.name '{entry.Name}' is used by another target.");
            else
                prefix = $"target '{entry.Name}'";

            var address = NormalizeAddress(entry.Address, out var addressError);
            if (addressError is not null) errors.Add($"{prefix}.address {addressError}");

            if ((entry.UserEnv is null) != (entry.PasswordEnv is null))
                errors.Add($"{prefix}: userEnv and passwordEnv must be given together (omit both for the anonymous user).");
            foreach (var (member, value) in new[] { ("userEnv", entry.UserEnv), ("passwordEnv", entry.PasswordEnv) })
                if (value is not null && !IsValidEnvironmentVariableName(value))
                    errors.Add($"{prefix}.{member} must be an environment variable name ([A-Za-z_][A-Za-z0-9_]*, at most 128 characters).");

            PlcWebApiCertificateValidation validation = PlcWebApiCertificateValidation.System;
            string? thumbprint = null;
            if (entry.Certificate is null)
            {
                errors.Add($"{prefix}.certificate is required: {{ \"validation\": \"system\" | \"thumbprint\" | \"none\", \"thumbprint\"? }}.");
            }
            else
            {
                switch (entry.Certificate.Validation)
                {
                    case "system": validation = PlcWebApiCertificateValidation.System; break;
                    case "thumbprint": validation = PlcWebApiCertificateValidation.Thumbprint; break;
                    case "none": validation = PlcWebApiCertificateValidation.None; break;
                    default:
                        errors.Add($"{prefix}.certificate.validation must be one of: {string.Join(", ", ValidationNames)}.");
                        break;
                }

                if (validation == PlcWebApiCertificateValidation.Thumbprint && entry.Certificate.Validation == "thumbprint")
                {
                    thumbprint = NormalizeThumbprint(entry.Certificate.Thumbprint);
                    if (thumbprint is null)
                        errors.Add($"{prefix}.certificate.thumbprint must be the SHA-1 (40 hex digits) or SHA-256 (64 hex digits) fingerprint of the PLC certificate; spaces and colons are ignored.");
                }
                else if (entry.Certificate.Thumbprint is not null)
                {
                    errors.Add($"{prefix}.certificate.thumbprint is only valid with validation 'thumbprint'.");
                }
            }

            var timeoutSeconds = entry.RequestTimeoutSeconds ?? DefaultRequestTimeoutSeconds;
            if (timeoutSeconds < 1 || timeoutSeconds > MaxRequestTimeoutSeconds)
                errors.Add($"{prefix}.requestTimeoutSeconds must be 1 to {MaxRequestTimeoutSeconds} (default {DefaultRequestTimeoutSeconds}).");

            if (errors.Count == before)
                targets.Add(new PlcWebApiTarget(entry.Name!, address!, entry.UserEnv, entry.PasswordEnv, validation, thumbprint,
                    TimeSpan.FromSeconds(timeoutSeconds)));
        }

        return errors.Count == 0
            ? new PlcWebApiConfigLoadResult(new PlcWebApiConfiguration(sourcePath, targets.AsReadOnly()), Array.Empty<string>())
            : new PlcWebApiConfigLoadResult(null, errors.AsReadOnly());
    }

    /// <summary>
    /// Accepts a bare host name, IPv4 address, bracketed IPv6 address, optionally with ":port". A scheme,
    /// path, query, fragment or user information is refused: the client always speaks https.
    /// </summary>
    internal static string? NormalizeAddress(string? address, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(address) || address.Trim() != address || address.Length > 255)
        {
            error = "must be a host name or IP address (optionally with :port), at most 255 characters, without surrounding spaces.";
            return null;
        }

        if (address.Contains("://", StringComparison.Ordinal) || address.IndexOfAny(new[] { '/', '\\', '?', '#', '@', ' ' }) >= 0)
        {
            error = "must be only a host name or IP address with an optional :port (no scheme, path, query or user); the Web API is always reached over https.";
            return null;
        }

        if (!Uri.TryCreate("https://" + address + "/", UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "is not a valid host name or IP address.";
            return null;
        }

        return address;
    }

    internal static string? NormalizeThumbprint(string? thumbprint)
    {
        if (thumbprint is null) return null;
        var hex = new string(thumbprint.Where(c => c is not (' ' or ':')).ToArray()).ToUpperInvariant();
        return (hex.Length is 40 or 64) && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    internal static bool IsValidName(string? name)
        => !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength && name.Trim() == name && !name.Any(char.IsControl);

    internal static bool IsValidEnvironmentVariableName(string name)
        => name.Length is > 0 and <= 128
           && (char.IsAsciiLetter(name[0]) || name[0] == '_')
           && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static PlcWebApiConfigLoadResult Fail(string error) => new(null, new[] { error });
}

public enum PlcWebApiConfigSource
{
    None,
    Option,
    Environment,
}

public sealed record PlcWebApiConfigOptionResult(bool IsValid, string? Path, PlcWebApiConfigSource Source, string? Error)
{
    public static string SourceName(PlcWebApiConfigSource source) => source switch
    {
        PlcWebApiConfigSource.None => "none",
        PlcWebApiConfigSource.Option => "option",
        PlcWebApiConfigSource.Environment => "environment",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}

/// <summary>Resolves the configuration path once at startup: <c>--plc-webapi-config</c>, then the environment, then none.</summary>
public static class PlcWebApiConfigOption
{
    public const string Flag = "--plc-webapi-config";
    public const string EnvVar = "TIA_MCP_PLC_WEBAPI_CONFIG";

    public static PlcWebApiConfigOptionResult Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null)
    {
        var option = StartupOptionReader.ReadSingle(args, Flag);
        if (option.Error is not null) return new(false, null, PlcWebApiConfigSource.Option, option.Error);
        var source = PlcWebApiConfigSource.Option;
        var path = option.Value;
        if (path is null)
        {
            path = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(EnvVar);
            if (string.IsNullOrWhiteSpace(path)) return new(true, null, PlcWebApiConfigSource.None, null);
            source = PlcWebApiConfigSource.Environment;
        }

        path = path.Trim();
        if (!Path.IsPathFullyQualified(path))
            return new(false, null, source, $"{(source == PlcWebApiConfigSource.Option ? Flag : EnvVar)} must be an absolute path to the PLC Web API target file; got '{path}'.");
        return new(true, path, source, null);
    }

    /// <summary>Parses the option and loads the file it names; a missing option yields the empty configuration.</summary>
    public static (PlcWebApiConfiguration? Configuration, string? Error) Resolve(string[] args, Func<string, string?>? getEnvironmentVariable = null)
    {
        var option = Parse(args, getEnvironmentVariable);
        if (!option.IsValid) return (null, option.Error);
        if (option.Path is null) return (PlcWebApiConfiguration.Empty, null);
        var loaded = PlcWebApiConfigLoader.Load(option.Path);
        return loaded.IsValid
            ? (loaded.Configuration, null)
            : (null, $"Invalid PLC Web API configuration '{option.Path}': " + string.Join(" ", loaded.Errors));
    }

    internal static string Describe(PlcWebApiConfiguration configuration)
        => configuration.SourcePath is null
            ? "none configured"
            : string.Create(CultureInfo.InvariantCulture, $"{configuration.Targets.Count} target(s) from {configuration.SourcePath}");
}
