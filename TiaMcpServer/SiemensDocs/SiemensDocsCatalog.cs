// Ported from Czarnak/siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki), siemens_docs_mcp/catalog.py and
// toc.py; reviewed and rewritten in C#. See THIRD_PARTY_NOTICES.md.
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.SiemensDocs;

/// <summary>One publication (Fluid Topics "map") of a documentation host.</summary>
public sealed record DocsPublication(
    string Host,
    string Id,
    string Title,
    string Locale,
    string Product,
    string Version,
    string VersionKey,
    string PrettyUrl,
    string LastPublication);

/// <summary>A TOC node; the synthetic root of a publication without one root topic has empty ids.</summary>
public sealed class DocsTocNode
{
    public DocsTocNode(string tocId, string contentId, string title, string prettyUrl, int depth)
    {
        TocId = tocId;
        ContentId = contentId;
        Title = title;
        PrettyUrl = prettyUrl;
        Depth = depth;
    }

    public string TocId { get; }
    public string ContentId { get; }
    public string Title { get; }
    public string PrettyUrl { get; }
    public int Depth { get; }
    public List<DocsTocNode> Children { get; } = new();
}

/// <summary>A resolved documentation address: a publication, optionally one topic in it.</summary>
public sealed record DocsTarget(string Host, string MapId, string? ContentId);

/// <summary>
/// Per-host publication catalog and TOC cache, and the resolution of the references the tool accepts:
/// compact <c>&lt;prefix&gt;:&lt;mapId&gt;[/&lt;contentId&gt;]</c> (prefix <c>tia</c>, <c>iox</c> or an allowed
/// host name) and reader URLs <c>https://&lt;allowed host&gt;/r/...</c> (id form or pretty URL). A reference is
/// only parsed into identifiers; it is never fetched. Catalog and TOCs are cached for 24 hours.
/// </summary>
public sealed partial class SiemensDocsCatalog
{
    public const string TiaVersionKey = "tia:SoftwareVersionFilter";
    public const string SoftwareVersionKey = "SoftwareVersion";
    public static readonly string[] VersionKeys = { TiaVersionKey, SoftwareVersionKey };
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    public const int MaxCachedTocs = 32;
    public const int MaxCachedMaps = 256;

    private readonly SiemensDocsHttp _http;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _mapLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Loaded, HostCatalog Catalog)> _catalogs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string, string), (DateTimeOffset Loaded, DocsPublication Publication)> _single = new();
    private readonly ConcurrentDictionary<(string, string), (DateTimeOffset Loaded, DocsTocNode Root)> _tocs = new();

    public SiemensDocsCatalog(SiemensDocsHttp http, TimeProvider? time = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? TimeProvider.System;
    }

    public SiemensDocsOptions Options => _http.Options;

    public SiemensDocsHttp Http => _http;

    /// <summary>The host for an optional caller value: <c>tia</c>, <c>iox</c> or an allowed host name; default the first allowed host.</summary>
    public string Host(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Options.DefaultHost;
        var host = value.Trim().ToLowerInvariant();
        if (SiemensDocsOptions.Aliases.TryGetValue(host, out var aliased)) host = aliased;
        return Options.IsAllowed(host)
            ? host
            : throw new SiemensDocsException(WorkerFailureCategories.ValidationError,
                $"Host '{value}' is not allowed. Allowed hosts: {string.Join(", ", Options.Hosts)} (aliases: tia, iox).");
    }

    // ------------------------------------------------------------------ publications

    /// <summary>All publications of a host (one <c>GET /api/khub/maps</c>, 16.6 MB on docs.tia.siemens.cloud; cached).</summary>
    public async Task<IReadOnlyList<DocsPublication>> PublicationsAsync(string host, CancellationToken ct)
        => (await CatalogAsync(host, ct).ConfigureAwait(false)).Publications;

    /// <summary>One publication; from the loaded catalog, else <c>GET /api/khub/maps/{id}</c> (404 → target_not_found).</summary>
    public async Task<DocsPublication> PublicationAsync(string host, string mapId, CancellationToken ct)
    {
        if (_catalogs.TryGetValue(host, out var cached) && Fresh(cached.Loaded))
            return cached.Catalog.ById.TryGetValue(mapId, out var known) ? known : throw PublicationNotFound(host, mapId);
        if (_single.TryGetValue((host, mapId), out var one) && Fresh(one.Loaded)) return one.Publication;
        RawMap raw;
        try
        {
            raw = await _http.GetJsonAsync<RawMap>(host, "/api/khub/maps/" + Escape(mapId), ct).ConfigureAwait(false);
        }
        catch (SiemensDocsException exception) when (exception.Category == WorkerFailureCategories.TargetNotFound)
        {
            throw PublicationNotFound(host, mapId);
        }

        var publication = ToPublication(host, raw) ?? throw SiemensDocsHttp.Invalid(host, "/api/khub/maps/" + mapId, "the publication has no id");
        if (_single.Count >= MaxCachedMaps) _single.Clear();
        _single[(host, mapId)] = (_time.GetUtcNow(), publication);
        return publication;
    }

    /// <summary>The canonical spelling of a product on a host (case-insensitive), or a validation error with close matches.</summary>
    public async Task<string> CanonicalProductAsync(string host, string value, CancellationToken ct)
    {
        var catalog = await CatalogAsync(host, ct).ConfigureAwait(false);
        return catalog.Products.FirstOrDefault(product => string.Equals(product, value, StringComparison.OrdinalIgnoreCase))
               ?? throw Unknown(host, "product", value, catalog.Products);
    }

    /// <summary>The metadata key and canonical value of a version (tia:SoftwareVersionFilter first, then SoftwareVersion).</summary>
    public async Task<(string Key, string Value)> CanonicalVersionAsync(string host, string value, CancellationToken ct)
    {
        var catalog = await CatalogAsync(host, ct).ConfigureAwait(false);
        foreach (var key in VersionKeys)
            if (catalog.Versions[key].FirstOrDefault(version => string.Equals(version, value, StringComparison.OrdinalIgnoreCase)) is { } match)
                return (key, match);
        throw Unknown(host, "version", value, catalog.Versions.Values.SelectMany(values => values).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Publications matching every given filter, sorted by title (then id).</summary>
    public async Task<IReadOnlyList<DocsPublication>> FilterAsync(string host, string? product, string? version, string? locale, string? titleContains,
        CancellationToken ct)
    {
        var catalog = await CatalogAsync(host, ct).ConfigureAwait(false);
        IEnumerable<DocsPublication> result = catalog.Publications;
        if (product is not null)
        {
            var canonical = await CanonicalProductAsync(host, product, ct).ConfigureAwait(false);
            result = result.Where(publication => publication.Product == canonical);
        }

        if (locale is not null)
        {
            var canonical = catalog.Locales.FirstOrDefault(known => string.Equals(known, locale, StringComparison.OrdinalIgnoreCase))
                            ?? throw Unknown(host, "locale", locale, catalog.Locales);
            result = result.Where(publication => publication.Locale == canonical);
        }

        if (version is not null)
        {
            var (key, canonical) = await CanonicalVersionAsync(host, version, ct).ConfigureAwait(false);
            result = result.Where(publication => catalog.VersionOf(publication.Id, key) == canonical);
        }

        if (!string.IsNullOrEmpty(titleContains))
            result = result.Where(publication => publication.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));
        return result.OrderBy(publication => publication.Title, StringComparer.OrdinalIgnoreCase).ThenBy(publication => publication.Id, StringComparer.Ordinal)
            .ToArray();
    }

    // ------------------------------------------------------------------ TOC

    public async Task<DocsTocNode> TocAsync(string host, string mapId, CancellationToken ct)
    {
        if (_tocs.TryGetValue((host, mapId), out var cached) && Fresh(cached.Loaded)) return cached.Root;
        var path = $"/api/khub/maps/{Escape(mapId)}/pages";
        RawPages raw;
        try
        {
            raw = await _http.GetJsonAsync<RawPages>(host, path, ct).ConfigureAwait(false);
        }
        catch (SiemensDocsException exception) when (exception.Category == WorkerFailureCategories.TargetNotFound)
        {
            throw PublicationNotFound(host, mapId);
        }

        var root = ParseToc(raw, host, path);
        if (_tocs.Count >= MaxCachedTocs)
            foreach (var oldest in _tocs.OrderBy(pair => pair.Value.Loaded).Take(_tocs.Count - MaxCachedTocs + 1).Select(pair => pair.Key).ToArray())
                _tocs.TryRemove(oldest, out _);
        _tocs[(host, mapId)] = (_time.GetUtcNow(), root);
        return root;
    }

    /// <summary>Parses <c>/pages</c>: one root, or several sections under a synthetic root with empty ids.</summary>
    internal static DocsTocNode ParseToc(RawPages raw, string host, string path)
    {
        var nodes = raw.PaginatedToc?.Where(node => node is not null).Select(node => node!).ToArray() ?? Array.Empty<RawTocNode>();
        if (nodes.Length == 0) throw SiemensDocsHttp.Invalid(host, path, "paginatedToc is missing or empty");
        if (nodes.Length == 1) return Node(nodes[0], 0, host, path);
        var first = nodes[0].PrettyUrl ?? string.Empty;
        var root = new DocsTocNode(string.Empty, string.Empty, string.Empty, first.Contains('/') ? first[..first.LastIndexOf('/')] : first, 0);
        foreach (var node in nodes) root.Children.Add(Node(node, 1, host, path));
        return root;
    }

    private static DocsTocNode Node(RawTocNode raw, int depth, string host, string path)
    {
        if (string.IsNullOrEmpty(raw.ContentId) || string.IsNullOrEmpty(raw.TocId))
            throw SiemensDocsHttp.Invalid(host, path, "a TOC node has no contentId or tocId");
        if (depth > 64) throw SiemensDocsHttp.Invalid(host, path, "the TOC is nested deeper than 64 levels");
        var node = new DocsTocNode(raw.TocId, raw.ContentId, raw.Title ?? string.Empty, raw.PrettyUrl ?? string.Empty, depth);
        var children = raw.PageToc is { Count: > 0 } ? raw.PageToc : raw.Children;
        foreach (var child in children ?? new List<RawTocNode?>())
            if (child is not null) node.Children.Add(Node(child, depth + 1, host, path));
        return node;
    }

    /// <summary>Root-to-node path of a content id or TOC id (in-page links use the TOC id), or null.</summary>
    public static IReadOnlyList<DocsTocNode>? FindPath(DocsTocNode root, string id)
    {
        if (id.Length > 0 && (root.ContentId == id || root.TocId == id)) return new[] { root };
        foreach (var child in root.Children)
            if (FindPath(child, id) is { } path)
                return new[] { root }.Concat(path).ToArray();
        return null;
    }

    public static IEnumerable<DocsTocNode> Walk(DocsTocNode root)
    {
        var stack = new Stack<DocsTocNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }

    // ------------------------------------------------------------------ references

    /// <summary>Resolves a compact reference or a reader URL of an allowed host to ids. Nothing is fetched from the given URL.</summary>
    public async Task<DocsTarget> ResolveAsync(string reference, CancellationToken ct)
    {
        var value = reference.Trim();
        if (!value.Contains("://", StringComparison.Ordinal) && !value.StartsWith("//", StringComparison.Ordinal))
            return ParseCompact(value);

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw Unresolvable(reference, "only https reader URLs of the allowed hosts are accepted");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw Unresolvable(reference, "a reader URL carries no user info and no port");
        var host = uri.IdnHost.ToLowerInvariant();
        if (!Options.IsAllowed(host))
            throw new SiemensDocsException(WorkerFailureCategories.ValidationError,
                $"Host '{host}' is not allowed. Allowed hosts: {string.Join(", ", Options.Hosts)}.");
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        if (!path.StartsWith("/r/", StringComparison.Ordinal)) throw Unresolvable(reference, "a reader URL path starts with /r/");
        var relative = path[3..];
        var segments = relative.Split('/');
        if (segments.Length <= 2 && MapIdShape().IsMatch(segments[0]))
        {
            if (segments.Length == 2 && !IsId(segments[1])) throw Unresolvable(reference, "the topic id is not valid");
            return new DocsTarget(host, segments[0], segments.Length == 2 ? segments[1] : null);
        }

        return await ResolvePrettyAsync(host, relative, reference, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A pretty reader URL: several publications can share a pretty URL and a longer prefix may lack the topic, so every
    /// publication whose pretty URL is a prefix is tried, longest first, until one's TOC holds the page (as the reference
    /// implementation does). Needs the host's publication list.
    /// </summary>
    private async Task<DocsTarget> ResolvePrettyAsync(string host, string relative, string reference, CancellationToken ct)
    {
        var low = relative.ToLowerInvariant();
        var catalog = await CatalogAsync(host, ct).ConfigureAwait(false);
        var matches = catalog.Publications
            .Where(publication => publication.PrettyUrl.Length > 0)
            .Where(publication => low == publication.PrettyUrl.ToLowerInvariant()
                                  || (low + "/").StartsWith(publication.PrettyUrl.ToLowerInvariant() + "/", StringComparison.Ordinal))
            .OrderByDescending(publication => publication.PrettyUrl.Length).ToArray();
        var target = "/r/" + low;
        foreach (var publication in matches)
        {
            if (low == publication.PrettyUrl.ToLowerInvariant()) return new DocsTarget(host, publication.Id, null);
            var root = await TocAsync(host, publication.Id, ct).ConfigureAwait(false);
            if (Walk(root).FirstOrDefault(node => node.PrettyUrl.ToLowerInvariant() == target) is { } page)
                return new DocsTarget(host, publication.Id, page.ContentId);
        }

        throw Unresolvable(reference, "no publication of the host has this reader URL");
    }

    public DocsTarget ParseCompact(string value)
    {
        var match = CompactReference().Match(value);
        if (!match.Success || (match.Groups[3].Success && !IsId(match.Groups[3].Value)) || !IsId(match.Groups[2].Value))
            throw new SiemensDocsException(WorkerFailureCategories.ValidationError,
                $"Invalid reference '{value}'. Use <prefix>:<mapId>[/<contentId>] (prefix tia, iox or an allowed host name) or an https reader URL; "
                + "references come from search, list_publications and get_toc.");
        var prefix = match.Groups[1].Value.ToLowerInvariant();
        var host = SiemensDocsOptions.Aliases.TryGetValue(prefix, out var aliased) ? aliased : prefix;
        if (!Options.IsAllowed(host))
            throw new SiemensDocsException(WorkerFailureCategories.ValidationError,
                $"Reference prefix '{match.Groups[1].Value}' names no allowed host. Allowed hosts: {string.Join(", ", Options.Hosts)} (aliases: tia, iox).");
        return new DocsTarget(host, match.Groups[2].Value, match.Groups[3].Success ? match.Groups[3].Value : null);
    }

    public static string CompactReference(string host, string mapId, string? contentId)
        => $"{SiemensDocsOptions.PrefixFor(host)}:{mapId}{(string.IsNullOrEmpty(contentId) ? string.Empty : "/" + contentId)}";

    /// <summary>The id form of a reader URL (the reader resolves it to the pretty URL).</summary>
    public static string ReaderUrl(string host, string mapId, string? contentId)
        => $"https://{host}/r/{Uri.EscapeDataString(mapId)}{(string.IsNullOrEmpty(contentId) ? string.Empty : "/" + Uri.EscapeDataString(contentId))}";

    /// <summary>A reader URL the API returned, kept only when it is https on an allowed host without user info or port.</summary>
    public string? SafeReaderUrl(string? value)
        => value is not null && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort && Options.IsAllowed(uri.IdnHost.ToLowerInvariant())
           && uri.AbsolutePath.StartsWith("/r/", StringComparison.Ordinal) && uri.AbsolutePath.Length > 3
            ? uri.AbsoluteUri
            : null;

    public static bool IsId(string value) => value.Length is > 0 and <= 128 && value is not ("." or "..") && IdShape().IsMatch(value);

    public static string Escape(string id) => Uri.EscapeDataString(id);

    // ------------------------------------------------------------------ internals

    private bool Fresh(DateTimeOffset loaded) => _time.GetUtcNow() - loaded <= CacheTtl;

    private async Task<HostCatalog> CatalogAsync(string host, CancellationToken ct)
    {
        if (_catalogs.TryGetValue(host, out var cached) && Fresh(cached.Loaded)) return cached.Catalog;
        var gate = _mapLocks.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_catalogs.TryGetValue(host, out cached) && Fresh(cached.Loaded)) return cached.Catalog;
            var raws = await _http.GetJsonAsync<List<RawMap?>>(host, "/api/khub/maps", ct, SiemensDocsHttp.MapsMaxBytes, SiemensDocsHttp.MapsTimeout)
                .ConfigureAwait(false);
            var catalog = new HostCatalog(host, raws);
            _catalogs[host] = (_time.GetUtcNow(), catalog);
            return catalog;
        }
        finally
        {
            gate.Release();
        }
    }

    internal static Dictionary<string, string> Metadata(List<RawMetadata>? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in metadata ?? new List<RawMetadata>())
            if (entry?.Key is { } key && !result.ContainsKey(key) && entry.Values?.FirstOrDefault(value => !string.IsNullOrEmpty(value)) is { } first)
                result[key] = first;
        return result;
    }

    internal static DocsPublication? ToPublication(string host, RawMap? raw)
    {
        if (raw?.Id is not { Length: > 0 } id) return null;
        var meta = Metadata(raw.Metadata);
        var versionKey = VersionKeys.FirstOrDefault(meta.ContainsKey) ?? string.Empty;
        var pretty = meta.GetValueOrDefault("ft:prettyUrl", string.Empty);
        if (pretty.StartsWith("/r/", StringComparison.Ordinal)) pretty = pretty[3..];
        return new DocsPublication(host, id, raw.Title ?? meta.GetValueOrDefault("ft:title", string.Empty), meta.GetValueOrDefault("ft:locale", string.Empty),
            meta.GetValueOrDefault("Product", string.Empty), versionKey.Length == 0 ? string.Empty : meta[versionKey], versionKey, pretty,
            meta.GetValueOrDefault("ft:lastPublication", string.Empty));
    }

    private static SiemensDocsException PublicationNotFound(string host, string mapId)
        => new(WorkerFailureCategories.TargetNotFound, $"Publication '{mapId}' was not found on {host}. Use search or list_publications to find a valid reference.");

    private static SiemensDocsException Unresolvable(string reference, string why)
        => new(WorkerFailureCategories.ValidationError, $"Could not resolve '{reference}': {why}. Use search or list_publications to find a valid reference.");

    private static SiemensDocsException Unknown(string host, string label, string value, IReadOnlyCollection<string> known)
    {
        var close = known.Where(candidate => candidate.Contains(value, StringComparison.OrdinalIgnoreCase)
                                             || value.Contains(candidate, StringComparison.OrdinalIgnoreCase)
                                             || Distance(candidate.ToLowerInvariant(), value.ToLowerInvariant()) <= 2)
            .Take(8).ToArray();
        var hint = close.Length > 0 ? $"Close matches: {string.Join(", ", close)}." : $"Valid values include: {string.Join(", ", known.Take(15))}.";
        return new SiemensDocsException(WorkerFailureCategories.ValidationError, $"Unknown {label} '{value}' on {host}. {hint}");
    }

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 3;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }

        return previous[b.Length];
    }

    [GeneratedRegex(@"^[\w~.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdShape();

    /// <summary>Fluid Topics map ids seen on both hosts are 22 characters of [A-Za-z0-9_~-]; pretty URL segments are not.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_~-]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex MapIdShape();

    [GeneratedRegex(@"^([A-Za-z0-9.-]+):([\w~.-]+)(?:/([\w~.-]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex CompactReference();

    private sealed class HostCatalog
    {
        public HostCatalog(string host, List<RawMap?> raws)
        {
            var publications = new List<DocsPublication>();
            var byId = new Dictionary<string, DocsPublication>(StringComparer.Ordinal);
            Versions = VersionKeys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
            foreach (var raw in raws)
            {
                if (ToPublication(host, raw) is not { } publication || byId.ContainsKey(publication.Id)) continue;
                publications.Add(publication);
                byId[publication.Id] = publication;
                var meta = Metadata(raw!.Metadata);
                var versions = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in VersionKeys)
                    if (meta.TryGetValue(key, out var version))
                    {
                        versions[key] = version;
                        if (!Versions[key].Contains(version, StringComparer.Ordinal)) Versions[key].Add(version);
                    }

                _mapVersions[publication.Id] = versions;
            }

            Publications = publications;
            ById = byId;
            Products = publications.Select(publication => publication.Product).Where(product => product.Length > 0).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            Locales = publications.Select(publication => publication.Locale).Where(locale => locale.Length > 0).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
        }

        private readonly Dictionary<string, Dictionary<string, string>> _mapVersions = new(StringComparer.Ordinal);

        public IReadOnlyList<DocsPublication> Publications { get; }
        public IReadOnlyDictionary<string, DocsPublication> ById { get; }
        public IReadOnlyList<string> Products { get; }
        public IReadOnlyList<string> Locales { get; }
        public Dictionary<string, List<string>> Versions { get; }

        public string? VersionOf(string mapId, string key)
            => _mapVersions.TryGetValue(mapId, out var versions) && versions.TryGetValue(key, out var version) ? version : null;
    }
}
