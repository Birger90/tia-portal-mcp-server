using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>One <c>key := value</c> of a SIMATIC SD pragma; <c>value</c> without its quotes.</summary>
public sealed record PlcSdPragmaEntryResult(string Key, string Value);

/// <summary>One .s7res text of a network title or comment.</summary>
public sealed record PlcSdCultureTextResult(string Culture, string Text);

/// <summary>
/// One network of list_networks: <c>number</c> 1-based in document order, <c>language</c> from its S7_Language pragma
/// (<c>languageSource</c> pragma) or detected from the RUNG syntax as the Siemens skill §5 does (detected; null when
/// ambiguous), every pragma entry in order, the title and comment ids (S7_NetworkTitle, S7_NetworkComment) resolved from
/// the .s7res per culture (an id the .s7res lacks has no texts), the body without its common indentation, and the
/// instruction forms the export wrote that are reported not to re-import (<see cref="SimaticSdExportForms"/>).
/// </summary>
public sealed record PlcSdNetworkResult(
    int Number,
    string? Language,
    string? LanguageSource,
    IReadOnlyList<PlcSdPragmaEntryResult> Pragmas,
    string? TitleId,
    IReadOnlyList<PlcSdCultureTextResult> Title,
    string? CommentId,
    IReadOnlyList<PlcSdCultureTextResult> Comment,
    string Code,
    IReadOnlyList<string> ExportOnlyForms);

/// <summary>
/// list_networks: the block header pragmas (S7_PreferredLanguage, S7_Safety, ... every key), <c>failSafe</c> (header
/// S7_Safety := "TRUE"), the networks, and the sd <c>contentHash</c> of the export they were read from (equal to the
/// get_block_content sd hash, usable as the expectedContentHash of insert_network, replace_network and delete_network).
/// </summary>
public sealed record PlcSdNetworkListResult(
    string BlockPath,
    string? BlockKind,
    string? BlockName,
    string? PreferredLanguage,
    bool FailSafe,
    IReadOnlyList<PlcSdPragmaEntryResult> HeaderPragmas,
    int NetworkCount,
    IReadOnlyList<PlcSdNetworkResult> Networks,
    string S7dclDocument,
    string? S7resDocument,
    string ContentHash);

/// <summary>plc_read list_networks: a host computation over get_block_content format sd. Not live-tested.</summary>
internal static class PlcSdNetworkReader
{
    public static bool Handles(string operation) => operation == SimaticSdMethods.ListNetworks;

    public static async Task<StructuredOperationItem> ExecuteAsync(OpennessWorkerClient client, PlcOperationRequest operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = await client.GetBlockContentAsync(operation.BlockPath!, format: SourceFormatNames.Sd).ConfigureAwait(false);
        var warnings = call.Warnings ?? Array.Empty<string>();
        if (!call.Success)
            return StructuredOperationItem.Failed(operation, call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                call.Error ?? "The SIMATIC SD export failed.", warnings);
        if (string.IsNullOrEmpty(call.Payload))
            return StructuredOperationItem.Failed(operation, WorkerFailureCategories.WorkerOperationFailed, "The SIMATIC SD export was empty.", warnings);
        try
        {
            var listing = Build(operation.BlockPath!, call.Payload);
            warnings = warnings.Concat(PlcV21PitfallReadWarnings.For(operation.BlockPath, call.Payload)).ToArray();
            return new StructuredOperationItem(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded,
                CanonicalJson.ToElement(listing), Failure: null, Omission: null, SkipReason: null, warnings);
        }
        catch (SimaticSdException exception)
        {
            return StructuredOperationItem.Failed(operation, WorkerFailureCategories.TargetKindUnsupported,
                $"The .s7dcl export of '{operation.BlockPath}' could not be split into networks ({exception.Message}); read it with get_block_content format sd.", warnings);
        }
    }

    /// <exception cref="SimaticSdException">The bundle has no .s7dcl document or its networks do not pair.</exception>
    internal static PlcSdNetworkListResult Build(string blockPath, string bundle)
    {
        if (!PlcBlockBundleText.TryGetDocument(bundle, SimaticSdText.BundleExtension, out var s7dclName, out var s7dcl))
            throw new SimaticSdException(WorkerFailureCategories.TargetKindUnsupported, "the export holds no .s7dcl document");
        string? s7resName = PlcBlockBundleText.TryGetDocument(bundle, SimaticSdText.ResourceExtension, out var resName, out var s7res) ? resName : null;
        var document = SimaticSdDocument.Parse(s7dcl);
        var texts = SimaticSdResources.Parse(s7resName is null ? null : s7res);
        IReadOnlyList<PlcSdCultureTextResult> Texts(string? id)
            => id is not null && texts.TryGetValue(id, out var found) ? found.Select(t => new PlcSdCultureTextResult(t.Culture, t.Text)).ToList() : Array.Empty<PlcSdCultureTextResult>();
        var networks = document.Networks.Select(n =>
        {
            var titleId = n.Pragma?.Get("S7_NetworkTitle");
            var commentId = n.Pragma?.Get("S7_NetworkComment");
            return new PlcSdNetworkResult(n.Number, n.Language, n.LanguageSource, Entries(n.Pragma), titleId, Texts(titleId), commentId, Texts(commentId),
                n.Code, SimaticSdExportForms.Find(n.Code));
        }).ToList();
        return new PlcSdNetworkListResult(blockPath, document.BlockKind, document.BlockName, document.PreferredLanguage, document.IsFailSafe,
            Entries(document.HeaderPragma), networks.Count, networks, s7dclName, s7resName, ContentHashes.Compute(SourceFormatNames.Sd, bundle));
    }

    private static IReadOnlyList<PlcSdPragmaEntryResult> Entries(SimaticSdPragma? pragma)
        => pragma?.Entries.Select(e => new PlcSdPragmaEntryResult(e.Key, e.Value)).ToList() ?? (IReadOnlyList<PlcSdPragmaEntryResult>)Array.Empty<PlcSdPragmaEntryResult>();
}
