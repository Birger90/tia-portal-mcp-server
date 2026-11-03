using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Cli;

public sealed record DoctorCliOptions(
    bool Valid,
    bool Json,
    bool Verbose,
    bool Help,
    string? ProjectPath,
    McpAccessMode AccessMode,
    string? ParseError)
{
    public WorkerTimeoutParseResult? WorkerTimeout { get; init; }

    /// <summary>The resolved access mode with its source and the settings file read (same precedence as the server).</summary>
    public AccessModeParseResult? AccessModeResolution { get; init; }

    /// <summary>The PLC Web API target file the server would load (option, environment or none).</summary>
    public PlcWebApi.PlcWebApiConfigOptionResult? PlcWebApiConfig { get; init; }
}

public static class DoctorCliParser
{
    private const string JsonFlag = "--json";
    private const string VerboseFlag = "--verbose";
    private const string HelpFlag = "--help";
    private const string ProjectFlag = "--project";
    private const string ProjectPrefix = "--project=";
    private const string AccessModeFlag = "--access-mode";
    private const string AccessModePrefix = "--access-mode=";
    private const string ReadOnlyFlag = "--read-only";
    private const string ReadWriteFlag = "--read-write";

    /// <param name="settings">The server settings file as the server would read it; null means none is considered.</param>
    public static DoctorCliOptions Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null, ServerSettingsReadResult? settings = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        bool json = args.Any(arg => string.Equals(arg, JsonFlag, StringComparison.OrdinalIgnoreCase));
        bool verbose = false;
        // Detected up front so a parse error result still reports it; DoctorCommand lets help win.
        bool help = args.Any(arg => string.Equals(arg, HelpFlag, StringComparison.OrdinalIgnoreCase));
        string? projectPath = null;

        var accessModeResult = AccessModeParser.Parse(args, getEnvironmentVariable, settings);
        if (!accessModeResult.IsValid)
        {
            return new DoctorCliOptions(
                false,
                json,
                verbose,
                help,
                null,
                default,
                accessModeResult.Error);
        }

        var accessMode = accessModeResult.Mode;
        var workerTimeout = WorkerTimeoutParser.Parse(args, getEnvironmentVariable);
        if (!workerTimeout.IsValid)
        {
            return new DoctorCliOptions(false, json, verbose, help, null, accessMode, workerTimeout.Error);
        }

        var plcWebApiConfig = PlcWebApi.PlcWebApiConfigOption.Parse(args, getEnvironmentVariable);
        if (!plcWebApiConfig.IsValid && plcWebApiConfig.Source == PlcWebApi.PlcWebApiConfigSource.Option
            && PlcWebApiOptionShapeError(args) is { } shapeError)
        {
            return new DoctorCliOptions(false, json, verbose, help, null, accessMode, shapeError);
        }

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (string.Equals(arg, JsonFlag, StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (string.Equals(arg, VerboseFlag, StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
                continue;
            }

            if (string.Equals(arg, HelpFlag, StringComparison.OrdinalIgnoreCase))
            {
                // Already detected up front; only skip it here.
                continue;
            }

            if (string.Equals(arg, ReadOnlyFlag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, ReadWriteFlag, StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(AccessModePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, AccessModeFlag, StringComparison.OrdinalIgnoreCase))
            {
                // AccessModeParser already validated the value above.
                i++;
                continue;
            }

            if (arg.StartsWith(WorkerTimeoutParser.Flag + "=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, WorkerTimeoutParser.Flag, StringComparison.OrdinalIgnoreCase))
            {
                // WorkerTimeoutParser already validated the value above.
                i++;
                continue;
            }

            if (arg.StartsWith(PlcWebApi.PlcWebApiConfigOption.Flag + "=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, PlcWebApi.PlcWebApiConfigOption.Flag, StringComparison.OrdinalIgnoreCase))
            {
                // PlcWebApiConfigOption already read the value above; a relative path is reported by the check.
                i++;
                continue;
            }

            if (string.Equals(arg, ProjectFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, accessMode, "--project requires a value.");
                }

                if (projectPath is not null)
                {
                    return new DoctorCliOptions(false, json, verbose, help, projectPath, accessMode, "--project may be specified only once.");
                }

                projectPath = args[++i];
                continue;
            }

            if (arg.StartsWith(ProjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = arg.Substring(ProjectPrefix.Length);
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, accessMode, "--project requires a value.");
                }


                if (projectPath is not null)
                {
                    return new DoctorCliOptions(false, json, verbose, help, projectPath, accessMode, "--project may be specified only once.");
                }

                projectPath = candidate;

                continue;
            }

            return new DoctorCliOptions(false, json, verbose, help, projectPath, accessMode, $"Unknown doctor argument: '{arg}'.");
        }

        return new DoctorCliOptions(true, json, verbose, help, projectPath, accessMode, null)
        {
            WorkerTimeout = workerTimeout,
            PlcWebApiConfig = plcWebApiConfig,
            AccessModeResolution = accessModeResult
        };
    }

    /// <summary>A missing or repeated <c>--plc-webapi-config</c> value is an argument error; a bad path is the check's finding.</summary>
    private static string? PlcWebApiOptionShapeError(string[] args)
        => StartupOptionReader.ReadSingle(args, PlcWebApi.PlcWebApiConfigOption.Flag).Error;
}
