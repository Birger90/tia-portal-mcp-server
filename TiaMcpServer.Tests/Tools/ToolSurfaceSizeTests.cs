using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

/// <summary>
/// K91: Claude Desktop refused plc_read and plc_write ("Claude can't use 2 tools from Tia portal because of a problem with
/// that connector") while their input schemas were 77-79 KB with 20-50 KB descriptions; network_write (32 KB schema, 8.8 KB
/// description) was accepted. The client's exact limit is unknown, so every tool stays clearly below the accepted ones:
/// a description of at most 8,000 characters, a compact input schema of at most 27,000 characters (about 30 KB as
/// Python's json.dumps spaces it) and no deeper nesting than before.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public class ToolSurfaceSizeTests
{
    internal const int MaxDescription = 8000;
    internal const int MaxCompactInputSchema = 27000;

    /// <summary>The nesting depth each large tool had before K91; none may grow.</summary>
    private static readonly IReadOnlyDictionary<string, int> DepthBefore = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["plc_write"] = 11, ["plc_read"] = 10, ["network_write"] = 12, ["network_read"] = 12,
        ["library_write"] = 13, ["library_read"] = 13, ["hmi_write"] = 9, ["hmi_read"] = 7,
    };

    private static int Depth(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => 1 + element.EnumerateObject().Select(property => Depth(property.Value)).DefaultIfEmpty(0).Max(),
        JsonValueKind.Array => 1 + element.EnumerateArray().Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0,
    };

    [Fact]
    public async Task EveryTool_StaysBelowTheClientLimits()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full);
        var tools = await harness.Client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "plc_write");
        Assert.All(tools, tool =>
        {
            var schema = tool.ProtocolTool.InputSchema;
            var compact = JsonSerializer.Serialize(schema);
            Assert.True((tool.Description ?? "").Length <= MaxDescription, $"{tool.Name}: description {(tool.Description ?? "").Length}");
            Assert.True(compact.Length <= MaxCompactInputSchema, $"{tool.Name}: input schema {compact.Length}");
            Assert.True(Depth(schema) <= 13, $"{tool.Name}: depth {Depth(schema)}");
            if (DepthBefore.TryGetValue(tool.Name, out var before)) Assert.True(Depth(schema) <= before, $"{tool.Name}: depth {Depth(schema)} > {before}");
        });
    }

    [Fact]
    public async Task PlcItemSchema_KeepsTheShapeButShortensTheFieldDescriptions()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full);
        var tools = await harness.Client.ListToolsAsync();
        foreach (var name in new[] { "plc_read", "plc_write" })
        {
            var item = tools.Single(tool => tool.Name == name).ProtocolTool.InputSchema
                .GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("properties");
            Assert.Contains("describe_operations", item.GetProperty("operation").GetProperty("description").GetString());
            Assert.Equal("Optional PLC software name.", item.GetProperty("plcName").GetProperty("description").GetString());
            Assert.True(item.TryGetProperty("operationNames", out _));
            Assert.All(item.EnumerateObject(), field =>
                Assert.True(!field.Value.TryGetProperty("description", out var description) || description.GetString()!.Length <= 160, field.Name));
        }

        var obEventClass = tools.Single(tool => tool.Name == "plc_write").ProtocolTool.InputSchema.GetProperty("properties")
            .GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("obEventClass");
        Assert.True(obEventClass.TryGetProperty("enum", out _));
    }
}
