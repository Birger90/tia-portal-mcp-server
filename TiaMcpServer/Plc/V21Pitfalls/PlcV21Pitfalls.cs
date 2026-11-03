using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// What plc_write planning found for one block content item about the V21 pitfalls reported by rickgaiser/TiaMcp
/// (roadmap row 22, not verified on V21): the SIMATIC SD import alterations and the textual-interface overwrite.
/// <see cref="S7dclHazards"/> is non-empty only when the bundle takes the SIMATIC SD route.
/// </summary>
internal sealed record PlcV21PitfallPlan(
    bool S7dclRoute,
    IReadOnlyList<S7dclImportHazard> S7dclHazards,
    bool OverlaysUnknown,
    bool TextualInterface,
    PlcBlockExportStateInfo? ExportState,
    IReadOnlyList<FiredGuard> Guards,
    IReadOnlyList<string> Warnings)
{
    public static PlcV21PitfallPlan None { get; } = new(false, Array.Empty<S7dclImportHazard>(), false, false, null,
        Array.Empty<FiredGuard>(), Array.Empty<string>());
}

internal static class PlcV21Pitfalls
{
    private const string Reported = "reported by rickgaiser/TiaMcp, not verified on V21";

    /// <summary>
    /// True when update_block_logic sends the bundle to <c>ImportFromDocuments</c>: it is delimited, has no .xml
    /// document and has an .s7dcl document (the worker's <c>BlockImportRouting.SelectRoute</c>).
    /// </summary>
    internal static bool UsesSimaticSdRoute(string content)
        => !PlcBlockBundleText.TryGetSimaticMl(content, out _, out _) && PlcBlockBundleText.TryGetDocument(content, ".s7dcl", out _, out _);

    /// <summary>True when the bundle's .s7dcl document declares a textual interface.</summary>
    internal static bool DeclaresTextualInterface(string? bundle)
        => bundle is not null && PlcBlockBundleText.TryGetDocument(bundle, ".s7dcl", out _, out var s7dcl)
            && S7dclImportHazards.HasTextualInterface(s7dcl);

    /// <summary>
    /// Plans one block overwrite: <paramref name="current"/> is the planning export in the write's format (null for an
    /// interface edit, whose <paramref name="submitted"/> is the planned bundle), <paramref name="submitted"/> what is imported.
    /// The worker's export ledger is read only when a textual interface is known or the write uses external source
    /// (whose text is not known to carry the editor mode); a failed read leaves the count unknown, never an error.
    /// </summary>
    internal static async Task<PlcV21PitfallPlan> PlanAsync(OpennessWorkerClient client, PlcOperationRequest item, string format,
        string? current, string submitted)
    {
        var xml = string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal);
        // format sd always takes the SIMATIC SD route (ImportFromDocuments); its planning export carries no SimaticML, so
        // AT overlays cannot be compared there.
        var sd = string.Equals(format, SourceFormatNames.Sd, StringComparison.Ordinal);
        var route = sd || xml && UsesSimaticSdRoute(submitted);
        IReadOnlyList<S7dclImportHazard> hazards = Array.Empty<S7dclImportHazard>();
        var overlaysUnknown = false;
        if (route)
        {
            PlcBlockBundleText.TryGetDocument(submitted, ".s7dcl", out _, out var s7dcl);
            var overlays = current is not null && PlcBlockBundleText.TryGetSimaticMl(current, out _, out var currentXml)
                ? S7dclImportHazards.AtOverlays(currentXml) : null;
            overlaysUnknown = overlays is null;
            hazards = S7dclImportHazards.Find(s7dcl, overlays);
        }

        var textual = (xml || sd) && (DeclaresTextualInterface(current) || DeclaresTextualInterface(submitted));
        PlcBlockExportStateInfo? state = null;
        string? stateError = null;
        if (textual || !xml && !sd)
        {
            (state, stateError) = await ReadExportStateAsync(client, item.BlockPath!).ConfigureAwait(false);
            textual |= state?.TextualInterface == true;
        }

        var guards = new List<FiredGuard>();
        var warnings = new List<string>();
        if (route && (hazards.Count > 0 || overlaysUnknown))
        {
            guards.Add(new FiredGuard(PlcGuardDefinitions.S7dclImportMayAlter, item.OperationId, S7dclGuardMessage(item, hazards, overlaysUnknown, sd)));
            // Live 2026-11-02 (K35): with no listed construct the warning claimed "with constructs the V21 import is reported to alter";
            // it now says only what is known.
            warnings.Add(hazards.Count > 0
                ? $"Operation '{item.OperationId}' imports '{item.BlockPath}' through SIMATIC SD (.s7dcl) with constructs the V21 import is "
                    + $"reported to alter ({string.Join(", ", hazards.Select(h => $"{h.Kind} '{h.Subject}'"))}; {Reported}); the SimaticML (.xml) route "
                    + "avoids them. The verification entry compares them with the re-export."
                : $"Operation '{item.OperationId}' imports '{item.BlockPath}' through SIMATIC SD (.s7dcl): no construct the V21 import is reported to "
                    + $"alter was found, but AT overlays could not be checked ({Reported}); the SimaticML (.xml) route avoids the question.");
        }

        if (textual)
        {
            var count = CountText(state, stateError);
            guards.Add(new FiredGuard(PlcGuardDefinitions.TextualInterfaceOverwrite, item.OperationId,
                $"Operation '{item.OperationId}': '{item.BlockPath}' has a textual interface (S7_EditorMode TextualInterface). TIA Portal V21 is "
                + $"reported ({Reported}) to terminate when it overwrites such a block after it was exported more than once since the project was "
                + $"opened or the block was last written. {count} Before its import this write exports the block again (worker hash check, and for a "
                + "bundle the unchanged-companion check), so the reported condition applies to every overwrite through plc_write: saving, closing "
                + "and reopening the project first resets the count but does not avoid it with this write path."));
            guards.Add(new FiredGuard(PlcGuardDefinitions.SaveBeforeWrite, item.OperationId,
                $"Operation '{item.OperationId}': save the project (save_project) before this call. TIA Portal is reported ({Reported}) to "
                + $"terminate when it overwrites '{item.BlockPath}', and a terminated TIA Portal loses every unsaved change."));
            warnings.Add($"Operation '{item.OperationId}' overwrites '{item.BlockPath}', which has a textual interface: TIA Portal V21 is reported "
                + $"({Reported}) to terminate on such an overwrite after more than one export since the project was opened or the block was last "
                + $"written. {count}");
        }

        return new PlcV21PitfallPlan(route, hazards, overlaysUnknown, textual, state, guards, warnings);
    }

    /// <summary>The worker's ledger entry of the block, or the reason it could not be read.</summary>
    internal static async Task<(PlcBlockExportStateInfo? State, string? Error)> ReadExportStateAsync(OpennessWorkerClient client, string blockPath)
    {
        var call = await client.GetBlockExportStateAsync(blockPath).ConfigureAwait(false);
        if (!call.Success) return (null, call.Error ?? call.FailureCategory ?? "the worker refused the request");
        try
        {
            return (CanonicalJson.DeserializeWorkerPayload<PlcBlockExportStateInfo>(call.Payload), null);
        }
        catch (JsonException)
        {
            return (null, "the worker's answer did not match its contract");
        }
    }

    private static string CountText(PlcBlockExportStateInfo? state, string? error)
    {
        if (state is null) return $"The export count could not be read ({error}).";
        if (state.ExportCount is not { } count)
            return "This server cannot tell how often it was exported: the project was already open when this server bound it, "
                + "or a VCI export or import ran since.";
        var since = state.Basis == PlcBlockExportStateMethods.WrittenByThisServer ? "it last wrote the block" : "it opened the project";
        return $"This server has exported it {count} time(s) since {since}, this plan's read included.";
    }

    private static string S7dclGuardMessage(PlcOperationRequest item, IReadOnlyList<S7dclImportHazard> hazards, bool overlaysUnknown, bool sdFormat)
    {
        var found = hazards.Count == 0 ? "no listed construct was found." : string.Join(" ", hazards.Select(h => h.Detail));
        // K35 (live 2026-11-02): for format sd the planning export is SIMATIC SD by design and carries no SimaticML; the former
        // text read as if the block itself had no readable document.
        var overlays = !overlaysUnknown
            ? string.Empty
            : sdFormat
                ? " AT overlays were not checked: this write's planning read is a SIMATIC SD export, which carries no SimaticML document to compare them with."
                : " AT overlays could not be checked: the planning read returned no SimaticML document for the block.";
        var why = sdFormat
            ? "this write uses the SIMATIC SD format (update_block_logic format sd, a network edit or create_block_from_document)"
            : "the bundle has no SimaticML (.xml) document";
        return $"Operation '{item.OperationId}': '{item.BlockPath}' is imported through SIMATIC SD (.s7dcl) because {why}. "
            + $"TIA Portal V21's .s7dcl import is reported ({Reported}) to alter some constructs without an error: {found}{overlays} "
            + "To avoid that route, write the block through its SimaticML document (plc_read get_block_content format xml, edit the .xml document, "
            + "submit the whole bundle); the verification compares the listed constructs with the re-export. A main safety block of an "
            + "F-runtime group is never imported through SIMATIC SD: the worker converts it to SimaticML through a temporary F-block.";
    }
}

/// <summary>The read-side flags of the V21 pitfalls on a get_block_content xml bundle (item warnings, the content is unchanged).</summary>
internal static class PlcV21PitfallReadWarnings
{
    internal static IReadOnlyList<string> For(string? blockPath, string bundle)
    {
        var warnings = new List<string>();
        if (string.IsNullOrEmpty(bundle) || !PlcBlockBundleText.TryGetDocument(bundle, ".s7dcl", out _, out var s7dcl)) return warnings;
        var overlays = PlcBlockBundleText.TryGetSimaticMl(bundle, out _, out var xml) ? S7dclImportHazards.AtOverlays(xml) : null;
        var hazards = S7dclImportHazards.Find(s7dcl, overlays);
        if (hazards.Count > 0)
            warnings.Add($"'{blockPath}': its .s7dcl document has constructs TIA Portal V21's .s7dcl import is reported (rickgaiser/TiaMcp, not "
                + $"verified on V21) to alter without an error ({string.Join(", ", hazards.Select(h => $"{h.Kind} '{h.Subject}'"))}). Writing it back "
                + "through the SIMATIC SD route (a bundle without the .xml document) can change them; update_block_logic with the bundle's SimaticML "
                + "(.xml) document avoids that route.");
        if (S7dclImportHazards.HasTextualInterface(s7dcl))
            warnings.Add($"'{blockPath}' has a textual interface (S7_EditorMode TextualInterface): TIA Portal V21 is reported (rickgaiser/TiaMcp, "
                + "not verified on V21) to terminate when it overwrites such a block after it was exported more than once since the project was "
                + "opened or the block was last written; this read is one such export.");
        return warnings;
    }
}
