namespace TiaMcpServer.SiemensDocs;

// The Fluid Topics JSON the reader consumes, reduced to the members it uses. Shapes were checked against
// docs.tia.siemens.cloud and docs.industrial-operations-x.siemens.cloud on 2026-11-03 (trimmed responses are the
// test fixtures in TiaMcpServer.Tests/SiemensDocs/Fixtures); unknown members are ignored.

/// <summary>One entry of <c>GET /api/khub/maps</c> (an array) or the body of <c>GET /api/khub/maps/{id}</c>.</summary>
internal sealed class RawMap
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public List<RawMetadata>? Metadata { get; set; }
}

internal sealed class RawMetadata
{
    public string? Key { get; set; }
    public List<string?>? Values { get; set; }
}

/// <summary><c>GET /api/khub/maps/{id}/pages</c>: <c>paginatedToc</c> holds one root, or several top-level sections.</summary>
internal sealed class RawPages
{
    public List<RawTocNode?>? PaginatedToc { get; set; }
}

/// <summary>A TOC node; a root keeps its children in <c>pageToc</c> (with an empty <c>children</c>), deeper nodes in <c>children</c>.</summary>
internal sealed class RawTocNode
{
    public string? TocId { get; set; }
    public string? ContentId { get; set; }
    public string? Title { get; set; }
    public string? PrettyUrl { get; set; }
    public List<RawTocNode?>? PageToc { get; set; }
    public List<RawTocNode?>? Children { get; set; }
}

/// <summary><c>POST /api/khub/clustered-search</c>.</summary>
internal sealed class RawSearch
{
    public List<RawCluster?>? Results { get; set; }
    public RawPaging? Paging { get; set; }
}

internal sealed class RawPaging
{
    public int CurrentPage { get; set; }
    public bool IsLastPage { get; set; }
    public int TotalResultsCount { get; set; }
    public int TotalClustersCount { get; set; }
}

/// <summary>One cluster: the same topic in several publications or versions (one entry each).</summary>
internal sealed class RawCluster
{
    public List<RawEntry?>? Entries { get; set; }
}

internal sealed class RawEntry
{
    public string? Type { get; set; }
    public RawTopic? Topic { get; set; }
}

internal sealed class RawTopic
{
    public string? MapId { get; set; }
    public string? ContentId { get; set; }
    public string? TocId { get; set; }
    public string? Title { get; set; }
    public string? MapTitle { get; set; }
    public List<string?>? Breadcrumb { get; set; }
    public string? HtmlExcerpt { get; set; }
    public string? ReaderUrl { get; set; }
    public List<RawMetadata>? Metadata { get; set; }
}
