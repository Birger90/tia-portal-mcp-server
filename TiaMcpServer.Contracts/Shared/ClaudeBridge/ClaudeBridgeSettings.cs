using System.Text.Json;

namespace TiaMcpServer.Contracts.Shared.ClaudeBridge;

/// <summary>
/// The "Claude bridge" settings (member <c>claudeBridge</c> of the server settings file, version 2; edited in the password
/// manager's tab "Zugriffsmodus"). The bridge (TiaMcpClaudeBridge.exe) starts one headless Claude run per request from the
/// TIA Portal Add-In. The command is an executable plus an argument list (each argument passed as one argument, no shell),
/// with the placeholders <c>{allowedTools}</c> (the allow-list joined by commas), <c>{prompt}</c> (the prompt as one
/// argument; without it the prompt goes to standard input), <c>{requestId}</c> and <c>{inboxRoot}</c>.
/// </summary>
public sealed class ClaudeBridgeSettings
{
    public const string Member = "claudeBridge";
    public const int MinTimeoutMinutes = 1;
    public const int MaxTimeoutMinutes = 240;
    public const int MaxArguments = 64;
    public const int MaxAllowedTools = 100;
    public const int MaxTextChars = 1000;

    /// <summary>The MCP server name the defaults assume (<c>claude mcp add -s user tia-portal ...</c>).</summary>
    public const string DefaultServerName = "tia-portal";

    /// <summary>Every tool of the TIA MCP server (13 in read-only, 32 in read-write/full); the server's access mode still decides
    /// which of them exist. No built-in Claude Code tool (shell, file editing, web) is in the default list.</summary>
    public static readonly IReadOnlyList<string> TiaToolNames = new[]
    {
        "archive_project", "bind_project", "browse_project_tree", "close_project", "compile_check", "create_project", "get_project_status",
        "hmi_read", "hmi_write", "library_read", "library_write", "network_read", "network_write", "open_project", "plc_download",
        "plc_online_read", "plc_online_write", "plc_read", "plc_station_upload", "plc_tia_online", "plc_write", "plcsim_read", "plcsim_test",
        "plcsim_write", "read_cross_references", "retrieve_project", "save_project", "save_project_as", "siemens_docs", "tia_inbox", "vci_read", "vci_write",
    };

    /// <summary>
    /// The default command: Claude Code CLI in print mode with JSON output, no built-in tools, only the TIA tools allowed and
    /// every other permission prompt denied. The flags are the ones <c>claude --help</c> of Claude Code 2.1.317 lists
    /// (<c>-p/--print</c>, <c>--output-format json</c>, <c>--tools ""</c>, <c>--allowedTools</c>, <c>--permission-prompts none</c>);
    /// they are to be verified against the CLI installed on the PC. The prompt is sent on standard input.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultArguments = new[]
    {
        "-p", "--output-format", "json", "--tools", "", "--allowedTools", "{allowedTools}", "--permission-prompts", "none",
    };

    public static readonly ClaudeBridgeSettings Default = new(false, "claude", DefaultArguments, 20,
        TiaToolNames.Select(name => "mcp__" + DefaultServerName + "__" + name).ToList());

    public ClaudeBridgeSettings(bool enabled, string command, IReadOnlyList<string> arguments, int timeoutMinutes, IReadOnlyList<string> allowedTools)
    {
        Enabled = enabled;
        Command = command;
        Arguments = arguments;
        TimeoutMinutes = timeoutMinutes;
        AllowedTools = allowedTools;
    }

    /// <summary>Off by default: the bridge does nothing until the owner switches it on.</summary>
    public bool Enabled { get; }

    public string Command { get; }

    public IReadOnlyList<string> Arguments { get; }

    public int TimeoutMinutes { get; }

    public IReadOnlyList<string> AllowedTools { get; }

    /// <summary>The first problem (English), or null when the settings are usable.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Command) || Command.Length > MaxTextChars || HasControl(Command)) return "\"command\" must be a non-empty program name or path without control characters.";
        if (Arguments.Count > MaxArguments) return $"\"arguments\" holds at most {MaxArguments} entries.";
        if (Arguments.Any(a => a is null || a.Length > MaxTextChars || HasControl(a))) return "\"arguments\" entries must be strings without control characters.";
        if (TimeoutMinutes is < MinTimeoutMinutes or > MaxTimeoutMinutes) return $"\"timeoutMinutes\" must be {MinTimeoutMinutes} to {MaxTimeoutMinutes}.";
        if (AllowedTools.Count > MaxAllowedTools) return $"\"allowedTools\" holds at most {MaxAllowedTools} entries.";
        if (AllowedTools.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 200 || HasControl(t) || t.Contains(","))) return "\"allowedTools\" entries must be non-empty tool names without commas or control characters.";
        return null;
    }

    private static bool HasControl(string text) => text.Any(char.IsControl);

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(Member);
        writer.WriteBoolean("enabled", Enabled);
        writer.WriteString("command", Command);
        writer.WriteStartArray("arguments");
        foreach (var argument in Arguments) writer.WriteStringValue(argument);
        writer.WriteEndArray();
        writer.WriteNumber("timeoutMinutes", TimeoutMinutes);
        writer.WriteStartArray("allowedTools");
        foreach (var tool in AllowedTools) writer.WriteStringValue(tool);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static readonly string[] Members = { "enabled", "command", "arguments", "timeoutMinutes", "allowedTools" };

    /// <exception cref="ServerSettingsException">The member is not a valid bridge object.</exception>
    internal static ClaudeBridgeSettings Parse(JsonElement element)
    {
        const string prefix = "The server settings file: \"" + Member + "\" ";
        if (element.ValueKind != JsonValueKind.Object) throw new ServerSettingsException(prefix + "must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (!Members.Contains(member.Name, StringComparer.Ordinal)) throw new ServerSettingsException(prefix + $"has an unknown member (allowed: {string.Join(", ", Members)}).");
            if (!seen.Add(member.Name)) throw new ServerSettingsException(prefix + $"repeats the member \"{member.Name}\".");
        }

        foreach (var name in Members)
            if (!seen.Contains(name)) throw new ServerSettingsException(prefix + $"needs the member \"{name}\".");

        var enabled = element.GetProperty("enabled");
        if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ServerSettingsException(prefix + "\"enabled\" must be true or false.");
        var command = element.GetProperty("command");
        if (command.ValueKind != JsonValueKind.String) throw new ServerSettingsException(prefix + "\"command\" must be a string.");
        var timeout = element.GetProperty("timeoutMinutes");
        if (timeout.ValueKind != JsonValueKind.Number || !timeout.TryGetInt32(out var minutes)) throw new ServerSettingsException(prefix + "\"timeoutMinutes\" must be a whole number.");
        var settings = new ClaudeBridgeSettings(enabled.GetBoolean(), command.GetString()!, Strings(element.GetProperty("arguments"), "arguments", prefix), minutes,
            Strings(element.GetProperty("allowedTools"), "allowedTools", prefix));
        if (settings.Problem() is { } problem) throw new ServerSettingsException(prefix + problem);
        return settings;
    }

    private static List<string> Strings(JsonElement array, string name, string prefix)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new ServerSettingsException(prefix + $"\"{name}\" must be an array of strings.");
        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new ServerSettingsException(prefix + $"\"{name}\" must be an array of strings.");
            values.Add(item.GetString()!);
        }

        return values;
    }
}

/// <summary>The German texts and the line format of the bridge section of the password manager (one argument or tool per line;
/// an empty argument is written as <c>""</c>).</summary>
public static class ClaudeBridgeEditorText
{
    public const string Title = "Claude-Brücke (Anfragen aus TIA Portal automatisch an Claude geben)";
    public const string EnabledLabel = "Brücke an (Standard: aus)";
    public const string CommandLabel = "Befehl (Programm):";
    public const string ArgumentsLabel = "Befehlsvorlage – Argumente, eines pro Zeile ({prompt}, {allowedTools}, {requestId}, {inboxRoot}; leeres Argument als \"\"):";
    public const string TimeoutLabel = "Zeitlimit je Anfrage (Minuten):";
    public const string AllowedToolsLabel = "Erlaubte Werkzeuge (eines pro Zeile; Standard: nur die Werkzeuge des TIA-MCP-Servers, keine Shell):";
    public const string AutostartLabel = "Brücke bei der Anmeldung starten (Autostart)";

    public const string Notice =
        "Die Brücke startet für jede neue Anfrage aus dem Add-In „Claude in TIA“ einen Claude-Lauf ohne Chat (Claude Code CLI, "
        + "„claude -p“). Voraussetzung: Claude Code ist auf diesem PC installiert und angemeldet, und der TIA-MCP-Server ist mit "
        + "„claude mcp add -s user tia-portal …“ eingetragen. Die Standard-Befehlsvorlage folgt „claude --help“ (Version 2.1.317) "
        + "und ist auf diesem PC noch nicht geprüft. Der Zugriffsmodus des Servers gilt auch für diese Läufe. Eine Änderung gilt "
        + "ab der nächsten Anfrage; ein Neustart ist nicht nötig.";

    public static string ArgumentsToText(IEnumerable<string> arguments)
        => string.Join("\r\n", arguments.Select(a => a.Length == 0 ? "\"\"" : a));

    public static IReadOnlyList<string> ArgumentsFromText(string text)
        => Lines(text).Select(line => line == "\"\"" ? string.Empty : line).ToList();

    public static string ToolsToText(IEnumerable<string> tools) => string.Join("\r\n", tools);

    public static IReadOnlyList<string> ToolsFromText(string text) => Lines(text).ToList();

    private static IEnumerable<string> Lines(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
}
