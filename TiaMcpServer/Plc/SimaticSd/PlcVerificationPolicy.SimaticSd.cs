using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;

namespace TiaMcpServer.Plc;

/// <summary>
/// The read-backs of the SIMATIC SD writes. A network edit is re-exported in sd (its contentHash is reported, usable as
/// the next expectedContentHash) and checked structurally: the network count and, for insert/replace, a network at the
/// expected position in the expected language. The submitted code is compared with the re-exported network in comparison
/// form and reported as <c>codeMatchesExport</c> in the message, as information: V21 exports some instructions in a form
/// that differs from the import form (reported by 1748477582/tia-portal-openness-mcp, not verified), so a different text
/// is no failed import. create_block_from_document re-exports the created block in sd.
/// </summary>
public sealed partial class PlcVerificationPolicy
{
    public const string NetworkCodeCheck = "networkCode";
    public const string CreatedBlockCheck = "createdBlock";

    private const string ExportFormNote = "The export form of an instruction is reported to differ from the form that was imported (not reproduced live on V21 2026-11-02 for the instructions tested), and TIA Portal reformats whitespace, so a different text alone is no failed import.";

    private async Task<WriteOperationVerification> NetworkReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var export = await client.GetBlockContentAsync(item.BlockPath!, format: SourceFormatNames.Sd).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return failure is null
                ? Fail(item, NetworkCodeCheck, $"Operation '{item.OperationId}': the block could not be re-exported in sd ({(export.Success ? "the export was empty" : export.Error)}).")
                : new(item.OperationId, false, NetworkCodeCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the block could not be re-exported.");
        var hash = ContentHashes.Compute(SourceFormatNames.Sd, export.Payload);
        _contentHashes[item.OperationId] = hash;
        if (!_planned.TryGetValue(item.OperationId, out var planned) || planned.Network is not { } expected)
            return new(item.OperationId, false, NetworkCodeCheck, null, null,
                $"Operation '{item.OperationId}': no planned network state is known; the current content hash is reported.");
        var check = Check(expected, export.Payload);
        var constructs = failure is null ? S7dclConstructsReadBack(item, null, export.Payload, hash) : null;
        if (failure is not null)
            return new(item.OperationId, false, NetworkCodeCheck, check.Expected, check.Actual,
                $"Operation '{item.OperationId}' failed with {failure.Category}; the current networks and content hash are reported.");
        var success = check.Success && constructs?.Success != false;
        var message = string.Join(" ", new[]
        {
            check.Success ? null : $"Operation '{item.OperationId}': the re-exported block does not show the planned network structure ({check.Problem}). Inspect it with plc_read list_networks.",
            check.CodeNote,
            constructs?.Success == false ? constructs.Message : null,
            success ? "Run compile_check: the import can leave the instance DB and callers inconsistent until compiled." : null,
        }.Where(m => m is not null));
        return new(item.OperationId, success, NetworkCodeCheck, check.Expected, check.Actual, message.Length == 0 ? null : message);
    }

    /// <summary>The structural comparison of a re-export with the planned network state (Siemens-free, unit-tested).</summary>
    internal static (bool Success, string Expected, string Actual, string? Problem, string? CodeNote) Check(PlcSdNetworkExpectation expected, string reExport)
    {
        SimaticSdDocument document;
        try
        {
            document = PlcBlockBundleText.TryGetDocument(reExport, SimaticSdText.BundleExtension, out _, out var s7dcl)
                ? SimaticSdDocument.Parse(s7dcl)
                : throw new SimaticSdException("validation_error", "the re-export has no .s7dcl document");
        }
        catch (SimaticSdException exception)
        {
            return (false, Describe(expected.ExpectedCount, expected.Position, expected.Language, expected.Code), "(not readable)", exception.Message, null);
        }

        var count = document.Networks.Count;
        if (expected.Operation == SimaticSdMethods.DeleteNetwork)
        {
            var remaining = document.Networks.Select(n => SimaticSdText.NormalizeForComparison(n.BodyLines)).ToList();
            var same = expected.Remaining is not null && remaining.SequenceEqual(expected.Remaining, StringComparer.Ordinal);
            return (count == expected.ExpectedCount, $"networks={expected.ExpectedCount}", $"networks={count}",
                count == expected.ExpectedCount ? null : $"{count} network(s) instead of {expected.ExpectedCount}",
                $"remainingNetworksMatchExport: {(same ? "true" : "false")}." + (same ? string.Empty : " The remaining networks read back in a different text than they were exported before the delete. " + ExportFormNote));
        }

        var network = expected.Position <= count ? document.Networks[expected.Position - 1] : null;
        var exportCode = network is null ? null : SimaticSdText.NormalizeForComparison(network.BodyLines);
        var languageOk = network is not null && (expected.Language is null || string.Equals(network.Language, expected.Language, StringComparison.OrdinalIgnoreCase));
        var problems = new List<string>();
        if (count != expected.ExpectedCount) problems.Add($"{count} network(s) instead of {expected.ExpectedCount}");
        if (network is null) problems.Add($"no network {expected.Position}");
        else if (!languageOk) problems.Add($"network {expected.Position} is {network.Language ?? "of undetected language"}, not {expected.Language}");
        var matches = exportCode is not null && string.Equals(exportCode, expected.Code, StringComparison.Ordinal);
        return (problems.Count == 0, Describe(expected.ExpectedCount, expected.Position, expected.Language, expected.Code),
            Describe(count, expected.Position, network?.Language, exportCode),
            problems.Count == 0 ? null : string.Join("; ", problems),
            $"codeMatchesExport: {(matches ? "true" : "false")}." + (matches ? string.Empty : " The re-exported network text differs from the submitted code (expected/actual show both in comparison form: lines trimmed, blank lines dropped). " + ExportFormNote));
    }

    private static string Describe(int count, int position, string? language, string? code)
        => $"networks={count}; network {position}: language={language ?? "(none)"}; code:\n{code ?? "(absent)"}";

    /// <summary>create_block_from_document: the reported block must re-export in sd and declare the name; its sd hash is reported.</summary>
    private async Task<WriteOperationVerification> CreatedBlockReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var declared = SimaticSdBlockDocument.TryCheck(item.Content ?? string.Empty, out _)?.BlockName;
        string? path = null;
        if (failure is null && _results.TryGetValue(item.OperationId, out var result))
            path = WithCallerPlc(CanonicalJson.Deserialize<PlcBlockEditResultInfo>(result.GetRawText()).Block.BlockPath, item.TargetGroupPath);
        path ??= effects[item.OperationId].Target.BlockPath;
        if (path is null) return Fail(item, CreatedBlockCheck, $"Operation '{item.OperationId}': the created block is unknown.");
        var export = await client.GetBlockContentAsync(path, format: SourceFormatNames.Sd).ConfigureAwait(false);
        if (failure is not null)
            return new(item.OperationId, false, CreatedBlockCheck, null, export.Success ? $"{path} exists" : $"{path} not readable ({export.FailureCategory})",
                $"Operation '{item.OperationId}' failed with {failure.Category}; the state of '{path}' is reported. Inspect it with plc_read before retrying.");
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return Fail(item, CreatedBlockCheck, $"Operation '{item.OperationId}': '{path}' could not be re-exported in sd ({(export.Success ? "the export was empty" : export.Error)}).");
        _contentHashes[item.OperationId] = ContentHashes.Compute(SourceFormatNames.Sd, export.Payload);
        string? actual;
        try
        {
            actual = PlcBlockBundleText.TryGetDocument(export.Payload, SimaticSdText.BundleExtension, out _, out var s7dcl) ? SimaticSdDocument.Parse(s7dcl).BlockName : null;
        }
        catch (SimaticSdException)
        {
            actual = null;
        }

        var success = declared is not null && string.Equals(actual, declared, StringComparison.Ordinal);
        return new(item.OperationId, success, CreatedBlockCheck, $"{path} declares {declared}", $"{path} declares {actual ?? "(unreadable)"}", success
            ? "Run compile_check: the new block and its callers are compiled by TIA Portal only then."
            : $"Operation '{item.OperationId}': the re-export of '{path}' does not declare '{declared}'. Inspect it with plc_read get_block_content format sd.");
    }
}
