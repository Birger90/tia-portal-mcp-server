using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.PlcDownload;

/// <summary>
/// The server-wide password map (<see cref="PasswordMapFile"/>), loaded once at startup from the path in
/// <c>TIA_MCP_PASSWORD_MAP</c> or, when that is unset, <c>%APPDATA%\TiaMcpServer\passwords.json</c> if it exists. An
/// invalid file refuses startup. It supplies password sources (names only) that a call does not give itself: the
/// fail-safe password operations (purpose <c>safety</c>) and the <c>passwords</c> of plc_download, plc_tia_online and
/// plc_station_upload (purpose = configuration type short name). A caller's own entry always wins.
/// </summary>
public static class PasswordMapStore
{
    private static IReadOnlyList<PasswordMapEntry> _entries = Array.Empty<PasswordMapEntry>();

    public static IReadOnlyList<PasswordMapEntry> Entries => Volatile.Read(ref _entries);

    public static string? LoadedPath { get; private set; }

    /// <summary>The path to load, or null when there is none (no variable and no default file).</summary>
    public static string? ResolvePath(Func<string, string?> readEnvironment, string? applicationData, Func<string, bool> exists)
    {
        var configured = readEnvironment(PasswordMapFile.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (string.IsNullOrEmpty(applicationData)) return null;
        var path = Path.Combine(applicationData, PasswordMapFile.DirectoryName, PasswordMapFile.FileName);
        return exists(path) ? path : null;
    }

    /// <summary>Loads the map; returns the refusal (never containing a value from the file) or null.</summary>
    public static string? Load(Func<string, string?> readEnvironment, string? applicationData)
    {
        var path = ResolvePath(readEnvironment, applicationData, File.Exists);
        if (path is null) return null;
        try
        {
            Set(PasswordMapFile.Parse(File.ReadAllText(path)), path);
            return null;
        }
        catch (PasswordMapException exception)
        {
            return $"{exception.Message} (file: {path})";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"The password map file could not be read ({exception.GetType().Name}; file: {path}).";
        }
    }

    internal static void Set(IReadOnlyList<PasswordMapEntry> entries, string? path)
    {
        Volatile.Write(ref _entries, entries);
        LoadedPath = path;
    }

    /// <summary>The project name the map matches: the bound project's file name without extension.</summary>
    public static string? ProjectName(string? projectPath)
        => string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFileNameWithoutExtension(projectPath.Replace('\\', '/').Split('/').Last());

    /// <summary>The caller's entries plus the map's entries for configuration types the caller answered neither way, in ordinal key order.</summary>
    public static PlcDownloadPasswordInfo[] Merge(IReadOnlyList<PlcDownloadPasswordInfo>? given, IEnumerable<string> decisionKeys, string? projectPath, string? plc)
    {
        var entries = (given ?? Array.Empty<PlcDownloadPasswordInfo>()).ToList();
        foreach (var (purpose, source) in Missing(projectPath, plc, entries.Select(e => e.Type).Concat(decisionKeys)))
            entries.Add(new PlcDownloadPasswordInfo { Type = purpose, Variable = source });
        return entries.OrderBy(entry => entry.Type, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The map's sources for purposes on <paramref name="plc"/>, for keys the caller did not give.</summary>
    public static IEnumerable<(string Purpose, string Source)> Missing(string? projectPath, string? plc, IEnumerable<string> givenKeys)
    {
        var given = new HashSet<string>(givenKeys, StringComparer.Ordinal);
        var project = ProjectName(projectPath);
        // The fail-safe, the CPU access level and the block protection purposes belong to plc_write; only configuration type short
        // names are answers here.
        foreach (var purpose in Entries.Select(e => e.Purpose)
                     .Where(p => p != PasswordMapFile.SafetyPurpose && !Contracts.Plc.Security.PlcSecurityPolicy.PasswordLevels.Contains(p, StringComparer.Ordinal)
                         && p is not (Contracts.Plc.BlockProtection.PlcBlockProtectionNames.KnowHowPurpose or Contracts.Plc.BlockProtection.PlcBlockProtectionNames.WriteProtectionPurpose)
                         && !given.Contains(p))
                     .Distinct(StringComparer.Ordinal))
            if (PasswordMapFile.Lookup(Entries, project, plc, purpose) is { } source)
                yield return (purpose, source);
    }
}
