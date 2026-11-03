using Microsoft.Win32;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.PlcSim;

namespace TiaMcpServer.Diagnostics.Checks;

/// <summary>
/// Reports where the S7-PLCSIM Advanced API would be loaded from (the worker's own resolution
/// rules), whether the DLL exists there, and whether the PLCSIM worker is present. It never loads
/// the DLL or starts a worker, so it stays fast and side-effect free. PLCSIM is optional: a machine
/// without it passes; a configured-but-broken location or a missing worker is a warning.
/// </summary>
public sealed class PlcSimAdvancedCheck : IDiagnosticCheck
{
    private readonly IApplicationInfoService _appInfo;
    private readonly IEnvironmentVariableService _env;
    private readonly IRegistryService _registry;
    private readonly IFileSystemService _fileSystem;
    private readonly Func<string, IEnumerable<string>> _subdirectoryNames;

    public PlcSimAdvancedCheck(
        IApplicationInfoService appInfo,
        IEnvironmentVariableService env,
        IRegistryService registry,
        IFileSystemService fileSystem,
        Func<string, IEnumerable<string>>? subdirectoryNames = null)
    {
        _appInfo = appInfo;
        _env = env;
        _registry = registry;
        _fileSystem = fileSystem;
        _subdirectoryNames = subdirectoryNames ?? (directory => Directory.Exists(directory)
            ? Directory.GetDirectories(directory).Select(path => Path.GetFileName(path))
            : Array.Empty<string>());
    }

    public string Id => "plcsim-advanced";

    public string Name => "S7-PLCSIM Advanced (optional)";

    public DiagnosticCheckResult Run()
    {
        var location = PlcSimApiLocator.Locate(
            _env.Get(PlcSimApiLocator.EnvironmentVariable),
            () => OperatingSystem.IsWindows()
                ? _registry.GetStringValue(RegistryHive.LocalMachine, RegistryView.Registry64,
                    PlcSimApiLocator.RegistryKeyPath, PlcSimApiLocator.RegistryValueName)
                : null,
            _fileSystem.FileExists,
            _fileSystem.DirectoryExists,
            _subdirectoryNames);
        var worker = PlcSimWorkerLocator.Locate(_appInfo.BaseDirectory, _fileSystem);

        var evidence = Evidence.Empty();
        evidence["apiSource"] = location.Source;
        evidence["environmentVariable"] = PlcSimApiLocator.EnvironmentVariable;
        evidence["environmentValue"] = location.EnvironmentValue;
        evidence["registryKey"] = PlcSimApiLocator.RegistryKeyDisplay;
        evidence["registryValue"] = location.RegistryPath;
        evidence["versionDirectories"] = location.VersionDirectories.Count == 0 ? null : string.Join(", ", location.VersionDirectories);
        evidence["selectedVersion"] = location.SelectedVersion;
        evidence["apiDirectory"] = location.ApiDirectory;
        evidence["dllPath"] = location.DllPath;
        evidence["dllExists"] = location.DllExists.ToString().ToLowerInvariant();
        evidence["problem"] = location.Problem;
        evidence["workerFound"] = worker.Found.ToString().ToLowerInvariant();
        evidence["workerPath"] = worker.SelectedPath;
        for (var index = 0; index < worker.Candidates.Count; index++)
        {
            evidence[$"workerCandidate[{index}].path"] = worker.Candidates[index].Path;
            evidence[$"workerCandidate[{index}].exists"] = worker.Candidates[index].Exists.ToString().ToLowerInvariant();
        }

        if (!worker.Found)
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Warning,
                "The PLCSIM worker executable was not found; plcsim_read, plcsim_write and plcsim_test cannot run.",
                "Rebuild the solution or reinstall the tia-mcp global tool; the plcsim-worker folder must be beside the MCP server executable.",
                evidence);

        if (location.DllExists)
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Passed,
                $"PLCSIM Advanced API found at {location.DllPath} (source: {location.Source}{(location.SelectedVersion is null ? string.Empty : $", version folder {location.SelectedVersion}")}). "
                + "doctor does not load it; run plcsim_read probe_api to verify the API surface.",
                null, evidence);

        if (location.Problem == PlcSimApiLocator.NotInstalledProblem)
            return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Passed,
                "S7-PLCSIM Advanced was not found. It is optional; plcsim_* tools fail with plcsim_api_unavailable until it is installed or "
                + $"{PlcSimApiLocator.EnvironmentVariable} points to its API directory.",
                null, evidence);

        return new DiagnosticCheckResult(Id, Name, DiagnosticStatus.Warning,
            location.Problem ?? "The PLCSIM Advanced API DLL was not found.",
            $"Repair the S7-PLCSIM Advanced installation, or set {PlcSimApiLocator.EnvironmentVariable} to the absolute path of the folder that contains {PlcSimApiLocator.ApiDllName}.",
            evidence);
    }
}
