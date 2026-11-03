using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcAnalysis;

/// <summary>
/// plc_read program analysis and plc_online_read read_watch_table_live end to end over MCP against
/// the <c>program-analysis</c> FakeWorker fixture (payload shapes only, no Siemens evidence) and the
/// in-memory FakePlcWebApiWorld.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcAnalysisMcpTests
{
    private const string Scenario = "program-analysis";

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations)
    {
        var reply = await harness.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["operations"] = operations });
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Item(JsonElement root, int index) => root.GetProperty("batch").GetProperty("operations")[index];

    private static JsonElement Result(JsonElement root, int index)
    {
        var item = Item(root, index);
        Assert.True(item.GetProperty("status").GetString() == "succeeded", item.GetRawText());
        return item.GetProperty("result");
    }

    private static string[] Names(JsonElement array, string member = "name")
        => array.EnumerateArray().Select(e => e.GetProperty(member).GetString()!).ToArray();

    [Fact]
    public async Task PlcRead_AnalysesShareOneEvidenceSnapshotPerCall()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path, startupProjectPath: Scenario);
        var root = await CallAsync(harness, "plc_read", new object[]
        {
            new { operationId = "tree", operation = "get_call_tree", blockPath = "PLC_1/Main" },
            new { operationId = "paths", operation = "find_call_paths", blockPath = "PLC_1/Blocks/Motors/FB_Motor" },
            new { operationId = "unused", operation = "find_unused_blocks" },
            new { operationId = "orphans", operation = "find_orphaned_instance_dbs" },
            new { operationId = "type", operation = "analyze_change_impact", typePath = "PLC_1/Types/UDT_Inner" },
            new { operationId = "tag", operation = "analyze_change_impact", tableName = "Default tag table", name = "Start" },
            new { operationId = "db", operation = "get_call_tree", blockPath = "PLC_1/Settings_DB" },
        });

        var tree = Result(root, 0);
        Assert.True(tree.GetProperty("complete").GetBoolean(), tree.GetRawText());
        var nodes = tree.GetProperty("nodes");
        Assert.Equal(new[] { "Main", "FB_Line", "FB_Motor", "TON", "FC_Util", "FC_Recursive", "FC_Recursive", "Unit_FC" },
            nodes.EnumerateArray().Select(n => n.GetProperty("block").GetProperty("name").GetString()));
        Assert.Equal("Line_DB", nodes[0].GetProperty("instances")[0].GetProperty("block").GetProperty("name").GetString());
        Assert.Equal(new[] { "call", "multiInstance" }, nodes[2].GetProperty("evidence").GetProperty("relations").EnumerateArray().Select(r => r.GetString()));
        Assert.False(nodes[3].GetProperty("block").GetProperty("inInventory").GetBoolean());
        Assert.True(nodes[6].GetProperty("cycle").GetBoolean());
        Assert.Equal("PLC_1/Units/Unit_A/Blocks/Unit_FC", nodes[7].GetProperty("block").GetProperty("path").GetString());
        Assert.Equal(new[] { "uses", "usedBy" }.Order(StringComparer.Ordinal),
            nodes[7].GetProperty("evidence").GetProperty("reportedAs").EnumerateArray().Select(r => r.GetString()!));
        Assert.Equal("FC_Maybe", tree.GetProperty("undirectedCallEvidence")[0].GetProperty("second").GetString());
        var evidence = tree.GetProperty("evidence");
        Assert.Equal(2, evidence.GetProperty("crossReferenceQueries").GetArrayLength());
        Assert.NotEmpty(evidence.GetProperty("limits").EnumerateArray());

        var paths = Result(root, 1);
        Assert.Equal(new[] { "Main", "FB_Line", "FB_Motor" }, paths.GetProperty("paths")[0].GetProperty("blocks").EnumerateArray().Select(b => b.GetString()));
        Assert.Equal("call+multiInstance", paths.GetProperty("paths")[0].GetProperty("relations")[1].GetString());

        var unused = Result(root, 2);
        Assert.Equal(new[] { "Dead_DB", "FB_Unused", "FC_Dead", "FC_Old", "FC_Orphan", "Old_DB", "Sys_FB", "Timer_DB" },
            unused.GetProperty("unused").EnumerateArray().Select(u => u.GetProperty("block").GetProperty("name").GetString()));
        Assert.Equal("referencedOnlyByUnreachableBlocks", unused.GetProperty("unused")[2].GetProperty("category").GetString());
        Assert.Equal("HMI_Tag_1", unused.GetProperty("usedOnlyOutsideProgram")[0].GetProperty("externalUsers")[0].GetProperty("name").GetString());
        Assert.Equal("FC_Maybe", unused.GetProperty("undetermined")[0].GetProperty("block").GetProperty("name").GetString());
        Assert.Equal(new[] { "Cyclic", "Main" }, unused.GetProperty("roots").EnumerateArray().Select(r => r.GetString()));
        // An undetermined block makes the listing incomplete, with its limit and the not-proof warning.
        Assert.False(unused.GetProperty("complete").GetBoolean());
        Assert.Contains(unused.GetProperty("evidence").GetProperty("limits").EnumerateArray(), l => l.GetString()!.StartsWith("1 block(s) are undetermined", StringComparison.Ordinal));
        Assert.Contains(Item(root, 2).GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("not proof that blocks can be deleted", StringComparison.Ordinal));

        var orphans = Result(root, 3);
        Assert.Equal(4, orphans.GetProperty("instanceDbCount").GetInt32());
        Assert.Equal(3, orphans.GetProperty("checkedCount").GetInt32());
        Assert.Equal(1, orphans.GetProperty("orphanCount").GetInt32());
        Assert.Equal(new[] { ("Dead_DB", "instanceOfUnreachable", "FB_Unused") },
            orphans.GetProperty("orphans").EnumerateArray().Select(o => (o.GetProperty("block").GetProperty("name").GetString()!,
                o.GetProperty("category").GetString()!, o.GetProperty("instanceOfName").GetString()!)));
        // A father instance outside the inventory (a system FB/SFB, an IEC timer type, or a deleted FB) is not an orphan.
        Assert.Equal(1, orphans.GetProperty("unresolvedInstanceOfCount").GetInt32());
        Assert.Equal(new[] { ("Old_DB", "instanceOfNotInInventory", "FB_Gone") },
            orphans.GetProperty("unresolvedInstanceOf").EnumerateArray().Select(o => (o.GetProperty("block").GetProperty("name").GetString()!,
                o.GetProperty("category").GetString()!, o.GetProperty("instanceOfName").GetString()!)));
        Assert.Equal("noInstanceOfName", orphans.GetProperty("notChecked")[0].GetProperty("category").GetString());
        Assert.False(orphans.GetProperty("complete").GetBoolean());

        var type = Result(root, 4);
        Assert.Equal(new[] { "UDT_Settings", "Settings_DB", "Main" }, type.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("object").GetProperty("name").GetString()));
        Assert.Equal(new[] { "Main" }, type.GetProperty("affectedRoots").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(1, type.GetProperty("directUserCount").GetInt32());

        var tag = Result(root, 5);
        Assert.Equal("tag", tag.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(new[] { ("FB_Motor", 1), ("FB_Line", 2), ("Main", 3) },
            tag.GetProperty("users").EnumerateArray().Select(u => (u.GetProperty("object").GetProperty("name").GetString()!, u.GetProperty("depth").GetInt32())));
        Assert.Equal("Screen_1", tag.GetProperty("externalUsers")[0].GetProperty("name").GetString());

        var db = Item(root, 6);
        Assert.Equal("failed", db.GetProperty("status").GetString());
        Assert.Equal("target_kind_unsupported", db.GetProperty("failure").GetProperty("category").GetString());

        var methods = log.Methods();
        Assert.Equal(1, methods.Count(m => m == "browse_project_tree_v3_snapshot"));
        // Two block folders, two type folders and the tag: each read once for the whole call.
        Assert.Equal(5, methods.Count(m => m == "read_cross_references"));
        Assert.Equal(4, methods.Count(m => m == "get_block_content"));
    }

    [Fact]
    public async Task PlcRead_AnalysisTargetsAreResolvedBeforeAnySweep()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path, startupProjectPath: Scenario);
        var root = await CallAsync(harness, "plc_read", new object[]
        {
            new { operationId = "missing", operation = "find_call_paths", blockPath = "PLC_1/Nope" },
            new { operationId = "table", operation = "analyze_change_impact", tableName = "Nope", name = "Start" },
            new { operationId = "plc", operation = "find_unused_blocks", plcName = "PLC_9" },
        });
        Assert.Equal(new[] { "target_not_found", "target_not_found", "target_not_found" },
            Enumerable.Range(0, 3).Select(i => Item(root, i).GetProperty("failure").GetProperty("category").GetString()));
        Assert.DoesNotContain("read_cross_references", log.Methods());
    }

    [Fact]
    public async Task PlcOnlineRead_ReadsAWatchTableLiveAndListsUnmappedEntries()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path, startupProjectPath: Scenario);
        var plc = harness.PlcWebApiWorld.Plc("Line1");
        plc.Tags["\"Motor_On\""] = "true";
        plc.Tags["\"Speed\""] = "42";
        plc.Tags["\"Start\""] = "false";
        var root = await CallAsync(harness, "plc_online_read", new object[]
        {
            new { operationId = "watch", operation = "read_watch_table_live", target = "Line1", table = "Watch_1" },
            new { operationId = "tags", operation = "read_watch_table_live", target = "Line1", tagTable = "Default tag table", representation = "simple" },
            new { operationId = "nope", operation = "read_watch_table_live", target = "Nope", table = "Watch_1" },
        });

        var watch = Item(root, 0);
        Assert.Equal("failed", watch.GetProperty("status").GetString());
        Assert.Equal("webapi_rejected", watch.GetProperty("failure").GetProperty("category").GetString());
        var result = watch.GetProperty("result");
        Assert.Equal(("watchTable", 4, 4, 3), (result.GetProperty("sourceKind").GetString(), result.GetProperty("mapped").GetInt32(),
            result.GetProperty("unmapped").GetInt32(), result.GetProperty("read").GetInt32()));
        var entries = result.GetProperty("entries");
        Assert.Equal(new[] { "\"Motor_On\"", "\"Speed\"", "\"DB_Line\".Speed", null, null, "\"DB_Line\".Missing", null, null },
            entries.EnumerateArray().Select(e => e.GetProperty("webApiName").GetString()));
        Assert.Equal(new[] { "plcTag", "plcTag", "webApiNotation", null, null, "webApiNotation", null, null },
            entries.EnumerateArray().Select(e => e.GetProperty("mapping").GetString()));
        Assert.Equal(42, entries[1].GetProperty("value").GetInt32());
        Assert.Equal(1500, entries[2].GetProperty("value").GetInt32());
        Assert.Contains("absolute address", entries[3].GetProperty("unmappedReason").GetString());
        Assert.Equal(200, entries[5].GetProperty("webApiError").GetProperty("code").GetInt32());
        Assert.Contains("Web API", entries[7].GetProperty("unmappedReason").GetString());
        // Unmapped names never reach the PLC.
        Assert.DoesNotContain(harness.PlcWebApiWorld.CallsFor("Line1"), call => call.Contains("%MW20") || call.Contains("DB_Line.Speed") || call.Contains("Sub"));

        var tags = Result(root, 1);
        Assert.Equal(("tagTable", 2, 2), (tags.GetProperty("sourceKind").GetString(), tags.GetProperty("mapped").GetInt32(), tags.GetProperty("unmapped").GetInt32()));
        Assert.Equal(new[] { "\"Start\"", "\"Motor_On\"", null, null }, tags.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("webApiName").GetString()));
        Assert.Contains("user constant", tags.GetProperty("entries")[3].GetProperty("unmappedReason").GetString());
        Assert.Contains(Item(root, 1).GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("could not be mapped"));

        Assert.Equal("target_not_found", Item(root, 2).GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task PlcOnlineRead_LiveTableNeedsABoundProject()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path);
        // The injected worker client is a service, never a tool argument.
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "plc_online_read");
        Assert.Equal(new[] { "operations" }, tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        var root = await CallAsync(harness, "plc_online_read", new object[]
        {
            new { operationId = "watch", operation = "read_watch_table_live", target = "Line1", table = "Watch_1" },
        });
        Assert.Equal("binding_conflict", Item(root, 0).GetProperty("failure").GetProperty("category").GetString());
        Assert.Empty(harness.PlcWebApiWorld.CallsFor("Line1"));
    }

    [Theory]
    [InlineData("{\"operationId\":\"a\",\"operation\":\"read_watch_table_live\",\"target\":\"Line1\"}", "exactly one of table")]
    [InlineData("{\"operationId\":\"a\",\"operation\":\"read_watch_table_live\",\"target\":\"Line1\",\"table\":\"W\",\"tagTable\":\"T\"}", "exactly one of table")]
    [InlineData("{\"operationId\":\"a\",\"operation\":\"read_watch_table_live\",\"target\":\"Line1\",\"table\":\"W\",\"count\":0}", "count must be 1 to 1000")]
    [InlineData("{\"operationId\":\"a\",\"operation\":\"read_tags\",\"target\":\"Line1\",\"names\":[\"x\"],\"table\":\"W\"}", "'table' is not valid for read_tags")]
    public void Catalog_ValidatesLiveTableItems(string json, string expected)
    {
        var item = JsonSerializer.Deserialize<TiaMcpServer.PlcWebApi.PlcOnlineReadRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var validation = TiaMcpServer.PlcWebApi.PlcWebApiCatalog.ValidateRead(new[] { item });
        Assert.False(validation.IsValid);
        Assert.Contains(expected, validation.Error);
    }
}
