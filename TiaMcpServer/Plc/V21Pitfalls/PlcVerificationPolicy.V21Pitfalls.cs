using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The content read-back of an update_block_logic that took the SIMATIC SD route with constructs the V21 import is
/// reported to alter: the re-export hash alone cannot show a silent alteration, so each listed construct is compared
/// with the re-exported .s7dcl (project types, nested indexes) and SimaticML (AT overlays). A construct that is
/// altered or cannot be compared makes the entry fail (not verified); the new content hash is still reported.
/// </summary>
public sealed partial class PlcVerificationPolicy
{
    /// <summary>Check name of the SIMATIC SD construct comparison.</summary>
    public const string S7dclConstructsCheck = "s7dclConstructs";

    private IReadOnlyDictionary<string, PlcV21PitfallPlan> _pitfalls = new Dictionary<string, PlcV21PitfallPlan>();

    private WriteOperationVerification? S7dclConstructsReadBack(PlcOperationRequest item, StructuredOperationFailure? failure,
        string reExport, string hash)
    {
        if (failure is not null || item.Operation != "update_block_logic" && !SimaticSdMethods.NetworkEdits.Contains(item.Operation, StringComparer.Ordinal)
            || !_pitfalls.TryGetValue(item.OperationId, out var plan) || plan.S7dclHazards.Count == 0)
            return null;

        var s7dcl = PlcBlockBundleText.TryGetDocument(reExport, ".s7dcl", out _, out var document) ? document : null;
        var simaticMl = PlcBlockBundleText.TryGetSimaticMl(reExport, out _, out var xml) ? xml : null;
        var checks = S7dclImportHazards.Compare(plan.S7dclHazards, s7dcl, simaticMl);
        _contentHashes[item.OperationId] = hash;
        var expected = string.Join("; ", checks.Select(c => $"{c.Hazard.Kind} {c.Hazard.Subject}: {c.Hazard.Evidence}"));
        var actual = string.Join("; ", checks.Select(c => $"{c.Hazard.Kind} {c.Hazard.Subject}: {c.State}{(c.Actual is null ? string.Empty : $" ({c.Actual})")}"));
        var different = checks.Where(c => c.State != S7dclImportHazards.Preserved).ToArray();
        if (different.Length == 0) return new(item.OperationId, true, S7dclConstructsCheck, expected, actual, null);
        return new(item.OperationId, false, S7dclConstructsCheck, expected, actual,
            $"Operation '{item.OperationId}': the re-export of '{item.BlockPath}' does not show "
            + string.Join(", ", different.Select(c => $"{c.Hazard.Kind} '{c.Hazard.Subject}' ({c.State})"))
            + " as submitted. TIA Portal V21's .s7dcl import is reported (rickgaiser/TiaMcp, not verified on V21) to alter these constructs "
            + "without an error. Inspect the block with plc_read get_block_content and write it through its SimaticML (.xml) document; "
            + "the current content hash is reported.");
    }
}
