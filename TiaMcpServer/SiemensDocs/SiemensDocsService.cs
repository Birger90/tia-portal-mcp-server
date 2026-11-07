// Ported from Czarnak/siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki), siemens_docs_mcp/tools.py;
// reviewed and rewritten in C#. See THIRD_PARTY_NOTICES.md.
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.SiemensDocs;

/// <summary>
/// Executes siemens_docs items in the MCP server process. Needs neither TIA Portal nor a worker nor a
/// bound project. Every failure becomes a failed item with an honest category; nothing throws except
/// caller cancellation.
/// </summary>
public sealed class SiemensDocsService : IDisposable
{
    public const int MaxOtherVersions = 10;

    private readonly SiemensDocsCatalog _catalog;

    public SiemensDocsService(SiemensDocsOptions options, OperationAccessPolicy accessPolicy, HttpMessageHandler? handler = null, TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        AccessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _catalog = new SiemensDocsCatalog(new SiemensDocsHttp(options, handler, time, delay), time);
    }

    public OperationAccessPolicy AccessPolicy { get; }

    public SiemensDocsOptions Options => _catalog.Options;

    public SiemensDocsCatalog Catalog => _catalog;

    public void Dispose() => _catalog.Http.Dispose();

    public async Task<StructuredOperationItem> ExecuteAsync(SiemensDocsRequest item, CancellationToken cancellationToken)
    {
        try
        {
            object result = item.Operation switch
            {
                "search" => await SearchAsync(item, cancellationToken).ConfigureAwait(false),
                "list_publications" => await ListPublicationsAsync(item, cancellationToken).ConfigureAwait(false),
                "get_toc" => await GetTocAsync(item, cancellationToken).ConfigureAwait(false),
                "read_page" => await ReadPageAsync(item, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"siemens_docs operation '{item.Operation}' passed validation without an executor."),
            };
            return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, CanonicalJson.ToElement(result), null, null, null,
                Array.Empty<string>());
        }
        catch (SiemensDocsException exception)
        {
            return StructuredOperationItem.Failed(item, exception.Category, exception.Message, Array.Empty<string>());
        }
    }

    // ------------------------------------------------------------------ search

    private async Task<SiemensDocsSearchResult> SearchAsync(SiemensDocsRequest item, CancellationToken ct)
    {
        var host = _catalog.Host(item.Host);
        var locale = SiemensDocsOptions.NormalizeLocale(item.Locale) ?? Options.Locale;
        var filters = new List<object>();
        if (item.Product is not null)
            filters.Add(new { key = "Product", values = new[] { await _catalog.CanonicalProductAsync(host, item.Product.Trim(), ct).ConfigureAwait(false) } });
        if (item.Version is not null)
        {
            var (key, value) = await _catalog.CanonicalVersionAsync(host, item.Version.Trim(), ct).ConfigureAwait(false);
            filters.Add(new { key, values = new[] { value } });
        }

        var page = item.Page ?? 1;
        var limit = item.Limit ?? SiemensDocsCatalogRules.DefaultSearchLimit;
        object body = filters.Count == 0
            ? new { query = item.Query!, contentLocale = locale, paging = new { page, perPage = limit } }
            : new { query = item.Query!, contentLocale = locale, paging = new { page, perPage = limit }, filters };
        var raw = await _catalog.Http.PostJsonAsync<RawSearch>(host, "/api/khub/clustered-search", body, ct).ConfigureAwait(false);
        var hits = new List<SiemensDocsSearchHit>();
        foreach (var cluster in raw.Results ?? new List<RawCluster?>())
        {
            var topics = cluster?.Entries?.Where(entry => entry?.Type == "TOPIC" && entry.Topic is not null).Select(entry => entry!.Topic!).ToArray()
                         ?? Array.Empty<RawTopic>();
            if (topics.Length == 0) continue;
            var first = topics[0];
            var meta = SiemensDocsCatalog.Metadata(first.Metadata);
            hits.Add(new SiemensDocsSearchHit(
                first.Title ?? string.Empty,
                first.MapTitle ?? string.Empty,
                meta.GetValueOrDefault("Product", string.Empty),
                VersionOf(meta),
                (first.Breadcrumb ?? new List<string?>()).Where(part => part is not null).Select(part => part!).ToArray(),
                SiemensDocsMarkdown.PlainText(first.HtmlExcerpt),
                ReferenceOf(host, first),
                ReaderUrlOf(host, first),
                topics.Skip(1).Take(MaxOtherVersions).Select(topic => new SiemensDocsHitVariant(topic.MapTitle ?? string.Empty,
                    VersionOf(SiemensDocsCatalog.Metadata(topic.Metadata)), ReferenceOf(host, topic))).ToArray()));
        }

        var paging = raw.Paging;
        return new SiemensDocsSearchResult(host, item.Query!, locale, page, paging?.TotalResultsCount ?? hits.Count, paging?.TotalClustersCount ?? hits.Count,
            paging?.IsLastPage ?? true, hits);
    }

    private static string VersionOf(IReadOnlyDictionary<string, string> meta)
        => SiemensDocsCatalog.VersionKeys.Select(key => meta.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? string.Empty;

    private static string ReferenceOf(string host, RawTopic topic)
        => topic.MapId is { } map && SiemensDocsCatalog.IsId(map)
            ? SiemensDocsCatalog.CompactReference(host, map, topic.ContentId is { } content && SiemensDocsCatalog.IsId(content) ? content : null)
            : string.Empty;

    private string ReaderUrlOf(string host, RawTopic topic)
        => _catalog.SafeReaderUrl(topic.ReaderUrl)
           ?? (topic.MapId is { } map && SiemensDocsCatalog.IsId(map)
               ? SiemensDocsCatalog.ReaderUrl(host, map, topic.ContentId is { } content && SiemensDocsCatalog.IsId(content) ? content : null)
               : string.Empty);

    // ------------------------------------------------------------------ list_publications

    private async Task<SiemensDocsPublicationsResult> ListPublicationsAsync(SiemensDocsRequest item, CancellationToken ct)
    {
        var host = _catalog.Host(item.Host);
        var locale = item.Locale is null ? null : SiemensDocsOptions.NormalizeLocale(item.Locale);
        var all = await _catalog.FilterAsync(host, item.Product?.Trim(), item.Version?.Trim(), locale, item.TitleContains, ct).ConfigureAwait(false);
        var offset = item.Offset ?? 0;
        var limit = item.Limit ?? SiemensDocsCatalogRules.DefaultPublications;
        var rows = all.Skip(offset).Take(limit).Select(Info).ToArray();
        var next = offset + rows.Length < all.Count ? offset + rows.Length : (int?)null;
        return new SiemensDocsPublicationsResult(host, all.Count, offset, rows.Length, next, rows);
    }

    private static SiemensDocsPublicationInfo Info(DocsPublication publication)
        => new(publication.Title, publication.Product, publication.Version, publication.Locale, publication.LastPublication,
            SiemensDocsCatalog.CompactReference(publication.Host, publication.Id, null), SiemensDocsCatalog.ReaderUrl(publication.Host, publication.Id, null));

    // ------------------------------------------------------------------ get_toc

    private async Task<SiemensDocsTocResult> GetTocAsync(SiemensDocsRequest item, CancellationToken ct)
    {
        var target = await _catalog.ResolveAsync(item.Reference!, ct).ConfigureAwait(false);
        var publication = await _catalog.PublicationAsync(target.Host, target.MapId, ct).ConfigureAwait(false);
        var root = await _catalog.TocAsync(target.Host, target.MapId, ct).ConfigureAwait(false);
        var start = root;
        if (target.ContentId is not null)
            start = SiemensDocsCatalog.FindPath(root, target.ContentId)?[^1]
                    ?? throw new SiemensDocsException(WorkerFailureCategories.TargetNotFound,
                        $"Topic '{target.ContentId}' is not in the table of contents of '{publication.Title}'. Use search to find a valid reference.");
        var depth = item.Depth ?? SiemensDocsCatalogRules.DefaultTocDepth;
        var rows = new List<SiemensDocsTocNodeInfo>();
        void Walk(DocsTocNode node, int level)
        {
            rows.Add(new SiemensDocsTocNodeInfo(level, node.Title.Length > 0 ? node.Title : publication.Title,
                SiemensDocsCatalog.CompactReference(target.Host, target.MapId, NullIfEmpty(node.ContentId)),
                SiemensDocsCatalog.ReaderUrl(target.Host, target.MapId, NullIfEmpty(node.ContentId)), node.Children.Count));
            if (level < depth)
                foreach (var child in node.Children) Walk(child, level + 1);
        }

        Walk(start, 0);
        var offset = item.Offset ?? 0;
        var limit = item.Limit ?? SiemensDocsCatalogRules.DefaultTocNodes;
        var page = rows.Skip(offset).Take(limit).ToArray();
        var next = offset + page.Length < rows.Count ? offset + page.Length : (int?)null;
        return new SiemensDocsTocResult(Info(publication), rows[0].Reference, depth, rows.Count, offset, page.Length, next, page);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    // ------------------------------------------------------------------ read_page

    private async Task<SiemensDocsPageResult> ReadPageAsync(SiemensDocsRequest item, CancellationToken ct)
    {
        var target = await _catalog.ResolveAsync(item.Reference!, ct).ConfigureAwait(false);
        var publication = await _catalog.PublicationAsync(target.Host, target.MapId, ct).ConfigureAwait(false);
        var root = await _catalog.TocAsync(target.Host, target.MapId, ct).ConfigureAwait(false);
        var contentId = target.ContentId ?? NullIfEmpty(root.ContentId) ?? root.Children.FirstOrDefault()?.ContentId
            ?? throw new SiemensDocsException(WorkerFailureCategories.TargetNotFound, $"Publication '{publication.Title}' has no pages.");
        var path = SiemensDocsCatalog.FindPath(root, contentId);
        string title;
        IReadOnlyList<string> titles;
        if (path is not null)
        {
            contentId = path[^1].ContentId; // the reference may have named the TOC id
            title = path[^1].Title;
            titles = path.Select(node => node.Title.Length > 0 ? node.Title : publication.Title).ToArray();
        }
        else
        {
            title = "(untitled topic)";
            titles = new[] { publication.Title };
        }

        var contentPath = $"/api/khub/maps/{SiemensDocsCatalog.Escape(target.MapId)}/topics/{SiemensDocsCatalog.Escape(contentId)}/content?target=DESIGNED_READER";
        string html;
        try
        {
            html = await _catalog.Http.GetTextAsync(target.Host, contentPath, ct).ConfigureAwait(false);
        }
        catch (SiemensDocsException exception) when (exception.Category == WorkerFailureCategories.TargetNotFound)
        {
            throw new SiemensDocsException(WorkerFailureCategories.TargetNotFound,
                $"Topic '{contentId}' was not found in '{publication.Title}' on {target.Host}. Use get_toc or search to find a valid reference.", 404);
        }

        var markdown = SiemensDocsMarkdown.Convert(html, title, target.Host);
        var offset = item.Offset ?? 0;
        if (offset > 0 && offset >= markdown.Length)
            throw new SiemensDocsException(WorkerFailureCategories.ValidationError, $"offset {offset} is past the end of this page (length {markdown.Length}).");
        var (chunk, end) = Slice(markdown, offset, item.MaxChars ?? SiemensDocsCatalogRules.DefaultChars);
        return new SiemensDocsPageResult(Info(publication), titles, title, SiemensDocsCatalog.CompactReference(target.Host, target.MapId, contentId),
            SiemensDocsCatalog.ReaderUrl(target.Host, target.MapId, contentId), markdown.Length, offset, chunk.Length, end < markdown.Length ? end : null, chunk);
    }

    /// <summary>
    /// At most <paramref name="maxChars"/> characters from <paramref name="offset"/>; when the page goes on, the cut moves
    /// back to the last line break in the final fifth of the window (so a table row or code line is not split) and never
    /// splits a surrogate pair.
    /// </summary>
    internal static (string Chunk, int End) Slice(string text, int offset, int maxChars)
    {
        var end = (int)Math.Min((long)offset + maxChars, text.Length);
        if (end < text.Length)
        {
            var newline = text.LastIndexOf('\n', end - 1, end - offset);
            if (newline >= offset + maxChars * 4 / 5) end = newline + 1;
            else if (char.IsHighSurrogate(text[end - 1])) end--;
        }

        return (text[offset..end], end);
    }
}
