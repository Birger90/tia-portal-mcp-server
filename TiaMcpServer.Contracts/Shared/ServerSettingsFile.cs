using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared.ClaudeBridge;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>The owner's server settings (<see cref="ServerSettingsFile"/>). Names only; a member that is not set is null.</summary>
public sealed class ServerSettings
{
    /// <summary>No setting at all (no file, or a file with only the version).</summary>
    public static readonly ServerSettings Empty = new(null);

    public ServerSettings(McpAccessMode? accessMode, ClaudeBridgeSettings? claudeBridge = null)
    {
        AccessMode = accessMode;
        ClaudeBridge = claudeBridge;
    }

    /// <summary>The access mode chosen in the password manager, or null when it is not set (the server then uses its default).</summary>
    public McpAccessMode? AccessMode { get; }

    /// <summary>The Claude bridge settings (version 2), or null when not set. Only the bridge reads them; the server ignores them.</summary>
    public ClaudeBridgeSettings? ClaudeBridge { get; }
}

/// <summary>A server settings file that is not accepted; the message never repeats a value from the file.</summary>
public sealed class ServerSettingsException(string message) : Exception(message);

/// <summary>What <see cref="ServerSettingsFile.Read(Func{string, string?}, string?, Func{string, bool}, Func{string, string})"/> found.</summary>
public sealed class ServerSettingsReadResult
{
    private ServerSettingsReadResult(ServerSettings settings, string? path, string? error)
    {
        Settings = settings;
        Path = path;
        Error = error;
    }

    /// <summary>No settings file (the default file does not exist and <see cref="ServerSettingsFile.EnvironmentVariable"/> is unset).</summary>
    public static readonly ServerSettingsReadResult None = new(ServerSettings.Empty, null, null);

    public static ServerSettingsReadResult Loaded(ServerSettings settings, string path) => new(settings, path, null);

    public static ServerSettingsReadResult Failed(string error, string? path) => new(ServerSettings.Empty, path, error);

    /// <summary>The settings read; <see cref="ServerSettings.Empty"/> when there is no file or the file was refused.</summary>
    public ServerSettings Settings { get; }

    /// <summary>The file that was read (or refused), or null when there is none.</summary>
    public string? Path { get; }

    /// <summary>Why the file is refused (English, with the path), or null. A refused file refuses server startup.</summary>
    public string? Error { get; }

    public bool IsValid => Error is null;
}

/// <summary>
/// The optional, owner-edited server settings file (default <c>%APPDATA%\TiaMcpServer\settings.json</c>, or the path in
/// <see cref="EnvironmentVariable"/>). Only the TiaMcp password manager writes it; the server process reads it once at
/// startup and never writes it, and no MCP tool reads or changes it (doctor only reports what was resolved). The schema is
/// closed and versioned, so a later key comes with a new version and an older server refuses rather than ignores it:
/// <code>
/// { "version": 1, "accessMode": "read-only" }
/// </code>
/// <c>accessMode</c> is optional (omitted: not set) and is one of the canonical names <c>read-only</c>, <c>read-write</c>,
/// <c>full</c> (<see cref="McpAccessModeNames"/>), spelled exactly. A <c>--access-mode</c> argument or
/// <c>TIA_MCP_ACCESS_MODE</c> takes precedence over it. An invalid file refuses startup.
/// Version 2 adds the optional closed object <c>claudeBridge</c> (<see cref="ClaudeBridgeSettings"/>, read only by the
/// Claude bridge); the server reads versions 1 and 2, and the writer writes version 1 when no bridge settings are set.
/// </summary>
public static class ServerSettingsFile
{
    public const string EnvironmentVariable = "TIA_MCP_SETTINGS";
    public const string DirectoryName = PasswordMapFile.DirectoryName;
    public const string FileName = "settings.json";
    public const int Version = 2;
    public const int MinVersion = 1;
    public const string AccessModeMember = "accessMode";

    private const string VersionMember = "version";
    private static readonly string[] RootMembersV1 = { VersionMember, AccessModeMember };
    private static readonly string[] RootMembersV2 = { VersionMember, AccessModeMember, ClaudeBridgeSettings.Member };

    /// <exception cref="ServerSettingsException">The text is not a valid version-1 settings file.</exception>
    public static ServerSettings Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            throw new ServerSettingsException("The server settings file is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ServerSettingsException($"The server settings file must be a JSON object with \"{VersionMember}\": {MinVersion} or {Version}.");
            // The version first, so a file of a later version is named as such rather than for its new members.
            if (!root.TryGetProperty(VersionMember, out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
                throw new ServerSettingsException($"The server settings file needs \"{VersionMember}\": {MinVersion} or {Version}.");
            if (number is < MinVersion or > Version)
                throw new ServerSettingsException($"The server settings file has version {number}; this server reads only versions {MinVersion} and {Version}. Update the server, or save the settings again with the TiaMcp Passwortverwaltung of this server.");
            var rootMembers = number == 1 ? RootMembersV1 : RootMembersV2;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in root.EnumerateObject())
            {
                if (!rootMembers.Contains(member.Name, StringComparer.Ordinal))
                    throw new ServerSettingsException($"The server settings file is refused: it has an unknown member (allowed in version {number}: {string.Join(", ", rootMembers)}).");
                if (!seen.Add(member.Name))
                    throw new ServerSettingsException($"The server settings file is refused: it repeats the member \"{member.Name}\".");
            }

            McpAccessMode? accessMode = null;
            if (root.TryGetProperty(AccessModeMember, out var mode))
            {
                if (mode.ValueKind != JsonValueKind.String || !TryParseCanonical(mode.GetString(), out var parsed))
                    throw new ServerSettingsException($"The server settings file: \"{AccessModeMember}\" must be \"read-only\", \"read-write\" or \"full\".");
                accessMode = parsed;
            }

            ClaudeBridgeSettings? bridge = null;
            if (root.TryGetProperty(ClaudeBridgeSettings.Member, out var bridgeElement))
                bridge = ClaudeBridgeSettings.Parse(bridgeElement);

            return new ServerSettings(accessMode, bridge);
        }
    }

    /// <summary>The settings as JSON (version 1, or 2 with bridge settings; an unset member is omitted), checked by parsing it again.</summary>
    public static string Serialize(ServerSettings settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionMember, settings.ClaudeBridge is null ? MinVersion : Version);
            if (settings.AccessMode is { } mode) writer.WriteString(AccessModeMember, McpAccessModeNames.ToName(mode));
            settings.ClaudeBridge?.Write(writer);
            writer.WriteEndObject();
        }

        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Parse(json);
        return json;
    }

    /// <summary>The file a writer uses: the path in <see cref="EnvironmentVariable"/>, else the %APPDATA% default (existing or not).</summary>
    public static string? TargetPath(Func<string, string?> readEnvironment, string? applicationData)
    {
        var configured = readEnvironment(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return string.IsNullOrEmpty(applicationData) ? null : System.IO.Path.Combine(applicationData, DirectoryName, FileName);
    }

    /// <summary>Reads the settings file of this machine and user (environment variable, else %APPDATA%).</summary>
    public static ServerSettingsReadResult Read(Func<string, string?> readEnvironment, string? applicationData)
        => Read(readEnvironment, applicationData, File.Exists, File.ReadAllText);

    /// <summary>
    /// Reads the settings file: the path in <see cref="EnvironmentVariable"/> (which must exist), else the %APPDATA% default
    /// when it exists. No file is no setting (<see cref="ServerSettingsReadResult.None"/>); an unreadable or invalid file is
    /// <see cref="ServerSettingsReadResult.Failed"/> and refuses startup.
    /// </summary>
    public static ServerSettingsReadResult Read(Func<string, string?> readEnvironment, string? applicationData, Func<string, bool> exists, Func<string, string> readAllText)
    {
        var configured = readEnvironment(EnvironmentVariable);
        string path;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            path = configured!;
            if (!exists(path))
                return ServerSettingsReadResult.Failed($"The server settings file named by {EnvironmentVariable} does not exist (file: {path}). Save the access mode in TiaMcp Passwortverwaltung, or remove the variable.", path);
        }
        else
        {
            if (string.IsNullOrEmpty(applicationData)) return ServerSettingsReadResult.None;
            path = System.IO.Path.Combine(applicationData, DirectoryName, FileName);
            if (!exists(path)) return ServerSettingsReadResult.None;
        }

        string text;
        try
        {
            text = readAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ServerSettingsReadResult.Failed($"The server settings file could not be read ({exception.GetType().Name}; file: {path}).", path);
        }

        try
        {
            return ServerSettingsReadResult.Loaded(Parse(text), path);
        }
        catch (ServerSettingsException exception)
        {
            return ServerSettingsReadResult.Failed($"{exception.Message} Fix or delete the file, or save the access mode again in TiaMcp Passwortverwaltung (file: {path}).", path);
        }
    }

    /// <summary>The access mode for an exactly spelled canonical name (no other case, no blanks).</summary>
    private static bool TryParseCanonical(string? value, out McpAccessMode mode)
        => McpAccessModeNames.TryParse(value, out mode) && string.Equals(McpAccessModeNames.ToName(mode), value, StringComparison.Ordinal);
}
