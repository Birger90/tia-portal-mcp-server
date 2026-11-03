using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The settings family's own guards (informative only; the access mode is the control). Changes that lower or raise CPU protection
/// fire the CPU protection guards <see cref="LowersCpuProtection"/> / <see cref="RaisesCpuProtection"/>.
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string GeneratesWebBlocks = "plc_generates_web_blocks";

    /// <summary>A password policy or account locking write that is not weaker than the current values (security review F1).</summary>
    public const string ChangesPasswordPolicy = "plc_changes_password_policy";

    /// <summary>SysLogAutoAcceptClient=true: the CPU accepts syslog client certificates automatically (security review F8); the human confirms.</summary>
    public const string SyslogAutoAcceptsCertificates = "plc_syslog_auto_accepts_certificates";

    private static IEnumerable<WriteGuardDefinition> SettingsDefinitions() => new[]
    {
        new WriteGuardDefinition(GeneratesWebBlocks, WriteGuardSeverities.Info,
            "generate_web_blocks calls WebserverUserDefinedPages.GenerateBlocks: TIA Portal generates the web DBs (data blocks with the user-defined web pages) in the PLC program; with overwrite:true existing ones are overridden. Compile the PLC before a download."),
        new WriteGuardDefinition(ChangesPasswordPolicy, WriteGuardSeverities.Info,
            "The item changes a password policy (PLC, legacy PLC or UMAC) or the runtime account locking. Its values were compared with the current ones read in planning and none is weaker; a weaker, uncomparable or unreadable value fires plc_lowers_cpu_protection instead."),
    }.Concat(MoreSettingsDefinitions());
}
