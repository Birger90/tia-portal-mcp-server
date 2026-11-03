using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The CPU protection guards: informative only (the access mode is the control), so nothing asks the user.</summary>
public static partial class PlcGuardDefinitions
{
    public const string LowersCpuProtection = "plc_lowers_cpu_protection";
    public const string RaisesCpuProtection = "plc_raises_cpu_protection";

    private static IEnumerable<WriteGuardDefinition> SecurityDefinitions() => new[]
    {
        new WriteGuardDefinition(LowersCpuProtection, WriteGuardSeverities.Info,
            "The operation removes CPU protection in the offline project (access level without password, a removed level password, or unencrypted confidential configuration data); it takes effect on the CPU only after a download. Do not use it for a real controller that needs protection."),
        new WriteGuardDefinition(RaisesCpuProtection, WriteGuardSeverities.Info,
            "The operation protects the CPU in the offline project (a protected access level, level passwords, or the confidential-data password) with passwords from password sources; after a download, online access and later downloads need them. Keep the sources available (environment variable or Windows credential, the server password map)."),
    };
}
