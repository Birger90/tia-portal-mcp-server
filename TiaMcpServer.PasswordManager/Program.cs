using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.PasswordManagement;

namespace TiaMcpServer.PasswordManager;

internal static class Program
{
    /// <summary>
    /// Start and failure trace next to the program (and under %LOCALAPPDATA%\TiaMcpServer when that folder is not
    /// writable): an exception before or outside the window used to end the process without any message (live
    /// 2026-11-02: "nothing happens"). It holds no password: only the start line, exception types, messages and stacks.
    /// </summary>
    private static readonly string[] TracePaths =
    {
        Path.Combine(AppContext.BaseDirectory, "TiaMcpPasswortverwaltung-Protokoll.txt"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TiaMcpServer", "TiaMcpPasswortverwaltung-Protokoll.txt"),
    };

    [STAThread]
    private static int Main(string[] args)
    {
        Trace($"Start {DateTime.Now:yyyy-MM-dd HH:mm:ss}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64" : "32")} Bit, Ordner {AppContext.BaseDirectory}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(e.ExceptionObject as Exception, "Unbehandelter Fehler");
        Application.ThreadException += (_, e) => Fail(e.Exception, "Fehler im Fenster");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        try
        {
            ApplicationConfiguration.Initialize();
            var options = PasswordManagerOptions.Parse(args, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            if (options.Error is not null)
            {
                Trace("Aufruf abgelehnt: " + options.Error);
                MessageBox.Show(options.Error, MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            // The map parser checks credential targets against the process-wide prefix: use the one the server accepts.
            if (PasswordSources.ConfigureCredentialPrefix(name => name == PasswordSources.CredentialPrefixVariable ? options.Prefix : null) is { } prefixError)
            {
                Trace("Präfix abgelehnt: " + prefixError);
                MessageBox.Show(prefixError, MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            Application.Run(new MainForm(options, new WindowsCredentialStore()));
            Trace("Beendet");
            return 0;
        }
        catch (Exception exception)
        {
            Fail(exception, "Fehler beim Start");
            return 1;
        }
    }

    private static void Fail(Exception? exception, string what)
    {
        Trace($"{what}: {exception}");
        try
        {
            MessageBox.Show($"{what}: {exception?.GetType().Name}: {exception?.Message}\r\n\nDetails stehen in:\r\n{string.Join("\r\n", TracePaths)}",
                MainForm.Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception)
        {
            // The trace file is the record when no window can be shown.
        }
    }

    private static void Trace(string line)
    {
        foreach (var path in TracePaths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
                return;
            }
            catch (Exception)
            {
                // Try the next location.
            }
        }
    }
}
