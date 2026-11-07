using System.Diagnostics;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.ClaudeBridge;
using TiaMcpServer.Contracts.Shared.ClaudeInbox;

namespace TiaMcpServer.ClaudeBridge;

/// <summary>
/// <c>TiaMcpClaudeBridge.exe [--once]</c>: one instance per user (named mutex). Re-reads the settings file before every
/// pass, so switching the bridge on or off in the password manager needs no restart. A pass runs on every inbox change
/// (FileSystemWatcher) and every 30 seconds; requests run one at a time. Log: %LOCALAPPDATA%\TiaMcpServer\claude-bridge\bridge.log.
/// </summary>
public static class Program
{
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            Console.WriteLine("Usage: TiaMcpClaudeBridge [--once]\n  Starts one headless Claude run per new \"Claude in TIA\" request (settings: TiaMcp Passwortverwaltung, tab Zugriffsmodus).\n  --once  process the pending requests once and exit.");
            return 0;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = ClaudeInboxStore.DefaultRoot(Environment.GetEnvironmentVariable, appData) ?? throw new InvalidOperationException("No %APPDATA% folder.");
        var bridgeDirectory = Path.Combine(localAppData, "TiaMcpServer", "claude-bridge");
        Directory.CreateDirectory(bridgeDirectory);
        var log = new BridgeLog(Path.Combine(bridgeDirectory, "bridge.log"));
        using var mutex = new Mutex(initiallyOwned: true, @"Local\TiaMcpClaudeBridge", out var created);
        if (!created)
        {
            log.Write("Another bridge instance is running; this one exits.");
            return 0;
        }

        var store = new ClaudeInboxStore(root);
        Directory.CreateDirectory(store.InboxDirectory);
        var workingDirectory = Path.Combine(bridgeDirectory, "work");
        Directory.CreateDirectory(workingDirectory);
        ClaudeBridgeState state;
        try
        {
            state = ClaudeBridgeState.Load(Path.Combine(root, "bridge", "state.json"));
        }
        catch (Exception exception)
        {
            log.Write($"The state file cannot be read ({exception.GetType().Name}: {exception.Message}); the bridge runs nothing rather than risk running a request twice. Fix or delete {Path.Combine(root, "bridge", "state.json")}.");
            return 1;
        }

        var engine = new ClaudeBridgeEngine(store, state, new ProcessRunner(), log.Write, () => DateTime.UtcNow, workingDirectory);
        log.Write($"Bridge started (inbox {store.InboxDirectory}).");
        using var signal = new SemaphoreSlim(0);
        using var watcher = new FileSystemWatcher(store.InboxDirectory, "*.json") { EnableRaisingEvents = true };
        watcher.Created += (_, _) => signal.Release();
        watcher.Renamed += (_, _) => signal.Release();
        bool? lastEnabled = null;
        do
        {
            var settings = LoadSettings(appData, log);
            if (settings.Enabled != lastEnabled)
            {
                log.Write(settings.Enabled ? "Bridge is switched on." : "Bridge is switched off (TiaMcp Passwortverwaltung, tab Zugriffsmodus); requests wait for Claude.");
                lastEnabled = settings.Enabled;
            }

            try
            {
                await engine.ProcessPendingAsync(settings, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                log.Write($"Pass failed: {exception.GetType().Name}: {exception.Message}");
            }

            if (args.Contains("--once")) break;
            await signal.WaitAsync(RescanInterval).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false); // let the Add-In finish its move before reading.
            while (signal.CurrentCount > 0) signal.Wait(0);
        }
        while (true);

        return 0;
    }

    private static ClaudeBridgeSettings LoadSettings(string appData, BridgeLog log)
    {
        var read = ServerSettingsFile.Read(Environment.GetEnvironmentVariable, appData);
        if (!read.IsValid)
        {
            log.Write("Settings file refused, bridge stays off: " + read.Error);
            return ClaudeBridgeSettings.Default;
        }

        return read.Settings.ClaudeBridge ?? ClaudeBridgeSettings.Default;
    }
}

internal sealed class BridgeLog(string path)
{
    private const long MaxBytes = 1024 * 1024;
    private readonly object _gate = new();

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }
}

/// <summary>Starts the command without a shell (argument list), writes the prompt to standard input and kills the process tree on timeout.</summary>
internal sealed class ProcessRunner : IClaudeProcessRunner
{
    public async Task<ClaudeRunResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? standardInput, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned no process.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ClaudeRunResult(null, "", "", false, exception.Message);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            if (standardInput is not null) await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
            process.StandardInput.Close();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }

            return new ClaudeRunResult(timedOut ? null : process.ExitCode, await SafeRead(output), await SafeRead(error), timedOut);
        }
    }

    private static async Task<string> SafeRead(Task<string> read)
    {
        try { return await read.ConfigureAwait(false); }
        catch (Exception) { return ""; }
    }
}
