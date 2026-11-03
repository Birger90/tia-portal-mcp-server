using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Vci;

/// <summary>
/// The one VCI export this server refuses to protect TIA Portal from a recorded crash (owner decision 2026-11-02):
/// a <c>SimaticSD</c> export of a PLC block that is fail-safe (its <c>ProgrammingLanguage</c> starts with <c>F_</c>) or lies
/// in a system block folder. Evidence: upstream's live VCI spike on V21 (Czarnak/tia-portal-mcp
/// <c>docs/superpowers/acceptance/reports/2026-10-15-vci-spike.md</c>, "Portal crashes") recorded 3 of 3 TIA Portal crashes
/// (<c>DomainEntityWrapper does not provide service IExportProvider</c>) for a <c>SimaticSD</c> export of an OB in a safety
/// group, with and without the safety login, while <c>SimaticML</c> of the same OB was refused without a crash; the
/// 2026-10-16 acceptance report reproduced it once (<c>portal_lost</c>). Which property of that OB causes the crash is not
/// known (upstream's hypothesis: a block VCI cannot export, possibly system-generated), so the rule covers fail-safe and
/// system-generated blocks; every other <c>SimaticSD</c> export, and every other format for these blocks, is left to TIA Portal.
/// </summary>
public static class VciSafetyExport
{
    public const string SimaticSd = "SimaticSD";

    /// <summary>The refusal text, or null when the export may run.</summary>
    public static string? Refusal(string? format, bool isPlcLeaf, IEnumerable<string>? pathNodeTypes, string? programmingLanguage, string leafName)
    {
        if (!isPlcLeaf || !string.Equals(format, SimaticSd, StringComparison.OrdinalIgnoreCase)) return null;
        var failSafe = PlcSafetyPolicy.IsFailSafeLanguage(programmingLanguage);
        var system = pathNodeTypes?.Any(type => string.Equals(type, ProjectTreeNodeTypes.SystemBlockFolder, StringComparison.Ordinal)) == true;
        if (!failSafe && !system) return null;
        return $"SimaticSD export of '{leafName}' refused: it is {(failSafe ? $"a fail-safe block ({programmingLanguage})" : "a block in a system block folder")}, "
            + "and a SimaticSD VCI export of a safety-group OB crashed TIA Portal V21 in 3 of 3 recorded attempts. Export it as SimaticML "
            + "(or SCL/DB where offered) instead. Nothing was exported for this leaf.";
    }
}
