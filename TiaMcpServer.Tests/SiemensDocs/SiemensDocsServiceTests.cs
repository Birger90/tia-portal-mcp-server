using System.Text.Json;
using TiaMcpServer.Contracts.Docs;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.SiemensDocs;
using Xunit;
using static TiaMcpServer.Tests.SiemensDocs.SiemensDocsFixtures;

namespace TiaMcpServer.Tests.SiemensDocs;

public sealed class SiemensDocsServiceTests
{
    private static SiemensDocsRequest Item(string operation) => new() { OperationId = "d", Operation = operation };

    private static async Task<JsonElement> Succeeded(SiemensDocsService service, SiemensDocsRequest item)
    {
        var result = await service.ExecuteAsync(item, default);
        Assert.True(result.Status == OperationBatchStatus.Succeeded, result.Failure?.Message);
        return result.Result!.Value;
    }

    [Fact]
    public async Task Search_PostsTheClusteredSearchBody_AndListsOtherVersions()
    {
        var handler = Standard();
        using var service = Service(handler);
        var item = Item("search");
        item.Query = "ExportAsDocuments";
        item.Limit = 3;
        var result = await Succeeded(service, item);

        var post = Assert.Single(handler.List, request => request.Method == "POST");
        Assert.Equal("/api/khub/clustered-search", post.PathAndQuery);
        Assert.Equal("""{"query":"ExportAsDocuments","contentLocale":"en-US","paging":{"page":1,"perPage":3}}""", post.Body);
        // No filter: the 16 MB publication list is not loaded.
        Assert.DoesNotContain(handler.List, request => request.PathAndQuery == "/api/khub/maps");

        Assert.Equal(8, result.GetProperty("totalResults").GetInt32());
        Assert.Equal(4, result.GetProperty("totalClusters").GetInt32());
        var hits = result.GetProperty("hits");
        Assert.Equal(3, hits.GetArrayLength());
        var first = hits[0];
        Assert.Equal("Exporting library type version as document", first.GetProperty("title").GetString());
        Assert.Equal("V21", first.GetProperty("version").GetString());
        Assert.Equal("TIAPortal", first.GetProperty("product").GetString());
        Assert.Equal("tia:gpR5ZkKnLSuzVoGX1ovoKg/wOh1SGzxD5Zbqx8K7ZTVzw", first.GetProperty("reference").GetString());
        Assert.StartsWith("https://docs.tia.siemens.cloud/r/en-us/v21/", first.GetProperty("readerUrl").GetString());
        Assert.StartsWith("...control. You can use the ExportAsDocuments() method", first.GetProperty("excerpt").GetString());
        Assert.Equal("V20", first.GetProperty("otherVersions")[0].GetProperty("version").GetString());
        // Live: the cluster's first entry may be V20 while V21 is the second (the reference showed only the first).
        Assert.Equal("V20", hits[2].GetProperty("version").GetString());
        Assert.Equal("V21", hits[2].GetProperty("otherVersions")[0].GetProperty("version").GetString());
    }

    [Fact]
    public async Task Search_WithFilters_CanonicalizesThemFromTheCatalog()
    {
        var handler = Standard();
        using var service = Service(handler);
        var item = Item("search");
        item.Query = "ExportAsDocuments";
        item.Product = "tiaportal";
        item.Version = "v21";
        item.Locale = "de-de";
        await Succeeded(service, item);
        var post = Assert.Single(handler.List, request => request.Method == "POST");
        Assert.Equal("""{"query":"ExportAsDocuments","contentLocale":"de-DE","paging":{"page":1,"perPage":10},"filters":[{"key":"Product","values":["TIAPortal"]},{"key":"tia:SoftwareVersionFilter","values":["V21"]}]}""",
            post.Body);
    }

    [Fact]
    public async Task Search_WithoutHits_SucceedsEmpty()
    {
        // Live 2026-11-03: an unknown contentLocale answers 200 with no results and currentPage 0.
        var handler = Standard();
        handler.Json("POST", Tia, "/api/khub/clustered-search", "search-empty.json");
        using var service = Service(handler);
        var item = Item("search");
        item.Query = "x";
        var result = await Succeeded(service, item);
        Assert.Equal(0, result.GetProperty("hits").GetArrayLength());
        Assert.Equal(0, result.GetProperty("totalResults").GetInt32());
        Assert.True(result.GetProperty("isLastPage").GetBoolean());
    }

    [Fact]
    public async Task Search_OnIox_UsesSoftwareVersion()
    {
        using var service = Service();
        var item = Item("search");
        item.Host = "iox";
        item.Query = "AxUnit";
        var result = await Succeeded(service, item);
        Assert.Equal(Iox, result.GetProperty("host").GetString());
        var hit = result.GetProperty("hits")[0];
        Assert.Equal("1.16.1", hit.GetProperty("version").GetString());
        Assert.StartsWith("iox:", hit.GetProperty("reference").GetString());
        Assert.Equal(2, hit.GetProperty("otherVersions").GetArrayLength());
    }

    [Fact]
    public async Task ListPublications_PagesWithOffset()
    {
        using var service = Service();
        var item = Item("list_publications");
        item.TitleContains = "openness";
        item.Limit = 2;
        var result = await Succeeded(service, item);
        Assert.Equal(6, result.GetProperty("total").GetInt32());
        Assert.Equal(2, result.GetProperty("returned").GetInt32());
        Assert.Equal(2, result.GetProperty("nextOffset").GetInt32());
        var row = result.GetProperty("publications")[0];
        Assert.Equal("Readme TIA Portal Openness", row.GetProperty("title").GetString());
        Assert.Equal("tia:5_QWRtE02wdb1Wg3xHZ4Nw", row.GetProperty("reference").GetString());
        Assert.Equal("https://docs.tia.siemens.cloud/r/5_QWRtE02wdb1Wg3xHZ4Nw", row.GetProperty("readerUrl").GetString());

        item.Offset = 5;
        var last = await Succeeded(service, item);
        Assert.Equal(1, last.GetProperty("returned").GetInt32());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("nextOffset").ValueKind);
    }

    [Fact]
    public async Task GetToc_OfAPublicationAndOfASubtree()
    {
        using var service = Service();
        var item = Item("get_toc");
        item.Reference = $"tia:{OpennessV21}";
        item.Depth = 1;
        var result = await Succeeded(service, item);
        Assert.Equal(7, result.GetProperty("total").GetInt32());
        Assert.Equal("V21", result.GetProperty("publication").GetProperty("version").GetString());
        var nodes = result.GetProperty("nodes");
        Assert.Equal(0, nodes[0].GetProperty("level").GetInt32());
        Assert.Equal(6, nodes[0].GetProperty("childCount").GetInt32());
        Assert.Equal("Cybersecurity information", nodes[1].GetProperty("title").GetString());

        item.Reference = $"tia:{OpennessV21}/UtNORpb3PLKA6EEl3zWl2Q";
        item.Depth = 6;
        item.Limit = 3;
        var subtree = await Succeeded(service, item);
        Assert.Equal(6, subtree.GetProperty("total").GetInt32());
        Assert.Equal(3, subtree.GetProperty("nextOffset").GetInt32());
        Assert.Equal("General functions", subtree.GetProperty("nodes")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetToc_OfAMultiSectionPublication_NamesTheSyntheticRootAfterThePublication()
    {
        using var service = Service();
        var item = Item("get_toc");
        item.Reference = $"iox:{IoxHsc}";
        item.Depth = 1;
        var result = await Succeeded(service, item);
        var root = result.GetProperty("nodes")[0];
        Assert.Equal("Siemens.Simatic.TechnologyObjects.HighSpeedCounter library", root.GetProperty("title").GetString());
        Assert.Equal($"iox:{IoxHsc}", root.GetProperty("reference").GetString());
        Assert.Equal(4, result.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetToc_UnknownTopic_IsTargetNotFound()
    {
        using var service = Service();
        var item = Item("get_toc");
        item.Reference = $"tia:{OpennessV21}/notInToc";
        var result = await service.ExecuteAsync(item, default);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, result.Failure!.Category);
    }

    [Fact]
    public async Task ReadPage_ByTocIdUrl_ReturnsMarkdownWithPublicationAndPath()
    {
        var handler = Standard();
        using var service = Service(handler);
        var item = Item("read_page");
        item.Reference = $"https://docs.tia.siemens.cloud/r/{OpennessV21}/{ConnectingTocId}";
        var result = await Succeeded(service, item);
        Assert.Equal("Connecting to the TIA Portal", result.GetProperty("title").GetString());
        Assert.Equal($"tia:{OpennessV21}/{Connecting}", result.GetProperty("reference").GetString());
        Assert.Equal(4, result.GetProperty("path").GetArrayLength());
        Assert.Equal("V21", result.GetProperty("publication").GetProperty("version").GetString());
        Assert.StartsWith("# Connecting to the TIA Portal", result.GetProperty("markdown").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("nextOffset").ValueKind);
        Assert.Equal(result.GetProperty("length").GetInt32(), result.GetProperty("returned").GetInt32());
        Assert.Contains(handler.List, request => request.PathAndQuery == $"/api/khub/maps/{OpennessV21}/topics/{Connecting}/content?target=DESIGNED_READER");
        Assert.DoesNotContain(handler.List, request => request.PathAndQuery == "/api/khub/maps");
    }

    [Fact]
    public async Task ReadPage_PagesWithOffset_AndCutsAtALineBreak()
    {
        using var service = Service();
        var item = Item("read_page");
        item.Reference = $"tia:{OpennessV21}/{Connecting}";
        item.MaxChars = 1000;
        var first = await Succeeded(service, item);
        var next = first.GetProperty("nextOffset").GetInt32();
        Assert.InRange(next, 800, 1000);
        Assert.EndsWith("\n", first.GetProperty("markdown").GetString());

        var whole = new System.Text.StringBuilder(first.GetProperty("markdown").GetString());
        int? offset = next;
        while (offset is { } at)
        {
            item.Offset = at;
            var page = await Succeeded(service, item);
            whole.Append(page.GetProperty("markdown").GetString());
            offset = page.GetProperty("nextOffset").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextOffset").GetInt32();
        }

        Assert.Equal(first.GetProperty("length").GetInt32(), whole.Length);

        item.Offset = 1_000_000;
        var past = await service.ExecuteAsync(item, default);
        Assert.Equal(WorkerFailureCategories.ValidationError, past.Failure!.Category);
    }

    [Fact]
    public async Task ReadPage_OfAPublication_ReadsItsRootTopic_OrTheFirstSection()
    {
        using var service = Service();
        var item = Item("read_page");
        item.Reference = $"iox:{IoxHsc}";
        var result = await Succeeded(service, item);
        Assert.Equal("High-Speed Counter overview", result.GetProperty("title").GetString());
        Assert.Contains("> **WARNING:**", result.GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task ReadPage_UnknownTopicOrPublication_IsTargetNotFound()
    {
        using var service = Service();
        var item = Item("read_page");
        item.Reference = $"tia:{OpennessV21}/doesNotExist123";
        Assert.Equal(WorkerFailureCategories.TargetNotFound, (await service.ExecuteAsync(item, default)).Failure!.Category);
        item.Reference = "tia:doesNotExist1234567890";
        Assert.Equal(WorkerFailureCategories.TargetNotFound, (await service.ExecuteAsync(item, default)).Failure!.Category);
    }

    [Theory]
    [InlineData(0, 10, "abcdefghij", 10)]
    [InlineData(0, 6, "abc\ndefghij", 6)]
    [InlineData(0, 5, "abcd\nefghij", 5)]
    public void Slice_PrefersALineBreakInTheLastFifth(int offset, int max, string text, int end)
        => Assert.Equal(end, SiemensDocsService.Slice(text, offset, max).End);

    [Fact]
    public void Slice_DoesNotSplitASurrogatePair()
    {
        var text = new string('a', 9) + "😀" + "tail";
        var (chunk, end) = SiemensDocsService.Slice(text, 0, 10);
        Assert.Equal(9, end);
        Assert.Equal(new string('a', 9), chunk);
    }
}

public sealed class SiemensDocsCatalogRulesTests
{
    private static string Error(OperationValidation validation)
    {
        Assert.False(validation.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, validation.Category);
        return validation.Error;
    }

    [Fact]
    public void OperationNames_MapToTheObserveDocsMethods_OutsideAnyProject()
    {
        Assert.Equal(SiemensDocsMethods.All.Order(StringComparer.Ordinal),
            SiemensDocsCatalogRules.OperationNames.Select(SiemensDocsCatalogRules.Method).Order(StringComparer.Ordinal));
        foreach (var method in SiemensDocsMethods.All)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
            Assert.Null(ProjectCapabilityCatalog.OperationFor(method));
            foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full })
                Assert.True(OperationPolicyCatalog.IsAllowed(mode, method));
        }
    }

    [Fact]
    public void Validation_RequiresTheOperationsMembers_AndRefusesForeignOnes()
    {
        Assert.Contains("search needs query", Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "search" } })));
        Assert.Contains("reference is required", Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "read_page" } })));
        Assert.Contains("'query' is not valid for get_toc",
            Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "get_toc", Reference = "tia:x", Query = "q" } })));
        Assert.Contains("unknown siemens_docs operation 'fetch'",
            Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "fetch" } })));
        Assert.Contains("maxChars must be 1000 to 40000",
            Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "read_page", Reference = "tia:x", MaxChars = 100_000 } })));
        Assert.Contains("limit must be 1 to 50",
            Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "search", Query = "q", Limit = 51 } })));
        Assert.Contains("depth must be 1 to 6",
            Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "get_toc", Reference = "tia:x", Depth = 7 } })));
        Assert.Contains("locale must be a culture name",
            Error(SiemensDocsCatalogRules.Validate(new[] { new SiemensDocsRequest { OperationId = "a", Operation = "search", Query = "q", Locale = "english" } })));
        Assert.Contains("Batch must contain at least one operation", Error(SiemensDocsCatalogRules.Validate(Array.Empty<SiemensDocsRequest>())));
        Assert.True(SiemensDocsCatalogRules.Validate(new[]
        {
            new SiemensDocsRequest { OperationId = "a", Operation = "search", Query = "q", Host = "iox", Product = "SIMATIC AX", Version = "6.1", Locale = "en-US", Limit = 50, Page = 2 },
            new SiemensDocsRequest { OperationId = "b", Operation = "read_page", Reference = "tia:x/y", Offset = 0, MaxChars = 40_000 },
            new SiemensDocsRequest { OperationId = "c", Operation = "get_toc", Reference = "tia:x", Depth = 6, Limit = 1000, Offset = 5 },
            new SiemensDocsRequest { OperationId = "d", Operation = "list_publications", TitleContains = "Openness", Limit = 200, Offset = 0 },
        }).IsValid);
    }
}
