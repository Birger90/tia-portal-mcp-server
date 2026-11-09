using System.Reflection;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TiaMcpServer.McpContext;

/// <summary>One embedded reference document: its stable resource URI, language and text.</summary>
public sealed record TiaMcpDocument(string Uri, string Language, string Group, string FileName, string Title, string Text)
{
    /// <summary>UTF-8 size in bytes, as MCP <c>Resource.size</c> reports it.</summary>
    public long Size { get; } = Encoding.UTF8.GetByteCount(Text);
}

/// <summary>
/// The user-facing reference documents embedded into the server at build time (<c>McpContext/EmbeddedDocs.props</c>):
/// every <c>docs/SupportedOperations/*.md</c> plus README and FORK_FEATURES, each in English and German. The server reads
/// only these resources, never a file of the user's machine. URIs: <c>tia-mcp://docs/{en|de}/{NAME}.md</c> for the
/// SupportedOperations documents, <c>tia-mcp://docs/{en|de}/server/{NAME}.md</c> for README and FORK_FEATURES.
/// </summary>
public static class TiaMcpDocuments
{
    public const string Scheme = "tia-mcp://";
    public const string DocsRoot = Scheme + "docs/";
    public const string MimeType = "text/markdown";
    internal const string ResourcePrefix = "TiaMcpDocs/";
    internal const string OperationsGroup = "ops";
    internal const string ServerGroup = "server";

    private static readonly Lazy<IReadOnlyList<TiaMcpDocument>> Loaded = new(() => Load(typeof(TiaMcpDocuments).Assembly));

    /// <summary>Every embedded document, sorted by URI.</summary>
    public static IReadOnlyList<TiaMcpDocument> All => Loaded.Value;

    public static TiaMcpDocument? Find(string uri) => All.FirstOrDefault(document => string.Equals(document.Uri, uri, StringComparison.Ordinal));

    /// <summary>The URI of a SupportedOperations document (file name without language suffix, e.g. PLC_OPERATIONS_SUMMARY.md).</summary>
    public static string OperationsDocumentUri(string language, string fileName) => DocsRoot + language + "/" + fileName;

    internal static IReadOnlyList<TiaMcpDocument> Load(Assembly assembly)
    {
        var documents = new List<TiaMcpDocument>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var relative = name.Substring(ResourcePrefix.Length);
            var slash = relative.IndexOf('/');
            if (slash <= 0) continue;
            var group = relative.Substring(0, slash);
            var file = relative.Substring(slash + 1);
            if (!file.EndsWith(".md", StringComparison.Ordinal)) continue;
            var german = file.EndsWith(".de.md", StringComparison.Ordinal);
            var language = german ? "de" : "en";
            var baseName = german ? file.Substring(0, file.Length - ".de.md".Length) + ".md" : file;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            var uri = group == ServerGroup ? DocsRoot + language + "/server/" + baseName : DocsRoot + language + "/" + baseName;
            documents.Add(new TiaMcpDocument(uri, language, group, baseName, TitleOf(text, baseName), text));
        }

        return documents.OrderBy(document => document.Uri, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The first level-1 heading, else the file name.</summary>
    internal static string TitleOf(string text, string fallback)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line.Substring(2).Trim();
        }

        return fallback;
    }

    internal static string Describe(TiaMcpDocument document)
        => (document.Language == "de" ? "German" : "English") + " reference document of the TIA Portal MCP server ("
            + (document.Group == ServerGroup ? "server overview" : "operation reference") + ", " + document.FileName
            + "). Embedded at build time; reading it needs no TIA Portal and works in every access mode. Not live-tested statements are marked in the text.";
}

/// <summary>One embedded document as a direct MCP resource.</summary>
internal sealed class TiaMcpDocumentResource : McpServerResource
{
    private readonly TiaMcpDocument _document;
    private readonly ResourceTemplate _template;
    private readonly Resource _resource;

    public TiaMcpDocumentResource(TiaMcpDocument document)
    {
        _document = document;
        var name = "docs-" + document.Language + (document.Group == TiaMcpDocuments.ServerGroup ? "-server-" : "-")
            + Path.GetFileNameWithoutExtension(document.FileName);
        var description = TiaMcpDocuments.Describe(document);
        _template = new ResourceTemplate
        {
            Name = name, Title = document.Title, UriTemplate = document.Uri, Description = description, MimeType = TiaMcpDocuments.MimeType,
        };
        _resource = new Resource
        {
            Name = name, Title = document.Title, Uri = document.Uri, Description = description, MimeType = TiaMcpDocuments.MimeType,
            Size = document.Size,
        };
    }

    public override ResourceTemplate ProtocolResourceTemplate => _template;

    public override Resource? ProtocolResource => _resource;

    public override IReadOnlyList<object> Metadata { get; } = Array.Empty<object>();

    public override bool IsMatch(string uri) => string.Equals(uri, _document.Uri, StringComparison.Ordinal);

    public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken)
        => ValueTask.FromResult(new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = _document.Uri, MimeType = TiaMcpDocuments.MimeType, Text = _document.Text }],
        });
}
