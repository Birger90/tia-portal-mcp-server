using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Cli;

/// <summary>Where the resolved access mode came from.</summary>
public enum AccessModeSource
{
    Default,
    Cli,
    Environment,
    SettingsFile
}

/// <summary>
/// Resolves the <see cref="McpAccessMode"/> from CLI arguments, the environment variable and the owner's server settings
/// file (<see cref="ServerSettingsFile"/>, written only by the TiaMcp password manager).
/// Precedence: CLI argument > environment variable > settings file > default (ReadWrite).
/// </summary>
public static class AccessModeParser
{
    public const string EnvVarName = "TIA_MCP_ACCESS_MODE";

    /// <summary>
    /// Resolves the access mode from the given CLI arguments, environment and settings file.
    /// Returns a result indicating success with the resolved mode and its source, or failure with a message.
    /// </summary>
    /// <param name="settings">
    /// The settings file read at startup (<see cref="ServerSettingsFile.Read(Func{string, string?}, string?)"/>); null means
    /// no settings file is considered. A refused file fails the parse even when an argument or the environment decides the
    /// mode: the file is the owner's configuration and must not be silently ignored.
    /// </param>
    public static AccessModeParseResult Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null, ServerSettingsReadResult? settings = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        if (settings is { IsValid: false })
        {
            return AccessModeParseResult.Fail(settings.Error!);
        }

        var settingsPath = settings?.Path;
        McpAccessMode? cliMode = null;

        AccessModeParseResult? RecordCliMode(McpAccessMode candidate)
        {
            if (cliMode is not null && cliMode.Value != candidate)
            {
                return AccessModeParseResult.Fail(
                    "Conflicting access mode arguments were supplied. Specify only one of 'read-only', 'read-write', or 'full'.");
            }

            cliMode = candidate;
            return null;
        }

        // 1. Check all CLI arguments. Repeating the same mode is harmless; contradictory
        // explicit modes are rejected rather than depending on argument order.
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--read-only", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = RecordCliMode(McpAccessMode.ReadOnly);
                if (conflict is not null)
                {
                    return conflict;
                }

                continue;
            }

            if (string.Equals(args[i], "--read-write", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = RecordCliMode(McpAccessMode.ReadWrite);
                if (conflict is not null)
                {
                    return conflict;
                }

                continue;
            }

            if (string.Equals(args[i], "--access-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return AccessModeParseResult.Fail(
                        "--access-mode requires a value. Valid values: 'read-only', 'read-write', 'full'.");
                }

                var parsed = ParseValue(args[++i]);
                if (!parsed.IsValid)
                {
                    return parsed;
                }

                var conflict = RecordCliMode(parsed.Mode);
                if (conflict is not null)
                {
                    return conflict;
                }

                continue;
            }

            const string accessModePrefix = "--access-mode=";
            if (args[i].StartsWith(accessModePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ParseValue(args[i].Substring(accessModePrefix.Length));
                if (!parsed.IsValid)
                {
                    return parsed;
                }

                var conflict = RecordCliMode(parsed.Mode);
                if (conflict is not null)
                {
                    return conflict;
                }
            }
        }

        if (cliMode is not null)
        {
            return AccessModeParseResult.Ok(cliMode.Value, AccessModeSource.Cli, settingsPath);
        }

        // 2. Check environment variable
        var envValue = getEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            var parsed = ParseValue(envValue);
            return parsed.IsValid
                ? AccessModeParseResult.Ok(parsed.Mode, AccessModeSource.Environment, settingsPath)
                : AccessModeParseResult.Fail(
                    $"Invalid {EnvVarName} value '{envValue}'. Valid values: 'read-only', 'read-write', 'full'.");
        }

        // 3. Settings file (the password manager's "Zugriffsmodus")
        if (settings?.Settings.AccessMode is { } fileMode)
        {
            return AccessModeParseResult.Ok(fileMode, AccessModeSource.SettingsFile, settingsPath);
        }

        // 4. Default
        return AccessModeParseResult.Ok(McpAccessMode.ReadWrite, AccessModeSource.Default, settingsPath);
    }

    /// <summary>
    /// Parses a canonical access mode name into an <see cref="McpAccessMode"/>.
    /// </summary>
    public static AccessModeParseResult ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AccessModeParseResult.Fail(
                "Access mode value is empty. Valid values: 'read-only', 'read-write', 'full'.");
        }

        if (McpAccessModeNames.TryParse(value, out var mode))
        {
            return AccessModeParseResult.Ok(mode);
        }

        return AccessModeParseResult.Fail(
            $"Invalid access mode '{value}'. Valid values: 'read-only', 'read-write', 'full'.");
    }
}

public sealed record AccessModeParseResult(bool IsValid, McpAccessMode Mode, string? Error)
{
    /// <summary>Where <see cref="Mode"/> came from (meaningful when <see cref="IsValid"/>).</summary>
    public AccessModeSource Source { get; init; } = AccessModeSource.Default;

    /// <summary>The server settings file that was read (whether or not it decided the mode), or null when there is none.</summary>
    public string? SettingsPath { get; init; }

    public static AccessModeParseResult Ok(McpAccessMode mode) => new(true, mode, null);

    public static AccessModeParseResult Ok(McpAccessMode mode, AccessModeSource source, string? settingsPath = null)
        => new(true, mode, null) { Source = source, SettingsPath = settingsPath };

    public static AccessModeParseResult Fail(string error) => new(false, default, error);

    /// <summary>The canonical source name: <c>cli</c>, <c>environment</c>, <c>settingsFile</c> or <c>default</c>.</summary>
    public static string SourceName(AccessModeSource source) => source switch
    {
        AccessModeSource.Default => "default",
        AccessModeSource.Cli => "cli",
        AccessModeSource.Environment => "environment",
        AccessModeSource.SettingsFile => "settingsFile",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    /// <summary>The source with its detail, for the startup log: <c>settingsFile (path)</c>, <c>environment (TIA_MCP_ACCESS_MODE)</c>, <c>cli</c>, <c>default</c>.</summary>
    public string DescribeSource() => Source switch
    {
        AccessModeSource.SettingsFile => $"{SourceName(Source)} ({SettingsPath})",
        AccessModeSource.Environment => $"{SourceName(Source)} ({AccessModeParser.EnvVarName})",
        _ => SourceName(Source)
    };
}
