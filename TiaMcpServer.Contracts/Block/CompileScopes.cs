namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// The <c>scope</c> argument of <c>compile_check</c>. The object whose <c>ICompilable</c> service is called decides what
/// is compiled (Siemens openness_development skill <c>blocks/SKILL.md</c>, "Compile Scopes"): <c>PlcSoftware</c> compiles
/// the program only, the CPU <c>DeviceItem</c> the hardware configuration only (also used live by the IO-system
/// qualification probe, 2026-09-26). A download compiles both, so a program-only compile cannot show hardware errors
/// such as a protection level without a password.
/// </summary>
public static class CompileScopes
{
    /// <summary>Default: <c>PlcSoftware.GetService&lt;ICompilable&gt;()</c>, or the block with <c>blockPath</c>. Report scope plc or block.</summary>
    public const string Software = "software";

    /// <summary>The CPU device item hosting the PLC software: <c>DeviceItem.GetService&lt;ICompilable&gt;()</c>. Report scope hardware.</summary>
    public const string Hardware = "hardware";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Software, Hardware });

    /// <summary>The report <c>scope</c> of a hardware compile.</summary>
    public const string HardwareReportScope = "hardware";

    /// <summary>Null when valid; otherwise the refusal. Omitted means <see cref="Software"/>.</summary>
    public static string? Error(string? scope, string? blockPath)
    {
        if (scope is null) return null;
        if (!All.Contains(scope, StringComparer.Ordinal))
            return $"'scope' value '{scope}' is not supported. Valid values: {string.Join(", ", All)}.";
        if (scope == Hardware && blockPath is not null)
            return "'blockPath' compiles one program block and cannot be combined with scope 'hardware'; omit blockPath to compile the CPU hardware configuration.";
        return null;
    }
}
