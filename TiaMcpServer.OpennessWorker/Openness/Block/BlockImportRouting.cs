using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

internal enum BlockImportRoute
{
    SimaticMl,
    SimaticSd,
}

/// <summary>
/// Chooses which Openness importer a parsed bundle belongs to.
///
/// This decision is deliberately isolated and directly tested. It previously lived inline in
/// BlockImporter (commit c53e6f4) and was removed by an unrelated refactor (dddf9d2) without
/// any test failing, which left update_block_logic broken for months.
/// </summary>
internal static class BlockImportRouting
{
    public static BlockImportRoute SelectRoute(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        return FindSimaticMlDocument(bundle) is null
            ? BlockImportRoute.SimaticSd
            : BlockImportRoute.SimaticMl;
    }

    public static BlockImportDocument SelectAuthoritativeDocument(ParsedBlockImportBundle bundle)
    {
        return FindSimaticMlDocument(bundle)
            ?? throw ValidationFailure(
                "This bundle contains no Simatic ML (.xml) document. The block could not be "
                + "exported as Simatic ML, which usually means it is inconsistent — compile it "
                + "in TIA Portal and read it again before writing.");
    }

    public static string SimaticSdBaseName(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        // ImportFromDocuments resolves the document set by file name WITHOUT extension.
        return Path.GetFileNameWithoutExtension(bundle.PrimaryDocumentName);
    }

    /// <summary>
    /// The .s7dcl document of an sd bundle (update_block_logic format sd): exactly one .s7dcl, no SimaticML (.xml)
    /// document, and nothing besides an optional .s7res of the same name.
    /// </summary>
    public static BlockImportDocument SelectSimaticSdDocument(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        if (FindSimaticMlDocument(bundle) is not null)
            throw ValidationFailure("format sd takes only the SIMATIC SD documents (.s7dcl, .s7res); this bundle has a SimaticML (.xml) "
                + "document. Write it with format xml, or submit the bundle of plc_read get_block_content format sd.");
        var s7dcl = bundle.Documents.Where(d => d.LogicalName.EndsWith(".s7dcl", StringComparison.OrdinalIgnoreCase)).ToList();
        if (s7dcl.Count != 1)
            throw ValidationFailure($"format sd needs exactly one .s7dcl document; the bundle has {s7dcl.Count}.");
        var baseName = Path.GetFileNameWithoutExtension(s7dcl[0].LogicalName);
        foreach (var document in bundle.Documents)
        {
            if (ReferenceEquals(document, s7dcl[0])) continue;
            if (!document.LogicalName.EndsWith(".s7res", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileNameWithoutExtension(document.LogicalName), baseName, StringComparison.OrdinalIgnoreCase))
                throw ValidationFailure($"format sd takes '{baseName}.s7dcl' and optionally '{baseName}.s7res'; '{document.LogicalName}' is not one of them.");
        }

        return s7dcl[0];
    }

    /// <summary>The .s7dcl must declare the block it updates: update_block_logic never renames or creates.</summary>
    public static void RequireDeclaredName(BlockImportDocument s7dcl, string targetName, string displayPath)
    {
        string? declared;
        try
        {
            declared = Contracts.Plc.SimaticSd.SimaticSdDocument.Parse(s7dcl.Content).BlockName;
        }
        catch (Contracts.Plc.SimaticSd.SimaticSdException exception)
        {
            throw ValidationFailure($"The .s7dcl document cannot be read: {exception.Message}");
        }

        if (declared is null)
            throw ValidationFailure("The .s7dcl document declares no block (FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK or DATA_BLOCK \"Name\").");
        if (!string.Equals(declared, targetName, StringComparison.Ordinal))
            throw ValidationFailure($"The .s7dcl document declares '{declared}' but '{displayPath}' resolves to '{targetName}'. "
                + "update_block_logic never renames and never creates: submit a document declaring "
                + $"'{targetName}' (create_block_from_document creates a new block).");
    }

    /// <summary>
    /// Only the authoritative document is applied. If the caller edited any other document we
    /// must refuse rather than silently discard their edit.
    /// </summary>
    public static void EnsureOnlyAuthoritativeDocumentChanged(
        ParsedBlockImportBundle submitted,
        ParsedBlockImportBundle current,
        string authoritativeName)
    {
        if (submitted is null) throw new ArgumentNullException(nameof(submitted));
        if (current is null) throw new ArgumentNullException(nameof(current));

        var currentByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in current.Documents)
        {
            currentByName[document.LogicalName] = document.Content;
        }

        foreach (var document in submitted.Documents)
        {
            if (string.Equals(document.LogicalName, authoritativeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!currentByName.TryGetValue(document.LogicalName, out var currentContent)
                || !string.Equals(currentContent, document.Content, StringComparison.Ordinal))
            {
                throw ValidationFailure(
                    $"'{document.LogicalName}' was modified, but only '{authoritativeName}' is "
                    + "applied by update_block_logic. Re-read the block, edit "
                    + $"'{authoritativeName}', and submit again.");
            }
        }
    }

    private static BlockImportDocument? FindSimaticMlDocument(ParsedBlockImportBundle bundle)
    {
        foreach (var document in bundle.Documents)
        {
            if (document.LogicalName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
