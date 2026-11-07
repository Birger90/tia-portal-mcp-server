using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Docs;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.SiemensDocs;

public static class SiemensDocsContractVersion
{
    public const string Current = "1.0";
}

/// <summary>One siemens_docs item. Members the operation does not use must be omitted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SiemensDocsRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    [Description("search only (required): full-text query, 1 to 500 characters, for example \"ExportAsDocuments\" or \"TSEND_C parameters\".")]
    public string? Query { get; set; }

    [Description("search, list_publications: documentation host, tia (docs.tia.siemens.cloud, default), iox (docs.industrial-operations-x.siemens.cloud) or another host the server allows. read_page and get_toc take the host from reference.")]
    public string? Host { get; set; }

    [Description("search, list_publications: product filter matched case-insensitively against the host's publication list (for example TIAPortal, WinCC, SIMATIC AX); an unknown value fails with close matches.")]
    public string? Product { get; set; }

    [Description("search, list_publications: version filter (for example V21 or V20 on tia, 6.1 on iox), matched against tia:SoftwareVersionFilter, then SoftwareVersion.")]
    public string? Version { get; set; }

    [Description("search, list_publications: content locale such as en-US or de-DE (default: the server's --docs-locale, en-US). list_publications filters by locale only when it is given.")]
    public string? Locale { get; set; }

    [Description("list_publications only: case-insensitive substring of the publication title.")]
    public string? TitleContains { get; set; }

    [Description("read_page, get_toc (required): a reference from search, list_publications or get_toc (tia:<mapId>[/<contentId>], iox:...) or an https reader URL of an allowed host (https://docs.tia.siemens.cloud/r/...). It is resolved to ids; the URL itself is never fetched.")]
    public string? Reference { get; set; }

    [Description("search: hits per page, 1 to 50 (default 10). list_publications: rows, 1 to 200 (default 50). get_toc: nodes, 1 to 1000 (default 300).")]
    public int? Limit { get; set; }

    [Description("search only: result page, 1 to 100 (default 1).")]
    public int? Page { get; set; }

    [Description("read_page: character offset into the page's Markdown (default 0; use nextOffset of the previous call). list_publications, get_toc: index of the first row (default 0; use nextOffset).")]
    public int? Offset { get; set; }

    [Description("read_page only: characters of Markdown returned, 1000 to 40000 (default 20000).")]
    public int? MaxChars { get; set; }

    [Description("get_toc only: levels below the start node, 1 to 6 (default 2).")]
    public int? Depth { get; set; }
}

/// <summary>Public operation catalog of siemens_docs: names, accepted members, validation before any network activity.</summary>
public static class SiemensDocsCatalogRules
{
    public const int MaxQueryChars = 500;
    public const int MaxReferenceChars = 2048;
    public const int MaxFilterChars = 200;
    public const int MaxSearchLimit = 50;
    public const int DefaultSearchLimit = 10;
    public const int MaxSearchPage = 100;
    public const int MaxPublications = 200;
    public const int DefaultPublications = 50;
    public const int MaxTocNodes = 1000;
    public const int DefaultTocNodes = 300;
    public const int MaxTocDepth = 6;
    public const int DefaultTocDepth = 2;
    public const int MinChars = 1000;
    public const int MaxChars = 40_000;
    public const int DefaultChars = 20_000;

    private static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["search"] = new[] { "query", "host", "product", "version", "locale", "limit", "page" },
        ["list_publications"] = new[] { "host", "product", "version", "locale", "titleContains", "limit", "offset" },
        ["get_toc"] = new[] { "reference", "depth", "limit", "offset" },
        ["read_page"] = new[] { "reference", "offset", "maxChars" },
    };

    public static IReadOnlyCollection<string> OperationNames => Fields.Keys.ToArray();

    public static string Method(string operation) => SiemensDocsMethods.ForOperation(operation);

    public static OperationValidation Validate(IReadOnlyList<SiemensDocsRequest?>? operations)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError)
            {
                errors.Add(itemError);
                continue;
            }

            var prefix = $"{item!.OperationId}: ";
            if (string.IsNullOrWhiteSpace(item.Operation))
            {
                errors.Add(prefix + "operation is required.");
                continue;
            }

            if (!Fields.TryGetValue(item.Operation, out var allowed))
            {
                errors.Add(prefix + $"unknown siemens_docs operation '{item.Operation}'. Valid operations: {string.Join(", ", Fields.Keys)}.");
                continue;
            }

            foreach (var (name, present) in Members(item))
                if (present && !allowed.Contains(name, StringComparer.Ordinal))
                    errors.Add(prefix + $"'{name}' is not valid for {item.Operation}.");
            ValidateItem(item, errors, prefix);
        }

        return errors.Count == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    public static IReadOnlyList<string> ValidateAccessMode(IEnumerable<IOperationBatchItem> operations, McpAccessMode mode)
        => operations
            .Where(item => !OperationPolicyCatalog.IsAllowed(mode, Method(item.Operation)))
            .Select(item => $"Operation '{item.Operation}' is not permitted in {McpAccessModeNames.ToName(mode)} mode.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static (string Name, bool Present)[] Members(SiemensDocsRequest item) => new[]
    {
        ("query", item.Query is not null), ("host", item.Host is not null), ("product", item.Product is not null), ("version", item.Version is not null),
        ("locale", item.Locale is not null), ("titleContains", item.TitleContains is not null), ("reference", item.Reference is not null),
        ("limit", item.Limit is not null), ("page", item.Page is not null), ("offset", item.Offset is not null), ("maxChars", item.MaxChars is not null),
        ("depth", item.Depth is not null),
    };

    private static void ValidateItem(SiemensDocsRequest item, List<string> errors, string prefix)
    {
        switch (item.Operation)
        {
            case "search":
                if (string.IsNullOrWhiteSpace(item.Query) || item.Query.Length > MaxQueryChars)
                    errors.Add(prefix + $"search needs query: 1 to {MaxQueryChars} characters.");
                Range(item.Limit, 1, MaxSearchLimit, "limit", errors, prefix);
                Range(item.Page, 1, MaxSearchPage, "page", errors, prefix);
                break;
            case "list_publications":
                Range(item.Limit, 1, MaxPublications, "limit", errors, prefix);
                Range(item.Offset, 0, int.MaxValue, "offset", errors, prefix);
                Filter(item.TitleContains, "titleContains", errors, prefix);
                break;
            case "get_toc":
                Reference(item.Reference, errors, prefix);
                Range(item.Depth, 1, MaxTocDepth, "depth", errors, prefix);
                Range(item.Limit, 1, MaxTocNodes, "limit", errors, prefix);
                Range(item.Offset, 0, int.MaxValue, "offset", errors, prefix);
                break;
            case "read_page":
                Reference(item.Reference, errors, prefix);
                Range(item.Offset, 0, int.MaxValue, "offset", errors, prefix);
                Range(item.MaxChars, MinChars, MaxChars, "maxChars", errors, prefix);
                break;
        }

        Filter(item.Host, "host", errors, prefix);
        Filter(item.Product, "product", errors, prefix);
        Filter(item.Version, "version", errors, prefix);
        if (item.Locale is not null && SiemensDocsOptions.NormalizeLocale(item.Locale) is null)
            errors.Add(prefix + "locale must be a culture name such as en-US or de-DE.");
    }

    private static void Reference(string? value, List<string> errors, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxReferenceChars)
            errors.Add(prefix + $"reference is required: a reference such as tia:<mapId>/<contentId> or an https reader URL (at most {MaxReferenceChars} characters).");
    }

    private static void Filter(string? value, string name, List<string> errors, string prefix)
    {
        if (value is not null && (value.Trim().Length == 0 || value.Length > MaxFilterChars))
            errors.Add(prefix + $"{name} must be nonblank and at most {MaxFilterChars} characters.");
    }

    private static void Range(int? value, int min, int max, string name, List<string> errors, string prefix)
    {
        if (value is { } number && (number < min || number > max))
            errors.Add(prefix + (max == int.MaxValue ? $"{name} must be {min} or more." : $"{name} must be {min} to {max}."));
    }
}

/// <summary>A publication in a result.</summary>
public sealed record SiemensDocsPublicationInfo(
    string Title,
    string Product,
    string Version,
    string Locale,
    string LastPublication,
    string Reference,
    string ReaderUrl);

/// <summary>The same topic in another publication or version (another entry of the search cluster).</summary>
public sealed record SiemensDocsHitVariant(string Publication, string Version, string Reference);

public sealed record SiemensDocsSearchHit(
    string Title,
    string Publication,
    string Product,
    string Version,
    IReadOnlyList<string> Breadcrumb,
    string Excerpt,
    string Reference,
    string ReaderUrl,
    IReadOnlyList<SiemensDocsHitVariant> OtherVersions);

public sealed record SiemensDocsSearchResult(
    string Host,
    string Query,
    string Locale,
    int Page,
    int TotalResults,
    int TotalClusters,
    bool IsLastPage,
    IReadOnlyList<SiemensDocsSearchHit> Hits);

public sealed record SiemensDocsPublicationsResult(
    string Host,
    int Total,
    int Offset,
    int Returned,
    int? NextOffset,
    IReadOnlyList<SiemensDocsPublicationInfo> Publications);

public sealed record SiemensDocsTocNodeInfo(int Level, string Title, string Reference, string ReaderUrl, int ChildCount);

public sealed record SiemensDocsTocResult(
    SiemensDocsPublicationInfo Publication,
    string Start,
    int Depth,
    int Total,
    int Offset,
    int Returned,
    int? NextOffset,
    IReadOnlyList<SiemensDocsTocNodeInfo> Nodes);

public sealed record SiemensDocsPageResult(
    SiemensDocsPublicationInfo Publication,
    IReadOnlyList<string> Path,
    string Title,
    string Reference,
    string ReaderUrl,
    int Length,
    int Offset,
    int Returned,
    int? NextOffset,
    string Markdown);
