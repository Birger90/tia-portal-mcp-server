using System.Text;
using TiaMcpServer.Contracts.Files;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Contracts.Shared.PasswordManagement;

/// <summary>One choice of the access mode section of the password manager (German texts).</summary>
public sealed class AccessModeChoice
{
    internal AccessModeChoice(McpAccessMode mode, string label, string explanation)
    {
        Mode = mode;
        Label = label;
        Explanation = explanation;
    }

    public McpAccessMode Mode { get; }

    /// <summary>The radio button text, with the canonical name in parentheses.</summary>
    public string Label { get; }

    /// <summary>One line explaining what the server allows in this mode.</summary>
    public string Explanation { get; }
}

/// <summary>
/// The non-UI logic of the "Zugriffsmodus" section of the TiaMcp password manager: it reads the server settings file
/// (<see cref="ServerSettingsFile"/>), describes the current value and writes a new one. The password manager is the only
/// writer of that file; the server reads it once at startup. A file the server would refuse is never overwritten: the owner
/// fixes or deletes it first.
/// </summary>
public sealed class ServerSettingsEditor
{
    public const string SectionTitle = "Zugriffsmodus";

    /// <summary>The three modes in the order the section shows them.</summary>
    public static readonly IReadOnlyList<AccessModeChoice> Choices = new[]
    {
        new AccessModeChoice(McpAccessMode.ReadOnly, "Nur lesen (read-only)",
            "Der Server liest nur: er ändert, öffnet, speichert oder schließt kein Projekt und schreibt nie in eine PLC oder Simulation."),
        new AccessModeChoice(McpAccessMode.ReadWrite, "Lesen und schreiben, mit Rückfrage (read-write)",
            "Änderungen im Projekt sind erlaubt; jeder Lebenszyklus-, Lade- (Download) und Online-Aufruf wird vorher einmal bestätigt."),
        new AccessModeChoice(McpAccessMode.Full, "Voll, ohne Rückfrage (full)",
            "Wie read-write, aber Lebenszyklus-, Lade- (Download) und Online-Aufrufe laufen ohne Rückfrage."),
    };

    /// <summary>The notice shown under the choices: what takes precedence and when a change takes effect.</summary>
    public const string PrecedenceNotice =
        "Ein Argument --access-mode (oder --read-only / --read-write) oder die Umgebungsvariable TIA_MCP_ACCESS_MODE in der "
        + "MCP-Client-Konfiguration (z. B. claude_desktop_config.json) hat Vorrang vor dieser Einstellung; dort dann \"args\" "
        + "bzw. \"env\" ohne Zugriffsmodus angeben. Eine Änderung gilt erst nach einem Neustart des MCP-Servers: "
        + "Claude Desktop vollständig beenden und neu starten.";

    /// <summary>The text for "no setting".</summary>
    public const string NotSetText = "nicht gesetzt – Standard read-write";

    private ServerSettingsEditor(string path, ServerSettings? current, bool fileExists, string? loadError)
    {
        Path = path;
        Current = current;
        FileExists = fileExists;
        LoadError = loadError;
    }

    /// <summary>The settings file this editor reads and writes.</summary>
    public string Path { get; }

    /// <summary>The settings in the file (<see cref="ServerSettings.Empty"/> when there is none), or null when it was refused.</summary>
    public ServerSettings? Current { get; private set; }

    public bool FileExists { get; private set; }

    /// <summary>Why the file cannot be used (German, with the parser's reason); saving is refused while it is set.</summary>
    public string? LoadError { get; }

    public bool CanSave => LoadError is null;

    /// <summary>The access mode in the file, or null when it is not set (or the file was refused).</summary>
    public McpAccessMode? CurrentMode => Current?.AccessMode;

    /// <summary>The German description of the current value, for example <c>Voll, ohne Rückfrage (full)</c>.</summary>
    public string CurrentDescription
        => LoadError is not null ? "Datei ungültig – der Server startet so nicht"
            : CurrentMode is { } mode ? LabelOf(mode)
            : NotSetText;

    /// <summary>The radio button label of a mode.</summary>
    public static string LabelOf(McpAccessMode mode) => Choices.First(choice => choice.Mode == mode).Label;

    /// <summary>Reads <paramref name="path"/>; a missing file is "not set", an unreadable or invalid one sets <see cref="LoadError"/>.</summary>
    public static ServerSettingsEditor Load(string path, Func<string, bool> exists, Func<string, string> readAllText)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Kein Pfad für die Einstellungsdatei.", nameof(path));
        if (!exists(path)) return new ServerSettingsEditor(path, ServerSettings.Empty, false, null);
        string text;
        try
        {
            text = readAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ServerSettingsEditor(path, null, true, $"Die Einstellungsdatei kann nicht gelesen werden ({exception.GetType().Name}).");
        }

        try
        {
            return new ServerSettingsEditor(path, ServerSettingsFile.Parse(text), true, null);
        }
        catch (ServerSettingsException exception)
        {
            return new ServerSettingsEditor(path, null, true,
                "Die Einstellungsdatei ist ungültig und wird nicht überschrieben; der MCP-Server startet mit ihr nicht. "
                + "Bitte korrigieren oder löschen und das Programm neu starten. Grund: " + exception.Message);
        }
    }

    /// <summary>Reads <paramref name="path"/> from the file system.</summary>
    public static ServerSettingsEditor Load(string path) => Load(path, File.Exists, File.ReadAllText);

    /// <summary>
    /// Stores <paramref name="mode"/> (null: remove the setting, the server then uses its default read-write) and returns
    /// the refusal (German), or null when the file was written.
    /// </summary>
    public string? Save(McpAccessMode? mode)
    {
        if (LoadError is not null) return "Die Einstellungsdatei ist ungültig und wird nicht überschrieben. Bitte korrigieren oder löschen und das Programm neu starten.";
        if (mode is { } value && !Choices.Any(choice => choice.Mode == value)) return "Unbekannter Zugriffsmodus.";
        var settings = new ServerSettings(mode);
        try
        {
            WriteFile(Path, settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ServerSettingsException)
        {
            return $"Die Einstellungsdatei konnte nicht geschrieben werden: {exception.Message}";
        }

        Current = settings;
        FileExists = true;
        return null;
    }

    /// <summary>The German status line after a successful <see cref="Save"/>.</summary>
    public string SavedMessage()
        => (CurrentMode is { } mode ? "Gespeichert: " + LabelOf(mode) : "Einstellung entfernt: " + NotSetText)
           + ". Gilt nach einem Neustart des MCP-Servers (Claude Desktop neu starten).";

    /// <summary>
    /// Writes the settings to <paramref name="path"/> through a temporary file in the same folder, so a reader sees the old
    /// or the new file, never half of one; the temporary file is removed when the write fails. Device paths are refused
    /// (they bypass path normalization); UNC shares are allowed. Only the password manager calls this.
    /// </summary>
    public static void WriteFile(string path, ServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new IOException("Kein Pfad für die Einstellungsdatei.");
        if (LocalFilePaths.IsDevicePath(path)) throw new IOException("Gerätepfade (\\\\?\\, \\\\.\\, \\??\\) werden für die Einstellungsdatei nicht angenommen.");
        var json = ServerSettingsFile.Serialize(settings);
        var full = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(full)) File.Replace(temporary, full, destinationBackupFileName: null);
            else File.Move(temporary, full);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
