using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Contracts.Plc.SimaticSd;

/// <summary>
/// The sd bundle of create_block_from_document, checked: exactly one .s7dcl declaring a block and at most one .s7res of
/// the same name. The .s7res is optional: TIA Portal V21's own export of a block without texts has none (live 2026-11-02,
/// E1), so requiring it would be stricter than TIA Portal (E5; the earlier refusal followed a report by
/// 1748477582/tia-portal-openness-mcp). No text ids are generated. Every culture an .s7res holds must be a language of the
/// project: an <c>en-US</c> text in a project without English failed the import live (E4), see <see cref="Cultures"/>.
/// Shared by the host validation, the worker and the FakeWorker. Siemens-free.
/// </summary>
public sealed class SimaticSdBlockDocument
{
    /// <summary>The live TIA Portal V21 error of an .s7res culture the project does not have (2026-11-02, E4).</summary>
    public const string ForeignCultureEvidence = "Cannot import multilingual text with culture 'en-US' in File …s7res: Document option \"Override\" is not valid for Multilingual text.";

    private SimaticSdBlockDocument(string baseName, string s7dclName, string s7dcl, string? s7resName, string? s7res, SimaticSdDocument parsed)
    {
        BaseName = baseName;
        S7dclName = s7dclName;
        S7dcl = s7dcl;
        S7resName = s7resName;
        S7res = s7res;
        Parsed = parsed;
    }

    /// <summary>The file name without extension that ImportFromDocuments resolves the documents by.</summary>
    public string BaseName { get; }

    public string S7dclName { get; }

    public string S7dcl { get; }

    /// <summary>The .s7res file name; null when the bundle has none.</summary>
    public string? S7resName { get; }

    /// <summary>The .s7res text; null when the bundle has none.</summary>
    public string? S7res { get; }

    /// <summary>The distinct cultures of the .s7res texts in first-seen order (e.g. de-DE, en-US); empty without an .s7res.</summary>
    public IReadOnlyList<string> Cultures
        => SimaticSdResources.Parse(S7res).Values.SelectMany(texts => texts.Select(t => t.Culture)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The note naming the .s7res cultures (each must be a project language); null without cultures.</summary>
    public string? CultureNote
        => Cultures.Count == 0 ? null
            : $"The .s7res holds texts in {string.Join(", ", Cultures)}. Every culture of an .s7res must be a language of the project: TIA Portal V21 refused an "
              + $"en-US text in a project without that language (live 2026-11-02: \"{ForeignCultureEvidence}\"). Remove the texts of cultures the project does not have.";

    public SimaticSdDocument Parsed { get; }

    /// <summary>The declared block name.</summary>
    public string BlockName => Parsed.BlockName!;

    public bool IsFailSafe => Parsed.IsFailSafe;

    /// <summary>The documents that had no BOM (they are written with one, as reported to be required).</summary>
    public IReadOnlyList<string> WithoutBom
        => new[] { (Name: S7dclName, Text: (string?)S7dcl), (Name: S7resName ?? string.Empty, Text: S7res) }
            .Where(d => d.Text is not null && !SimaticSdFileText.HasBom(d.Text)).Select(d => d.Name).ToList();

    /// <summary>The checked document, or the reason it is refused (a validation_error).</summary>
    public static SimaticSdBlockDocument? TryCheck(string content, out string? error)
    {
        error = null;
        var documents = PlcBlockBundleText.Documents(content);
        if (documents.Count == 0)
        {
            error = "content must be an sd bundle: '--- FILE: Name.s7dcl ---' then the .s7dcl text, optionally '--- FILE: Name.s7res ---' then the .s7res text (the layout of plc_read get_block_content format sd).";
            return null;
        }

        var s7dcl = documents.Where(d => d.Key.EndsWith(SimaticSdText.BundleExtension, StringComparison.OrdinalIgnoreCase)).ToList();
        var s7res = documents.Where(d => d.Key.EndsWith(SimaticSdText.ResourceExtension, StringComparison.OrdinalIgnoreCase)).ToList();
        if (s7dcl.Count != 1) error = $"content must hold exactly one .s7dcl document (found {s7dcl.Count}).";
        else if (s7res.Count > 1) error = $"content may hold at most one .s7res document (found {s7res.Count}).";
        else if (documents.Count != 1 + s7res.Count) error = "content may hold only the .s7dcl and, optionally, the .s7res document.";
        else if (s7dcl[0].Key.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || s7res.Any(r => r.Key.IndexOfAny(new[] { '/', '\\', ':' }) >= 0))
            error = "document names must be plain file names.";
        else if (s7res.Count == 1 && !string.Equals(Strip(s7dcl[0].Key, SimaticSdText.BundleExtension), Strip(s7res[0].Key, SimaticSdText.ResourceExtension), StringComparison.OrdinalIgnoreCase))
            error = "the .s7dcl and the .s7res document must have the same name (ImportFromDocuments finds them by the file name without extension).";
        if (error is not null) return null;

        SimaticSdDocument parsed;
        try
        {
            parsed = SimaticSdDocument.Parse(s7dcl[0].Value);
        }
        catch (SimaticSdException exception)
        {
            error = $"the .s7dcl document: {exception.Message}";
            return null;
        }

        if (parsed.BlockName is null)
        {
            error = "the .s7dcl document declares no block (FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK or DATA_BLOCK \"Name\").";
            return null;
        }

        return new SimaticSdBlockDocument(Strip(s7dcl[0].Key, SimaticSdText.BundleExtension), s7dcl[0].Key, s7dcl[0].Value,
            s7res.Count == 1 ? s7res[0].Key : null, s7res.Count == 1 ? s7res[0].Value : null, parsed);
    }

    private static string Strip(string name, string extension) => name.Substring(0, name.Length - extension.Length);
}
