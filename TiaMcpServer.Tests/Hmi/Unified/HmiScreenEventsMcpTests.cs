using System.Text.Json;
using ModelContextProtocol.Client;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.UnifiedExtensions;

/// <summary>Unified events and dynamizations (issue #29) through MCP and the FakeWorker.</summary>
[Collection("Mcp protocol serial")]
public class HmiScreenEventsMcpTests
{
    private const string Scenario = "hmi-write-roundtrip";

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        Assert.True(reply.StructuredContent is not null, string.Join(" ", reply.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)));
        return NetworkGuardedWriteMcpTests.Document(reply);
    }

    private static JsonElement Item(JsonElement document, int index) => document.GetProperty("batch").GetProperty("operations")[index];

    [Fact]
    public async Task EventsAndDynamizationsAreWrittenAndRead()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        object[] writes =
        [
            new { operationId = "tap", operation = "set_event_script", hmiName = "H", screenName = "Main", itemName = "Button_1", eventType = "Tapped", scriptCode = "HMIRuntime.Trace('x');" },
            new { operationId = "change", operation = "set_event_script", hmiName = "H", screenName = "Main", itemName = "Button_1", propertyName = "Visible", eventType = "Change", scriptCode = "v();" },
            new { operationId = "dyn", operation = "set_dynamization_script", hmiName = "H", screenName = "Main", itemName = "Button_1", propertyName = "BackColor", scriptCode = "return 0;", triggerType = "T1s" },
            new { operationId = "flash", operation = "set_dynamization_flashing", hmiName = "H", screenName = "Main", itemName = "Button_1", propertyName = "ForeColor", color = "#FF0000", flashingRate = "Fast" },
            new { operationId = "drop", operation = "delete_dynamization", hmiName = "H", screenName = "Main", itemName = "Button_1", propertyName = "ForeColor" },
        ];

        var preview = await CallAsync(harness, "hmi_write", writes, dryRun: true);
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Contains(preview.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == HmiGuardDefinitions.DeletesScripting);
        Assert.Equal("flash", preview.GetProperty("effects")[4].GetProperty("effect").GetProperty("dependsOn").GetString());

        var applied = await CallAsync(harness, "hmi_write", writes);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());

        var read = await CallAsync(harness, "hmi_read",
        [
            new { operationId = "e", operation = "list_screen_item_events", hmiName = "H", screenName = "Main", itemName = "Button_1" },
            new { operationId = "d", operation = "list_screen_item_dynamizations", hmiName = "H", screenName = "Main", itemName = "Button_1" },
        ]);
        var events = Item(read, 0).GetProperty("result");
        Assert.Equal("HMIRuntime.Trace('x');", events.GetProperty("events")[0].GetProperty("scriptCode").GetString());
        Assert.Equal("Visible", events.GetProperty("propertyEvents")[0].GetProperty("propertyName").GetString());
        var dynamizations = Item(read, 1).GetProperty("result").GetProperty("dynamizations");
        Assert.Equal("BackColor", Assert.Single(dynamizations.EnumerateArray()).GetProperty("propertyName").GetString());
    }

    [Fact]
    public async Task UnknownEventTypeFailsTheItemAndShapesAreRefusedByTheCatalog()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

        var bad = await CallAsync(harness, "hmi_write", [new { operationId = "x", operation = "set_event_script", hmiName = "H", screenName = "Main", itemName = "Button_1", eventType = "Clicked", scriptCode = "x();" }]);

        Assert.Equal("validation_error", Item(bad, 0).GetProperty("failure").GetProperty("category").GetString());
        static string? Error(HmiWriteOperationRequest item) => HmiWriteCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("colour", Error(new() { OperationId = "a", Operation = "set_dynamization_flashing", ScreenName = "Main", PropertyName = "BackColor", Color = "red" }));
        Assert.Contains("'triggerTags' must hold", Error(new() { OperationId = "a", Operation = "set_dynamization_script", ScreenName = "Main", PropertyName = "P", ScriptCode = "x", TriggerTags = new() }));
        Assert.Contains("CustomCycle", Error(new() { OperationId = "a", Operation = "set_dynamization_script", ScreenName = "Main", PropertyName = "P", ScriptCode = "x", TriggerType = "T1s", CustomDuration = "5" }));
        Assert.Contains("enum member name", Error(new() { OperationId = "a", Operation = "delete_event_script", ScreenName = "Main", EventType = "Tap ped" }));
    }
}
