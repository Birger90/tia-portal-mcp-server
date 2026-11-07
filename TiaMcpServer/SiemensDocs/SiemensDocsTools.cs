using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.SiemensDocs;

[McpServerToolType]
public class SiemensDocsTools
{
    internal const string ToolName = "siemens_docs";

    [McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Read the official Siemens documentation (docs.tia.siemens.cloud: TIA Portal, STEP 7, WinCC, Openness, hardware manuals; docs.industrial-operations-x.siemens.cloud: SIMATIC AX, Industrial Edge, Insights Hub) "
        + "with up to 50 independent operations (a failed item does not stop the others). Runs in the MCP server; works without TIA Portal and without a bound project; available in every access mode. "
        + "Operations: search (query; optional host tia|iox, product, version, locale, limit 1-50, page: full-text search; each hit has title, publication, version, breadcrumb, excerpt, reference, readerUrl, and otherVersions of the same topic), "
        + "read_page (reference; optional offset, maxChars 1000-40000 default 20000: one topic as Markdown with publication, path and nextOffset when the page goes on), "
        + "get_toc (reference; optional depth 1-6 default 2, limit, offset: the outline of a publication or of the subtree under a topic), "
        + "list_publications (optional host, product, version, locale, titleContains, limit 1-200, offset: publications with product, version, locale and reference; use it to find valid product and version values). "
        + "A reference is tia:<mapId>[/<contentId>], iox:..., or an https reader URL of an allowed host; it is resolved to ids and never fetched as given. "
        + "Only the hosts the server allows are contacted (default the two Siemens hosts; --docs-hosts); requests are spaced per host, time out, and are size-capped. "
        + "The first product/version filter or pretty reader URL on a host loads its publication list (16 MB on docs.tia.siemens.cloud; cached 24 h). "
        + "Failures: validation_error, target_not_found, docs_unreachable, docs_rejected (HTTP status), docs_response_invalid (size cap or changed site). Not live-tested on the Windows PC.")]
    public static async Task<CallToolResult> SiemensDocs(
        SiemensDocsService docs,
        [Description("Independent documentation operations, each with a unique operationId and an operation.")] SiemensDocsRequest[] operations,
        CancellationToken cancellationToken = default)
    {
        var validation = SiemensDocsCatalogRules.Validate(operations);
        if (!validation.IsValid) return Error(validation.Category, validation.Error);
        var denied = SiemensDocsCatalogRules.ValidateAccessMode(operations, docs.AccessPolicy.Mode);
        if (denied.Count != 0) return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", denied));

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync<SiemensDocsRequest>(operations,
            item => docs.ExecuteAsync(item, cancellationToken), cancellationToken).ConfigureAwait(false);
        var bounded = BatchReadTool.ApplyBudget(batch, ToolName, SiemensDocsContractVersion.Current, RetryGuidance);
        return StructuredToolResult.Create(new OperationBatchReadResponse(ToolName, SiemensDocsContractVersion.Current,
            bounded.IsFullySuccessful, null, Array.Empty<string>(), bounded), isError: false);
    }

    internal static string RetryGuidance(StructuredOperationItem item)
        => item.Operation switch
        {
            "read_page" => "Lower maxChars and continue with offset, or re-run this operationId in its own siemens_docs call.",
            "get_toc" or "list_publications" => "Lower limit or depth and page with offset, or re-run this operationId in its own siemens_docs call.",
            "search" => "Lower limit, or re-run this operationId in its own siemens_docs call.",
            _ => "Re-run this operationId in its own siemens_docs call.",
        };

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, SiemensDocsContractVersion.Current, category, message);
}
