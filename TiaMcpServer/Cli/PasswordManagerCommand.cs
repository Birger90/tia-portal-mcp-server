using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TiaMcpServer.Cli;

/// <summary>
/// <c>tia-mcp passwords [--prefix &lt;prefix&gt;] [--map &lt;path&gt;] [--settings &lt;path&gt;]</c>: starts the Windows password
/// manager ("TiaMcp Passwortverwaltung") that ships next to the server in <c>password-manager\</c>; it also sets the
/// server's access mode (tab "Zugriffsmodus"). The arguments go to it unchanged; it takes no password on its command line
/// (the user types passwords in its window).
/// </summary>
public static class PasswordManagerCommand
{
    public const string FolderName = "password-manager";
    public const string ExecutableName = "TiaMcpPasswortverwaltung.exe";

    public const string UsageText = """
        Usage: tia-mcp passwords [--prefix <credential prefix>] [--map <path of passwords.json>] [--settings <path of settings.json>]
        Starts the Windows password manager "TiaMcp Passwortverwaltung" (password-manager\TiaMcpPasswortverwaltung.exe).
        Passwords are typed in its window only and stored as Windows generic credentials; the map file holds names only.
        Its tab "Zugriffsmodus" sets the server's access mode (read-only, read-write, full) in the settings file; a
        --access-mode argument or TIA_MCP_ACCESS_MODE in the MCP client configuration takes precedence, and a change
        takes effect when the MCP server restarts.
        --prefix    the credential prefix the server accepts (default: TIA_MCP_CREDENTIAL_PREFIX, else TiaMcp/)
        --map       the map file (default: TIA_MCP_PASSWORD_MAP, else %APPDATA%\TiaMcpServer\passwords.json)
        --settings  the server settings file (default: TIA_MCP_SETTINGS, else %APPDATA%\TiaMcpServer\settings.json)
        """;

    public static int Run(string[] args)
        => Run(args, Console.Out, Console.Error, AppContext.BaseDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows), File.Exists, Start);

    public static int Run(string[] args, TextWriter output, TextWriter error, string baseDirectory, bool isWindows, Func<string, bool> exists, Func<ProcessStartInfo, bool> start)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            output.WriteLine(UsageText);
            return 0;
        }

        if (!isWindows)
        {
            error.WriteLine("Error: the password manager uses Windows Credential Manager and runs on Windows only.");
            return 1;
        }

        var path = Path.Combine(baseDirectory, FolderName, ExecutableName);
        if (!exists(path))
        {
            error.WriteLine($"Error: the password manager is not installed next to the server (expected {path}). It is built on Windows with the server; rebuild or reinstall the tool.");
            return 1;
        }

        var info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var argument in args) info.ArgumentList.Add(argument);
        if (!start(info))
        {
            error.WriteLine("Error: the password manager could not be started.");
            return 1;
        }

        output.WriteLine("TiaMcp Passwortverwaltung started.");
        return 0;
    }

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
