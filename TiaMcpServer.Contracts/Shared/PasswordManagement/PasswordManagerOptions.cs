namespace TiaMcpServer.Contracts.Shared.PasswordManagement;

/// <summary>
/// The start options of the password manager: the credential prefix the server accepts (<c>--prefix</c>, else
/// <see cref="PasswordSources.CredentialPrefixVariable"/> of the environment, else the default) and the map file
/// (<c>--map</c>, else <see cref="PasswordMapFile.EnvironmentVariable"/>, else the %APPDATA% default) and the server
/// settings file (<c>--settings</c>, else <see cref="ServerSettingsFile.EnvironmentVariable"/>, else the %APPDATA% default).
/// All are the owner's settings; the program never reads a password from its arguments.
/// </summary>
public sealed class PasswordManagerOptions
{
    private PasswordManagerOptions(string prefix, string prefixSource, string? mapPath, string? settingsPath, string? error)
    {
        Prefix = prefix;
        PrefixSource = prefixSource;
        MapPath = mapPath;
        SettingsPath = settingsPath;
        Error = error;
    }

    public const string Usage = "Aufruf: TiaMcpPasswortverwaltung [--prefix <Präfix>] [--map <Pfad der passwords.json>] [--settings <Pfad der settings.json>]";

    /// <summary>The prefix the server accepts (<see cref="PasswordSources.AnyCredentialPrefix"/>: any target).</summary>
    public string Prefix { get; }

    /// <summary>German: where <see cref="Prefix"/> came from.</summary>
    public string PrefixSource { get; }

    public string? MapPath { get; }

    /// <summary>The server settings file (access mode) the program reads and writes.</summary>
    public string? SettingsPath { get; }

    /// <summary>The refusal (German), or null.</summary>
    public string? Error { get; }

    public static PasswordManagerOptions Parse(IReadOnlyList<string> args, Func<string, string?> readEnvironment, string? applicationData)
    {
        string? prefix = null;
        string? map = null;
        string? settings = null;
        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            string? Value(string name)
            {
                if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return argument.Substring(name.Length + 1);
                if (!string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)) return null;
                return i + 1 < args.Count ? args[++i] : "";
            }

            if (Value("--prefix") is { } p) prefix = p;
            else if (Value("--map") is { } m) map = m;
            else if (Value("--settings") is { } s) settings = s;
            else return Failed($"Unbekanntes Argument. {Usage}");
        }

        var source = "Standard";
        if (prefix is not null) source = "Argument --prefix";
        else if (!string.IsNullOrEmpty(readEnvironment(PasswordSources.CredentialPrefixVariable)))
        {
            prefix = readEnvironment(PasswordSources.CredentialPrefixVariable);
            source = "Umgebungsvariable " + PasswordSources.CredentialPrefixVariable;
        }

        if (prefix is not null && prefix.Length == 0) return Failed("--prefix braucht einen Wert.");
        if (PasswordSources.CredentialPrefixError(prefix) is { } prefixError) return Failed(prefixError + ".");
        if (map is not null && string.IsNullOrWhiteSpace(map)) return Failed("--map braucht einen Pfad.");
        var path = map ?? PasswordMapFile.TargetPath(readEnvironment, applicationData);
        if (path is null) return Failed("Der Ordner %APPDATA% ist nicht bekannt; bitte --map angeben.");
        if (settings is not null && string.IsNullOrWhiteSpace(settings)) return Failed("--settings braucht einen Pfad.");
        var settingsPath = settings ?? ServerSettingsFile.TargetPath(readEnvironment, applicationData);
        if (settingsPath is null) return Failed("Der Ordner %APPDATA% ist nicht bekannt; bitte --settings angeben.");
        return new PasswordManagerOptions(prefix ?? PasswordSources.CredentialTargetPrefix, source, path, settingsPath, null);
    }

    private static PasswordManagerOptions Failed(string error) => new(PasswordSources.CredentialTargetPrefix, "Standard", null, null, error);
}
