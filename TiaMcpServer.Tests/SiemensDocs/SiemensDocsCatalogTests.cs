using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.SiemensDocs;
using Xunit;
using static TiaMcpServer.Tests.SiemensDocs.SiemensDocsFixtures;

namespace TiaMcpServer.Tests.SiemensDocs;

public sealed class SiemensDocsCatalogTests
{
    private static (SiemensDocsCatalog Catalog, FixtureHandler Handler) Create()
    {
        var handler = Standard();
        var options = new SiemensDocsOptions(new[] { Tia, Iox }, "en-US", TimeSpan.Zero);
        return (new SiemensDocsCatalog(new SiemensDocsHttp(options, handler, delay: (_, _) => Task.CompletedTask)), handler);
    }

    [Fact]
    public async Task Toc_WithOneRoot_UsesPageTocForTheRootAndChildrenBelow()
    {
        var (catalog, _) = Create();
        var root = await catalog.TocAsync(Tia, OpennessV21, default);
        Assert.Equal("QaJz2AlV7QFrZCe4DSDmGA", root.ContentId);
        Assert.Equal(6, root.Children.Count);
        var path = SiemensDocsCatalog.FindPath(root, Connecting)!;
        Assert.Equal(new[] { "TIA Portal Openness: API for automation of engineering workflows", "TIA Portal Openness API", "General functions", "Connecting to the TIA Portal" },
            path.Select(node => node.Title));
        // In-page links name the TOC id; it finds the same page.
        Assert.Same(path[^1], SiemensDocsCatalog.FindPath(root, ConnectingTocId)![^1]);
    }

    [Fact]
    public async Task Toc_WithSeveralSections_GetsASyntheticRoot()
    {
        var (catalog, _) = Create();
        var root = await catalog.TocAsync(Iox, IoxHsc, default);
        Assert.Equal(string.Empty, root.ContentId);
        Assert.Equal(3, root.Children.Count);
        Assert.Equal("/r/en-us/ax/simatic-technology-objects/4.1.3", root.PrettyUrl);
        Assert.Equal(IoxHscOverview, root.Children[0].ContentId);
    }

    [Fact]
    public void Toc_WithoutPaginatedToc_IsDocsResponseInvalid()
    {
        var failure = Assert.Throws<SiemensDocsException>(() => SiemensDocsCatalog.ParseToc(new RawPages(), Tia, "/pages"));
        Assert.Equal(WorkerFailureCategories.DocsResponseInvalid, failure.Category);
    }

    [Fact]
    public async Task Toc_IsCached()
    {
        var (catalog, handler) = Create();
        await catalog.TocAsync(Tia, OpennessV21, default);
        await catalog.TocAsync(Tia, OpennessV21, default);
        Assert.Single(handler.List, request => request.PathAndQuery.EndsWith("/pages", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tia:gpR5ZkKnLSuzVoGX1ovoKg", Tia, OpennessV21, null)]
    [InlineData("TIA:gpR5ZkKnLSuzVoGX1ovoKg/DP7NDA58oox~t1~oZJkHQQ", Tia, OpennessV21, Connecting)]
    [InlineData("iox:YnqSGjg18sxz1X1czYtfcA/~_uoVimLbbt51ONhNyeIHw", Iox, IoxHsc, IoxHscOverview)]
    [InlineData("docs.tia.siemens.cloud:gpR5ZkKnLSuzVoGX1ovoKg", Tia, OpennessV21, null)]
    [InlineData("https://docs.tia.siemens.cloud/r/gpR5ZkKnLSuzVoGX1ovoKg/7g593pqgubN9AV3Dqy9FQQ?section=", Tia, OpennessV21, ConnectingTocId)]
    [InlineData("https://DOCS.TIA.SIEMENS.CLOUD/r/gpR5ZkKnLSuzVoGX1ovoKg/", Tia, OpennessV21, null)]
    public async Task References_ResolveToIds_WithoutLoadingTheCatalog(string reference, string host, string map, string? content)
    {
        var (catalog, handler) = Create();
        Assert.Equal(new DocsTarget(host, map, content), await catalog.ResolveAsync(reference, default));
        Assert.Empty(handler.List);
    }

    [Fact]
    public async Task PrettyReaderUrl_ResolvesThroughTheCatalogAndToc()
    {
        var (catalog, handler) = Create();
        var page = await catalog.ResolveAsync(
            $"https://docs.tia.siemens.cloud/r/{OpennessV21Pretty}/tia-portal-openness-api/general-functions/connecting-to-the-tia-portal", default);
        Assert.Equal(new DocsTarget(Tia, OpennessV21, Connecting), page);
        Assert.Equal(new DocsTarget(Tia, OpennessV21, null), await catalog.ResolveAsync($"https://docs.tia.siemens.cloud/r/{OpennessV21Pretty}", default));
        Assert.Contains(handler.List, request => request.PathAndQuery == "/api/khub/maps");
    }

    [Theory]
    [InlineData("http://docs.tia.siemens.cloud/r/gpR5ZkKnLSuzVoGX1ovoKg", "https")]
    [InlineData("https://docs.tia.siemens.cloud:8443/r/gpR5ZkKnLSuzVoGX1ovoKg", "port")]
    [InlineData("https://user:pw@docs.tia.siemens.cloud/r/gpR5ZkKnLSuzVoGX1ovoKg", "user info")]
    [InlineData("https://evil.example.com/r/gpR5ZkKnLSuzVoGX1ovoKg", "not allowed")]
    [InlineData("https://docs.tia.siemens.cloud.evil.example.com/r/gpR5ZkKnLSuzVoGX1ovoKg", "not allowed")]
    [InlineData("https://docs.tia.siemens.cloud/api/khub/maps", "/r/")]
    [InlineData("file:///etc/passwd", "https")]
    [InlineData("evil:gpR5ZkKnLSuzVoGX1ovoKg", "names no allowed host")]
    [InlineData("tia:../../internal", "Invalid reference")]
    [InlineData("tia:gpR5ZkKnLSuzVoGX1ovoKg/..", "Invalid reference")]
    [InlineData("tia:gpR5Zk KnL", "Invalid reference")]
    public async Task References_OutsideTheAllowedShapes_AreValidationErrors(string reference, string expected)
    {
        var (catalog, handler) = Create();
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => catalog.ResolveAsync(reference, default));
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
        Assert.Contains(expected, failure.Message);
        Assert.DoesNotContain(handler.List, request => request.Host != Tia && request.Host != Iox);
    }

    [Fact]
    public async Task UnknownPrettyUrl_IsAValidationError()
    {
        var (catalog, _) = Create();
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => catalog.ResolveAsync("https://docs.tia.siemens.cloud/r/en-us/v99/nothing", default));
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
    }

    [Fact]
    public async Task SharedPrettyUrl_TriesEveryPublication()
    {
        // Live 2026-11-03: two iox publications share en-us/notification-api; without a topic the longest/first match wins.
        var (catalog, _) = Create();
        var target = await catalog.ResolveAsync("https://docs.industrial-operations-x.siemens.cloud/r/en-us/notification-api", default);
        Assert.Contains(target.MapId, new[] { "fYJw6NgnY2FrqDNrm1E89Q", "2l9IiMfTcPSJWE7k_0vnPQ" });
    }

    [Fact]
    public async Task Publication_UsesTheSingleMapEndpoint_UntilTheCatalogIsLoaded()
    {
        var (catalog, handler) = Create();
        var publication = await catalog.PublicationAsync(Tia, OpennessV21, default);
        Assert.Equal(("TIAPortal", "V21", SiemensDocsCatalog.TiaVersionKey, "en-US"), (publication.Product, publication.Version, publication.VersionKey, publication.Locale));
        Assert.Equal(OpennessV21Pretty, publication.PrettyUrl);
        Assert.DoesNotContain(handler.List, request => request.PathAndQuery == "/api/khub/maps");
        var missing = await Assert.ThrowsAsync<SiemensDocsException>(() => catalog.PublicationAsync(Tia, "doesNotExist123", default));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.Category);
    }

    [Fact]
    public async Task Filters_AreCanonicalizedCaseInsensitively()
    {
        var (catalog, _) = Create();
        Assert.Equal("TIAPortal", await catalog.CanonicalProductAsync(Tia, "tiaportal", default));
        Assert.Equal((SiemensDocsCatalog.TiaVersionKey, "V21"), await catalog.CanonicalVersionAsync(Tia, "v21", default));
        Assert.Equal((SiemensDocsCatalog.SoftwareVersionKey, "21.00.00.00"), await catalog.CanonicalVersionAsync(Tia, "21.00.00.00", default));
        // iox publications carry SoftwareVersion only.
        Assert.Equal((SiemensDocsCatalog.SoftwareVersionKey, "4.1.3"), await catalog.CanonicalVersionAsync(Iox, "4.1.3", default));
        var unknown = await Assert.ThrowsAsync<SiemensDocsException>(() => catalog.CanonicalProductAsync(Tia, "TIAPortl", default));
        Assert.Equal(WorkerFailureCategories.ValidationError, unknown.Category);
        Assert.Contains("Close matches: TIAPortal", unknown.Message);
    }

    [Fact]
    public async Task Filter_CombinesProductVersionLocaleAndTitle()
    {
        var (catalog, _) = Create();
        var german = await catalog.FilterAsync(Tia, "TIAPortal", "V21", "de-de", null, default);
        Assert.Equal(new[] { OpennessV21De }, german.Select(publication => publication.Id));
        var titled = await catalog.FilterAsync(Tia, null, null, null, "sivarc", default);
        Assert.Equal("SiVArc Openness", Assert.Single(titled).Title);
        var all = await catalog.FilterAsync(Tia, null, null, null, null, default);
        Assert.Equal(8, all.Count);
        Assert.Equal(all.Select(p => p.Title).Order(StringComparer.OrdinalIgnoreCase), all.Select(p => p.Title));
    }

    [Fact]
    public void ReaderUrlFromTheApi_IsKeptOnlyForAllowedHttpsHosts()
    {
        var (catalog, _) = Create();
        Assert.NotNull(catalog.SafeReaderUrl("https://docs.tia.siemens.cloud/r/en-us/v21/x"));
        Assert.Null(catalog.SafeReaderUrl("https://evil.example.com/r/en-us/v21/x"));
        Assert.Null(catalog.SafeReaderUrl("javascript:alert(1)"));
        Assert.Null(catalog.SafeReaderUrl("http://docs.tia.siemens.cloud/r/x"));
    }

    [Fact]
    public void Host_AcceptsAliasesAndAllowedNamesOnly()
    {
        var (catalog, _) = Create();
        Assert.Equal(Tia, catalog.Host(null));
        Assert.Equal(Iox, catalog.Host("IOX"));
        Assert.Equal(Tia, catalog.Host("docs.tia.siemens.cloud"));
        Assert.Equal(WorkerFailureCategories.ValidationError, Assert.Throws<SiemensDocsException>(() => catalog.Host("support.industry.siemens.com")).Category);
    }
}
