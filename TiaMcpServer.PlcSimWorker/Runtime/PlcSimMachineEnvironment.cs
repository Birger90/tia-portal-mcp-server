using Microsoft.Win32;
using TiaMcpServer.Contracts.PlcSim;

namespace TiaMcpServer.PlcSimWorker.Runtime;

/// <summary>Feeds <see cref="PlcSimApiLocator"/> from this machine's environment, registry and file system.</summary>
internal static class PlcSimMachineEnvironment
{
    public static PlcSimApiLocationInfo Locate()
        => PlcSimApiLocator.Locate(
            Environment.GetEnvironmentVariable(PlcSimApiLocator.EnvironmentVariable),
            ReadRegistryPath,
            File.Exists,
            Directory.Exists,
            directory => Directory.GetDirectories(directory).Select(path => Path.GetFileName(path)));

    private static string? ReadRegistryPath()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return null;

        // The key lives under Wow6432Node; the 64-bit view with the explicit path reads exactly it.
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(PlcSimApiLocator.RegistryKeyPath);
        return key?.GetValue(PlcSimApiLocator.RegistryValueName) as string;
    }
}
