using System.Globalization;

namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// The one resolution path for the S7-PLCSIM Advanced .NET API directory, shared by the worker
/// (which then loads the DLL) and <c>doctor</c> (which only reports). It is side-effect free: the
/// caller supplies the environment value, the registry value and file-system probes.
/// <list type="number">
/// <item><c>TIA_MCP_PLCSIM_API_DIR</c>, when set and nonblank, is the directory holding the DLL; the
/// registry is then not consulted. It must be fully qualified (<see cref="IsFullyQualified"/>); a
/// relative value is refused with a problem, never resolved against a current directory. A registry
/// <c>Path</c> that is not fully qualified is refused the same way.</item>
/// <item>Otherwise the <c>Path</c> value of <c>HKLM\SOFTWARE\Wow6432Node\Siemens\Shared Tools\PLCSIMADV_SimRT</c>
/// plus <c>API\&lt;highest version subfolder&gt;</c>. Subfolder names are compared as versions;
/// names that do not parse as a version (or a plain integer) are ignored. There is no fallback to a
/// lower version when the highest lacks the DLL.</item>
/// </list>
/// </summary>
public static class PlcSimApiLocator
{
    public const string EnvironmentVariable = "TIA_MCP_PLCSIM_API_DIR";
    public const string RegistryKeyPath = @"SOFTWARE\Wow6432Node\Siemens\Shared Tools\PLCSIMADV_SimRT";
    public const string RegistryKeyDisplay = @"HKEY_LOCAL_MACHINE\" + RegistryKeyPath;
    public const string RegistryValueName = "Path";
    public const string ApiFolderName = "API";
    public const string ApiDllName = "Siemens.Simatic.Simulation.Runtime.Api.x64.dll";

    public const string SourceEnvironment = "environment";
    public const string SourceRegistry = "registry";
    public const string SourceNone = "none";

    /// <summary>The <see cref="PlcSimApiLocationInfo.Problem"/> of a machine without PLCSIM Advanced (and without an override).</summary>
    public const string NotInstalledProblem =
        "S7-PLCSIM Advanced was not found: " + EnvironmentVariable + " is not set and the registry value "
        + RegistryKeyDisplay + "\\" + RegistryValueName + " is absent.";

    public static PlcSimApiLocationInfo Locate(
        string? environmentValue,
        Func<string?> readRegistryPath,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> subdirectoryNames)
    {
        var location = new PlcSimApiLocationInfo();
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            location.Source = SourceEnvironment;
            location.EnvironmentValue = environmentValue;
            var directory = environmentValue!.Trim();
            if (!IsFullyQualified(directory))
            {
                // Relative (or drive-relative) paths would resolve differently in doctor and in the
                // worker, and Assembly.LoadFile refuses them; both reject them the same way.
                location.Problem = $"{EnvironmentVariable} must be an absolute directory path; '{directory}' is relative. Set it to the full path of the folder holding {ApiDllName}.";
                return location;
            }

            location.ApiDirectory = directory;
            location.DllPath = Path.Combine(directory, ApiDllName);
            location.DllExists = fileExists(location.DllPath);
            if (!location.DllExists)
                location.Problem = directoryExists(directory)
                    ? $"{EnvironmentVariable} is set, but {ApiDllName} is not in '{directory}'."
                    : $"{EnvironmentVariable} is set, but the directory '{directory}' does not exist.";
            return location;
        }

        string? registryPath;
        try
        {
            registryPath = readRegistryPath();
        }
        catch (Exception exception)
        {
            location.Problem = $"The registry value {RegistryKeyDisplay}\\{RegistryValueName} could not be read ({exception.GetType().Name}: {exception.Message}).";
            return location;
        }

        if (string.IsNullOrWhiteSpace(registryPath))
        {
            location.Problem = NotInstalledProblem;
            return location;
        }

        location.Source = SourceRegistry;
        location.RegistryPath = registryPath;
        if (!IsFullyQualified(registryPath!.Trim()))
        {
            location.Problem = $"The registry value {RegistryKeyDisplay}\\{RegistryValueName} is not an absolute path ('{registryPath.Trim()}').";
            return location;
        }

        var apiRoot = Path.Combine(registryPath!.Trim(), ApiFolderName);
        if (!directoryExists(apiRoot))
        {
            location.Problem = $"The PLCSIM Advanced registry path exists, but its API folder '{apiRoot}' does not.";
            return location;
        }

        location.VersionDirectories = subdirectoryNames(apiRoot)
            .Select(name => name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var selected = location.VersionDirectories
            .Select(name => (Name: name, Version: ParseVersion(name)))
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (selected.Name is null)
        {
            location.Problem = location.VersionDirectories.Count == 0
                ? $"The PLCSIM Advanced API folder '{apiRoot}' has no version subfolder."
                : $"No subfolder of '{apiRoot}' is named as a version (found: {string.Join(", ", location.VersionDirectories)}).";
            return location;
        }

        location.SelectedVersion = selected.Name;
        location.ApiDirectory = Path.Combine(apiRoot, selected.Name);
        location.DllPath = Path.Combine(location.ApiDirectory, ApiDllName);
        location.DllExists = fileExists(location.DllPath);
        if (!location.DllExists)
            location.Problem = $"The highest API version folder '{location.ApiDirectory}' does not contain {ApiDllName}.";
        return location;
    }

    /// <summary>
    /// True for a fully qualified path: <c>X:\…</c> or <c>X:/…</c>, a UNC path (<c>\\server\share</c>), or
    /// an absolute path on a platform whose separator is <c>/</c>. Drive-relative (<c>C:dir</c>) and
    /// root-relative (<c>\dir</c>) Windows paths are not fully qualified.
    /// </summary>
    public static bool IsFullyQualified(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return true;
        if (path.Length >= 2 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/'))
            return true;
        return Path.DirectorySeparatorChar == '/' && path[0] == '/';
    }

    /// <summary>Parses a version folder name such as <c>6.0</c>, <c>5.0.1</c> or <c>6</c>; anything else is null.</summary>
    public static Version? ParseVersion(string name)
    {
        if (Version.TryParse(name, out var version))
            return version;
        return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            ? new Version(major, 0)
            : null;
    }
}
