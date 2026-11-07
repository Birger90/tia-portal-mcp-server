using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Shared.ClaudeInbox;

namespace TiaMcpServer.Contracts.Shared.ClaudeBridge;

/// <summary>What one headless run returned.</summary>
public sealed class ClaudeRunResult
{
    public ClaudeRunResult(int? exitCode, string standardOutput, string standardError, bool timedOut, string? startError = null)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        TimedOut = timedOut;
        StartError = startError;
    }

    public int? ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public bool TimedOut { get; }

    /// <summary>Why the program could not be started (not found, access denied), or null.</summary>
    public string? StartError { get; }
}

/// <summary>Starts one process: the executable with its argument list (no shell), standard input, a working folder and a time limit.</summary>
public interface IClaudeProcessRunner
{
    Task<ClaudeRunResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? standardInput, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>The command line of one run, after the placeholders were filled in.</summary>
public sealed class ClaudeBridgeCommand
{
    public ClaudeBridgeCommand(string executable, IReadOnlyList<string> arguments, string? standardInput)
    {
        Executable = executable;
        Arguments = arguments;
        StandardInput = standardInput;
    }

    public string Executable { get; }

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The prompt, when no argument carries <c>{prompt}</c>; else null.</summary>
    public string? StandardInput { get; }

    public static ClaudeBridgeCommand Build(ClaudeBridgeSettings settings, string prompt, string requestId, string inboxRoot)
    {
        var usesPrompt = settings.Arguments.Any(a => a.Contains("{prompt}"));
        var tools = string.Join(",", settings.AllowedTools);
        var arguments = settings.Arguments.Select(a => a
            .Replace("{allowedTools}", tools)
            .Replace("{requestId}", requestId)
            .Replace("{inboxRoot}", inboxRoot)
            .Replace("{prompt}", prompt)).ToList();
        return new ClaudeBridgeCommand(settings.Command, arguments, usesPrompt ? null : prompt);
    }
}

/// <summary>The prompt of a headless run: the request, what to do, and how to finish (English; Claude answers in German).</summary>
public static class ClaudeBridgePrompt
{
    public static string Build(ClaudeRequest request, string serverName = ClaudeBridgeSettings.DefaultServerName)
    {
        var text = new StringBuilder();
        text.AppendLine("A TIA Portal user handed this request to you with the \"Claude in TIA\" Add-In. Nobody is watching this run:");
        text.AppendLine("do not ask questions; if something is unclear or not allowed, say so in the reply.");
        text.AppendLine();
        text.AppendLine($"Request id: {request.Id}");
        text.AppendLine($"Action: {request.Action} - {ClaudeActions.Instruction(request.Action)}");
        if (request.Text is { Length: > 0 }) text.AppendLine("Text from the user: " + request.Text);
        text.AppendLine("Project: " + (request.ProjectPath ?? "(not reported; use get_project_status)"));
        text.AppendLine($"Opened from: {request.Source}");
        text.AppendLine("Selected objects (address hints built from names; confirm them with the read tools before use):");
        foreach (var item in request.Selection)
        {
            var a = item.Address;
            var hints = new[]
            {
                ("plcName", a.PlcName), ("unitName", a.UnitName), ("blockPath", a.BlockPath), ("typePath", a.TypePath), ("tableName", a.TableName),
                ("folderPath", a.FolderPath), ("deviceName", a.DeviceName), ("deviceItemName", a.DeviceItemName), ("library", a.Library),
                ("libraryTypePath", a.LibraryTypePath), ("masterCopyName", a.MasterCopyName), ("masterCopyFolderPath", a.MasterCopyFolderPath),
            }.Where(h => h.Item2 is not null).Select(h => h.Item1 + "=" + h.Item2);
            text.AppendLine($"- {item.Kind} \"{item.Name}\" ({string.Join(", ", hints)}); parents: {string.Join(" < ", item.Chain.Skip(1).Where(c => c.Name is not null).Select(c => c.Name))}");
        }

        text.AppendLine();
        text.AppendLine($"Use only the tools of the MCP server \"{serverName}\". First call tia_inbox get_request with id {request.Id} for the full request.");
        text.AppendLine("If the bound project is not the request's project, use bind_project with projectPath (never open, close or save a project unless the request text asks for it).");
        text.AppendLine("The server's access mode decides what may change; never download to a PLC or go online unless the request text asks for exactly that.");
        text.AppendLine($"Finish with tia_inbox reply_request (id {request.Id}): the answer in German, plain text, at most a few screens, listing every change you made.");
        text.AppendLine("If you cannot do the task, still reply with status failed and the reason.");
        return text.ToString();
    }
}

/// <summary>The ids the bridge has started (file <c>%APPDATA%\TiaMcpServer\bridge\state.json</c>): an id in it never runs again.</summary>
public sealed class ClaudeBridgeState
{
    public const int MaxEntries = 5000;
    private readonly string _path;
    private readonly List<string> _started;

    private ClaudeBridgeState(string path, List<string> started)
    {
        _path = path;
        _started = started;
    }

    public IReadOnlyList<string> Started => _started;

    public bool HasStarted(string id) => _started.Contains(id, StringComparer.Ordinal);

    /// <summary>Loads the state; a missing file is empty, an unreadable or invalid one throws (the bridge then runs nothing rather than risk a repeat).</summary>
    public static ClaudeBridgeState Load(string path)
    {
        if (!File.Exists(path)) return new ClaudeBridgeState(path, new List<string>());
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.GetInt32() != 1
            || !root.TryGetProperty("started", out var started) || started.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The bridge state file is not a version-1 state file.");
        return new ClaudeBridgeState(path, started.EnumerateArray().Select(e => e.GetString() ?? throw new InvalidDataException("Invalid id.")).ToList());
    }

    /// <summary>Records <paramref name="id"/> and writes the file before the run starts (at most once per request).</summary>
    public void MarkStarted(string id)
    {
        if (HasStarted(id)) return;
        _started.Add(id);
        if (_started.Count > MaxEntries) _started.RemoveRange(0, _started.Count - MaxEntries);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("started");
            foreach (var started in _started) writer.WriteStringValue(started);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var temporary = Path.Combine(directory, ".state." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temporary, stream.ToArray());
        try
        {
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

/// <summary>
/// One pass of the bridge: every pending request the bridge has not started yet, oldest first, one at a time. A request is
/// recorded as started before its run (so it never runs twice, even after a crash); requests older than
/// <see cref="MaxRequestAge"/> or with a reply are skipped. When the run ends without a <c>tia_inbox</c> reply, the bridge
/// writes the CLI's final output (the <c>result</c> member of the JSON output, else the raw output) as the reply.
/// </summary>
public sealed class ClaudeBridgeEngine
{
    public static readonly TimeSpan MaxRequestAge = TimeSpan.FromHours(24);
    public const int MaxFallbackReplyChars = 20000;

    private readonly ClaudeInboxStore _store;
    private readonly ClaudeBridgeState _state;
    private readonly IClaudeProcessRunner _runner;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;
    private readonly string _workingDirectory;

    public ClaudeBridgeEngine(ClaudeInboxStore store, ClaudeBridgeState state, IClaudeProcessRunner runner, Action<string> log, Func<DateTime> utcNow, string workingDirectory)
    {
        _store = store;
        _state = state;
        _runner = runner;
        _log = log;
        _utcNow = utcNow;
        _workingDirectory = workingDirectory;
    }

    /// <summary>Runs every new pending request; returns how many runs were started.</summary>
    public async Task<int> ProcessPendingAsync(ClaudeBridgeSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Enabled) return 0;
        if (settings.Problem() is { } problem)
        {
            _log("Bridge settings refused: " + problem);
            return 0;
        }

        var started = 0;
        var queue = _store.Pending().Where(e => !_state.HasStarted(e.Id)).ToList();
        foreach (var entry in queue)
            if (entry.Request is not null && !TooOld(entry.Request))
                _store.WriteStatus(Status(entry.Id, ClaudeRequestStatus.Queued, null));

        foreach (var entry in queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Request is null)
            {
                _state.MarkStarted(entry.Id);
                _store.WriteStatus(Status(entry.Id, ClaudeRequestStatus.Skipped, "Invalid request file: " + entry.Error));
                _log($"{entry.Id}: skipped, invalid request file ({entry.Error}).");
                continue;
            }

            if (TooOld(entry.Request))
            {
                _state.MarkStarted(entry.Id);
                _store.WriteStatus(Status(entry.Id, ClaudeRequestStatus.Skipped, $"Older than {MaxRequestAge.TotalHours:0} hours; not started automatically. Ask Claude to look in TIA."));
                _log($"{entry.Id}: skipped, older than {MaxRequestAge.TotalHours:0} hours.");
                continue;
            }

            if (_store.ReadReply(entry.Id) is not null)
            {
                _state.MarkStarted(entry.Id);
                _store.WriteStatus(Status(entry.Id, ClaudeRequestStatus.Skipped, "Already answered before the bridge started it."));
                continue;
            }

            await RunOneAsync(settings, entry.Request, cancellationToken).ConfigureAwait(false);
            started++;
        }

        return started;
    }

    private async Task RunOneAsync(ClaudeBridgeSettings settings, ClaudeRequest request, CancellationToken cancellationToken)
    {
        _state.MarkStarted(request.Id);
        _store.WriteStatus(Status(request.Id, ClaudeRequestStatus.Running, null));
        var command = ClaudeBridgeCommand.Build(settings, ClaudeBridgePrompt.Build(request), request.Id, _store.Root);
        _log($"{request.Id}: starting {command.Executable} ({command.Arguments.Count} arguments, timeout {settings.TimeoutMinutes} min).");
        var result = await _runner.RunAsync(command.Executable, command.Arguments, command.StandardInput, _workingDirectory,
            TimeSpan.FromMinutes(settings.TimeoutMinutes), cancellationToken).ConfigureAwait(false);

        string state;
        string message;
        if (result.StartError is not null)
        {
            state = ClaudeRequestStatus.Failed;
            message = $"The command could not be started: {result.StartError}";
        }
        else if (result.TimedOut)
        {
            state = ClaudeRequestStatus.TimedOut;
            message = $"The run was stopped after {settings.TimeoutMinutes} minutes.";
        }
        else if (result.ExitCode != 0)
        {
            state = ClaudeRequestStatus.Failed;
            message = $"The command ended with exit code {result.ExitCode}.";
        }
        else
        {
            state = ClaudeRequestStatus.Finished;
            message = "The run ended normally.";
        }

        _log($"{request.Id}: {state}. {message}");
        if (_store.ReadReply(request.Id) is null)
        {
            var (output, isError) = FinalOutput(result.StandardOutput);
            var text = state == ClaudeRequestStatus.Finished && !isError && output.Length > 0
                ? "Claude hat ohne Antwort über tia_inbox geendet. Letzte Ausgabe:\n\n" + output
                : "Die Claude-Brücke konnte die Anfrage nicht erledigen. " + message
                  + (output.Length > 0 ? "\n\nLetzte Ausgabe:\n" + output : "")
                  + (result.StandardError.Trim().Length > 0 ? "\n\nFehlerausgabe:\n" + Truncate(result.StandardError.Trim(), 4000) : "");
            var status = state == ClaudeRequestStatus.Finished && !isError ? ClaudeReply.StatusDone : ClaudeReply.StatusFailed;
            try
            {
                _store.WriteReply(request.Id, status, Truncate(text, ClaudeInboxContract.MaxReplyChars), ClaudeReply.FromBridge, _utcNow());
            }
            catch (ClaudeInboxException exception) when (exception.Category == "already_answered")
            {
            }
        }

        _store.WriteStatus(Status(request.Id, state, message));
    }

    /// <summary>The <c>result</c> text of Claude Code's <c>--output-format json</c> output (and its <c>is_error</c> flag), else the raw output.</summary>
    public static (string Text, bool IsError) FinalOutput(string standardOutput)
    {
        var trimmed = (standardOutput ?? string.Empty).Trim();
        if (trimmed.Length == 0) return (string.Empty, false);
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                var isError = document.RootElement.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
                return (Truncate(result.GetString()!.Trim(), MaxFallbackReplyChars), isError);
            }
        }
        catch (JsonException)
        {
        }

        return (Truncate(trimmed, MaxFallbackReplyChars), false);
    }

    private bool TooOld(ClaudeRequest request)
        => DateTime.TryParse(request.CreatedUtc, System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var created)
           && _utcNow() - created > MaxRequestAge;

    private ClaudeRequestStatus Status(string id, string state, string? message) => new(id, state, ClaudeInboxContract.FormatTime(_utcNow()), message);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max) + "…";
}

/// <summary>The autostart entry of the bridge: a <c>.cmd</c> in the user's Startup folder (no <c>.lnk</c>: K44).</summary>
public static class ClaudeBridgeAutostart
{
    public const string FileName = "TiaMcp Claude-Bruecke.cmd";
    public const string ExecutableName = "TiaMcpClaudeBridge.exe";

    /// <summary>Where the install script puts the bridge: <c>%LOCALAPPDATA%\TiaMcpServer\claude-bridge\TiaMcpClaudeBridge.exe</c>.</summary>
    public static string DefaultExecutable(string localApplicationData)
        => Path.Combine(localApplicationData, "TiaMcpServer", "claude-bridge", ExecutableName);

    public static string Content(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(new[] { '"', '\r', '\n', '%', '&', '|', '<', '>', '^' }) >= 0)
            throw new ArgumentException("The bridge path must not contain quotes, line breaks or cmd special characters.", nameof(executable));
        return "@echo off\r\nrem Starts the TiaMcp Claude bridge at logon (written by TiaMcp Passwortverwaltung).\r\nstart \"\" /min \"" + executable + "\"\r\n";
    }

    public static string? Problem(string executable, Func<string, bool> exists)
        => exists(executable) ? null : $"Die Claude-Brücke ist nicht installiert ({executable}). Bitte scripts\\install-claude-in-tia.ps1 -InstallBridge ausführen.";
}
