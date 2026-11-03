using ModelContextProtocol.Client;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.Screens;

/// <summary>The WinCC Unified screen, script and batch rename writes of hmi_write (adopted from BetterTIA/writer), through MCP and the FakeWorker.</summary>
[Collection("Mcp protocol serial")]
public class HmiScreenWriteMcpTests
{
    private const string Scenario = "hmi-write-roundtrip";

    private static async Task<System.Text.Json.JsonElement> WriteAsync(McpProtocolTestHarness harness, object[] operations, bool dryRun = false)
        => NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write",
            new Dictionary<string, object?> { ["operations"] = operations, ["dryRun"] = dryRun }));

    [Fact]
    public async Task ScreenItemPropertyDynamizationAndScriptWritesRunInOrder()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        object[] operations =
        [
            new { operationId = "screen", operation = "create_screen", screenName = "Detail", width = 1024, height = 768 },
            new { operationId = "item", operation = "create_screen_item", screenName = "Detail", itemName = "Start", itemType = "HmiButton", left = 10, top = 20, width = 100, height = 40 },
            new { operationId = "colour", operation = "set_screen_item_property", screenName = "Detail", itemName = "Start", propertyName = "BackColor", value = "#FF0000" },
            new { operationId = "dyn", operation = "set_dynamization_tag", screenName = "Detail", itemName = "Start", propertyName = "Visible", tagName = "Pump_On" },
            new { operationId = "script", operation = "replace_in_scripts", replacements = new[] { new { find = "'Detail'", replace = "'Overview'" } } },
        ];

        var preview = await WriteAsync(harness, operations, dryRun: true);
        var applied = await WriteAsync(harness, operations);

        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        var effects = applied.GetProperty("effects");
        Assert.Equal("screen", effects[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal("item", effects[2].GetProperty("effect").GetProperty("dependsOn").GetString());
        var results = applied.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "Create", "Width", "Height" }, results[0].GetProperty("result").GetProperty("appliedProperties").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("Main event Loaded ScriptCode", results[4].GetProperty("result").GetProperty("appliedProperties")[0].GetString());
        Assert.True(applied.GetProperty("verification").GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task RenameScreensAndTagsSwapAndAMissingItemTypeFailsWithApiMemberMissing()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var applied = await WriteAsync(harness,
        [
            new { operationId = "a", operation = "create_screen", screenName = "Detail" },
            new { operationId = "swap", operation = "rename_screens", renames = new[] { new { name = "Main", newName = "Detail" }, new { name = "Detail", newName = "Main" } } },
            new { operationId = "tag", operation = "create_tag", tableName = "Existing", tagName = "T1", dataType = "Bool" },
            new { operationId = "tags", operation = "rename_tags", renames = new[] { new { name = "T1", newName = "T2" } } },
        ]);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Contains(applied.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == HmiGuardDefinitions.RenameUpdatesNoReferences);
        Assert.Equal(4, applied.GetProperty("batch").GetProperty("operations")[1].GetProperty("result").GetProperty("appliedProperties").GetArrayLength());

        var missing = await WriteAsync(harness, [new { operationId = "x", operation = "create_screen_item", screenName = "Main", itemName = "Gauge", itemType = "HmiGauge" }]);
        Assert.Equal("api_member_missing", missing.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public void Catalog_RefusesBadShapes()
    {
        static string? Error(HmiWriteOperationRequest item) => HmiWriteCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("'itemType' must be the Openness type name", Error(new HmiWriteOperationRequest { OperationId = "i", Operation = "create_screen_item", ScreenName = "S", ItemName = "I", ItemType = "Hmi Button" }));
        Assert.Contains("'renames' must hold", Error(new HmiWriteOperationRequest { OperationId = "r", Operation = "rename_tags", Renames = new() }));
        Assert.Contains("appears twice", Error(new HmiWriteOperationRequest { OperationId = "r", Operation = "rename_screens", Renames = new() { new() { Name = "A", NewName = "X" }, new() { Name = "B", NewName = "x" } } }));
        Assert.Contains("'replacements' must hold", Error(new HmiWriteOperationRequest { OperationId = "s", Operation = "replace_in_scripts", Replacements = new() }));
        Assert.Contains("'propertyName' is required", Error(new HmiWriteOperationRequest { OperationId = "p", Operation = "set_screen_item_property", ScreenName = "S", Value = "1" }));
    }
}
