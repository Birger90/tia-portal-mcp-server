using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// What the re-export after a network edit must show: the network count, and for insert/replace the network at
/// <see cref="Position"/> with <see cref="Language"/>; <see cref="Code"/> is the submitted body in comparison form
/// (lines trimmed, blank lines dropped), reported against the re-export as information (the export form of an
/// instruction can differ from its import form). <see cref="Remaining"/> (delete) lists the bodies of the networks
/// that stay, in order.
/// </summary>
internal sealed record PlcSdNetworkExpectation(
    string Operation,
    int Position,
    int ExpectedCount,
    string? Language,
    string? Code,
    IReadOnlyList<string>? Remaining);

/// <summary>
/// Plans insert_network, replace_network and delete_network host-side on the block's get_block_content sd export (or an
/// earlier planned document of the same call), with the network edits of <see cref="SimaticSdDocument"/> (the Siemens
/// V21 skill simatic-sd). The mutation imports exactly that bundle through update_block_logic format sd against the
/// export's hash; the .s7res is never changed. Structural refusals only: the caller's code or the numbers would corrupt
/// the document; whether TIA Portal accepts the LAD/FBD/SCL is its import's and compile's decision. Not live-tested.
/// </summary>
internal static class PlcSdNetworkEditPlanner
{
    public static async Task<PlcEditResolution> ResolveAsync(
        OpennessWorkerClient client,
        PlcOperationRequest item,
        PlcWriteEffect effect,
        string? dependsOn,
        Dictionary<string, (string Content, string OperationId)> plannedDocuments,
        PlcContentDiffBudget budget,
        PlcSafetyPlanning? safety)
    {
        var path = effect.Target.BlockPath ?? item.BlockPath!;
        var key = "sd:" + path.ToUpperInvariant();
        var guards = new List<FiredGuard>
        {
            new(PlcGuardDefinitions.ReimportsBlockForText, item.OperationId,
                $"Operation '{item.OperationId}': {item.Operation} re-imports the whole block '{path}' from its SIMATIC SD documents (ImportFromDocuments with "
                + "ImportDocumentOptions.Override); TIA Portal can mark its instance DB and its callers inconsistent (reported by BetterTIA, not verified). "
                + "Run compile_check afterwards."),
        };

        string baseContent;
        string? baseHash = null;
        var preconditions = new List<CheckedPrecondition>();
        if (plannedDocuments.TryGetValue(key, out var prior))
        {
            baseContent = prior.Content;
        }
        else if (dependsOn is not null)
        {
            return Deferred(effect, guards);
        }
        else
        {
            var export = await client.GetBlockContentAsync(item.BlockPath!, format: SourceFormatNames.Sd).ConfigureAwait(false);
            if (!export.Success && export.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
                return Fail(effect, export.FailureCategory!, $"Operation '{item.OperationId}': {export.Error}");
            if (!export.Success || string.IsNullOrEmpty(export.Payload))
                return Unverifiable(item, effect, guards, $"the SIMATIC SD documents could not be exported ({(export.Success ? "the export was empty" : export.Error)})");
            baseContent = export.Payload;
            baseHash = ContentHashes.Compute(SourceFormatNames.Sd, baseContent);
            if (item.ExpectedContentHash is not null)
            {
                var check = ContentHashes.Check(item.ExpectedContentHash, SourceFormatNames.Sd, baseContent);
                if (!check.Success) return Fail(effect, check.ErrorCategory!, $"Operation '{item.OperationId}': {check.Message}");
                preconditions.Add(check.Evidence!);
            }
        }

        if (baseHash is null && item.ExpectedContentHash is not null)
            return Fail(effect, WorkerFailureCategories.ValidationError,
                $"Operation '{item.OperationId}': an earlier item of this call changes the same block, so expectedContentHash cannot describe the document this edit applies to. Omit it; the edit is re-planned against the real state before it runs.");
        if (!PlcBlockBundleText.TryGetDocument(baseContent, SimaticSdText.BundleExtension, out _, out var s7dcl))
            return Unverifiable(item, effect, guards, "the SIMATIC SD export carries no .s7dcl document");

        SimaticSdDocument document;
        string edited;
        PlcSdNetworkExpectation expectation;
        IReadOnlyList<PlcFieldChange> changes;
        int touched;
        try
        {
            document = SimaticSdDocument.Parse(s7dcl);
            (edited, expectation, changes, touched) = Apply(item, document);
        }
        catch (SimaticSdException exception)
        {
            return Fail(effect, exception.Category, $"Operation '{item.OperationId}': {exception.Message}");
        }

        if (document.IsFailSafe && safety is not null)
        {
            // A fail-safe block (header S7_Safety := "TRUE"): TIA Portal's login rule, never a stricter one, and the acceptance guard.
            var (safetyGuards, _) = await safety.FailSafeEditAsync(item, effect.Target, $"Operation '{item.OperationId}' ({item.Operation} of '{path}')").ConfigureAwait(false);
            guards.AddRange(safetyGuards);
        }

        // Networks the edit does not touch are re-imported in their export form.
        var untouched = SimaticSdDocument.Parse(edited).Networks.Where(n => n.Number != touched)
            .Select(n => (n.Number, Forms: SimaticSdExportForms.Find(n.Code))).Where(n => n.Forms.Count > 0).ToList();
        if (untouched.Count > 0)
            guards.Add(new FiredGuard(PlcGuardDefinitions.SdReimportsExportForm, item.OperationId,
                $"Operation '{item.OperationId}': the import of '{path}' re-imports {string.Join(", ", untouched.Select(n => $"network {n.Number} ({string.Join(" ", n.Forms)})"))} "
                + "in the form TIA Portal exported it; these forms are reported (1748477582/tia-portal-openness-mcp) to fail ImportFromDocuments "
                + "(reported, not reproduced live on V21 2026-11-02 for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF, Add; other instructions not tested). "
                + "Nothing is rewritten; if TIA Portal refuses the import, rewrite those networks in the import form (GT_Contact/LT_Contact... with S7_Templates, "
                + "P_Trig, Move( in := ..., out1 => ... )) with replace_network."));

        var content = PlcBlockBundleText.ReplaceDocument(baseContent, SimaticSdText.BundleExtension, edited);
        plannedDocuments[key] = (content, item.OperationId);
        effect = effect with { Changes = changes, ContentDiff = PlcContentDiff.Build(baseContent, content, budget) };
        return new PlcEditResolution(effect, guards, preconditions,
            baseHash is null ? null : new PlcPlannedContent(content, baseHash, false, null, Format: SourceFormatNames.Sd, Network: expectation), null);
    }

    /// <summary>The edited .s7dcl, what the re-export must show, the effect's changes and the touched network number (0 for a delete).</summary>
    /// <exception cref="SimaticSdException">The code or the numbers do not fit the document.</exception>
    internal static (string Edited, PlcSdNetworkExpectation Expectation, IReadOnlyList<PlcFieldChange> Changes, int Touched) Apply(PlcOperationRequest item, SimaticSdDocument document)
    {
        var count = document.Networks.Count;
        switch (item.Operation)
        {
            case SimaticSdMethods.InsertNetwork:
            {
                var code = SimaticSdText.NormalizeNetworkCode(item.Code!);
                var language = item.Language ?? code.DeclaredLanguage ?? SimaticSdText.DetectLanguage(string.Join("\n", code.BodyLines))
                    ?? throw new SimaticSdException(WorkerFailureCategories.ValidationError, "the network language cannot be detected from the code; give language.");
                var position = item.Position ?? count + 1;
                var edited = document.InsertNetwork(code, language, item.Position);
                return (edited, new(item.Operation, position, count + 1, language, code.Normalized, null),
                    new[] { new PlcFieldChange($"network {position}", null, $"inserted ({language}, {code.BodyLines.Count} line(s)); networks {count} -> {count + 1}") }, position);
            }
            case SimaticSdMethods.ReplaceNetwork:
            {
                var code = SimaticSdText.NormalizeNetworkCode(item.Code!);
                var number = item.NetworkNumber!.Value;
                var network = document.Network(number);
                var language = item.Language ?? code.DeclaredLanguage;
                var edited = document.ReplaceNetwork(number, code, language);
                if (edited == document.Render())
                    throw new SimaticSdException(WorkerFailureCategories.ValidationError,
                        $"replace_network changes nothing in network {number} (the code and language are already as requested); nothing is imported, because a re-import of an unchanged block can still leave its instance DB and callers inconsistent.");
                var changes = new List<PlcFieldChange> { new($"network {number} code", network.Code, string.Join("\n", code.BodyLines)) };
                if (language is not null && !string.Equals(language, network.Language, StringComparison.Ordinal))
                    changes.Add(new($"network {number} language", network.Language, language));
                return (edited, new(item.Operation, number, count, language ?? network.Language, code.Normalized, null), changes, number);
            }
            default:
            {
                var number = item.NetworkNumber!.Value;
                var network = document.Network(number);
                var edited = document.DeleteNetwork(number);
                var remaining = document.Networks.Where(n => n.Number != number).Select(n => SimaticSdText.NormalizeForComparison(n.BodyLines)).ToList();
                return (edited, new(item.Operation, number, count - 1, null, null, remaining),
                    new[] { new PlcFieldChange($"network {number}", $"{network.Language ?? "unknown language"}: {Summary(network)}", null) }, 0);
            }
        }
    }

    private static string Summary(SimaticSdNetwork network)
    {
        var first = network.BodyLines.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "(empty)";
        return network.BodyLines.Count > 1 ? $"{first} ... ({network.BodyLines.Count} lines)" : first;
    }

    private static PlcEditResolution Deferred(PlcWriteEffect effect, List<FiredGuard> guards)
        => new(effect, guards, Array.Empty<CheckedPrecondition>(), null, null);

    private static PlcEditResolution Unverifiable(PlcOperationRequest item, PlcWriteEffect effect, List<FiredGuard> guards, string reason)
        => new(effect, guards.Append(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
            $"Operation '{item.OperationId}': {reason}, so the edit cannot be planned.")).ToArray(), Array.Empty<CheckedPrecondition>(), null, null);

    private static PlcEditResolution Fail(PlcWriteEffect effect, string category, string message)
        => new(effect, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), null, new WriteToolError(category, message));
}
