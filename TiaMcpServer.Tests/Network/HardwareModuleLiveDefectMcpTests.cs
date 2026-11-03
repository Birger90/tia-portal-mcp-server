using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// The live defects of 2026-11-02 through the MCP surface against the <c>hardware-modules</c> FakeWorker fixture: K05 (a module
/// plugged into a rack lands under the device), K29 (CanPlugNew false on a device target names the rack item) and K06 (a
/// module's addresses on a sub-item without type identifier). Object access only; no Siemens evidence.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class HardwareModuleLiveDefectMcpTests
{
    private const string Scenario = "hardware-modules";

    private static object Segment(int index, string name, int position, string type) => new { index, name, positionNumber = position, typeIdentifier = type };

    private static object Rack => new { kind = "deviceItem", deviceName = "PLC_1", itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500") } };

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Result(JsonElement root, int index = 0)
    {
        var item = root.GetProperty("batch").GetProperty("operations")[index];
        Assert.True(item.GetProperty("status").GetString() == "succeeded", item.GetRawText());
        return item.GetProperty("result");
    }

    private static Task<McpProtocolTestHarness> StartAsync(TempAuditDirectory audit)
        => McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

    [Fact]
    public async Task Plug_IntoTheRackPlacedUnderTheDevice_SucceedsAndReportsPlacedUnder()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        const string type = "OrderNumber:DeviceLevel 6ES7 521-1BL00-0AB0/V2.2";
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "plug", operation = "plug_device_item", target = Rack, typeIdentifier = type, positionNumber = 4, deviceItemName = "DI_32_Test" },
        });

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var result = Result(root);
        Assert.Empty(result.GetProperty("placedUnder").GetProperty("itemPath").EnumerateArray());
        Assert.Equal("PLC_1", result.GetProperty("placedUnder").GetProperty("deviceName").GetString());
        var path = result.GetProperty("result").GetProperty("itemPath");
        Assert.Equal(("DI_32_Test", 1), (Assert.Single(path.EnumerateArray()).GetProperty("name").GetString(), path[0].GetProperty("index").GetInt32()));
        Assert.Contains("directly under device 'PLC_1'", result.GetProperty("messages")[0].GetString());

        var read = await CallAsync(harness, "network_read", new object[] { new { operationId = "hw", operation = "read_hardware_config" } });
        Assert.Equal(2, Result(read).GetProperty("devices")[0].GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Plug_IntoTheRackReportsTheContainerAsPlacedUnder()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "plug", operation = "plug_device_item", target = Rack, typeIdentifier = "OrderNumber:6ES7 521-1BL00-0AB0/V2.1", positionNumber = 4 },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("Rack_0", Result(root).GetProperty("placedUnder").GetProperty("itemPath")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Plug_DeviceTargetRefusedByCanPlugNew_SuggestsTheRackItem()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "plug", operation = "plug_device_item", target = new { kind = "device", deviceName = "PLC_1" },
                typeIdentifier = "OrderNumber:Unpluggable 6ES7 136-6BA01-0CA0/V2.0", positionNumber = 1 },
        }, dryRun: true);

        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        var message = root.GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("CanPlugNew returned false", message);
        Assert.Contains("Modules usually plug into the rack item", message);
        Assert.Contains("itemPath: [{ index: 0, name: 'Rack_0', positionNumber: 0, typeIdentifier: 'System:Rack.S71500' }]", message);
    }

    [Fact]
    public async Task SubItemAddresses_AreListedWithTheirPathAndCanBeWritten()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        const string type = "OrderNumber:SubItemAddresses 6ES7 521-1BL00-0AB0/V2.2";
        var plug = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "plug", operation = "plug_device_item", target = Rack, typeIdentifier = type, positionNumber = 4, deviceItemName = "DI_32" },
        });
        Assert.True(plug.GetProperty("success").GetBoolean(), plug.GetRawText());
        var module = new
        {
            kind = "deviceItem", deviceName = "PLC_1",
            itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(3, "DI_32", 4, type) },
        };

        var list = await CallAsync(harness, "network_read", new object[] { new { operationId = "addresses", operation = "list_io_addresses", target = module } });
        var address = Assert.Single(Result(list).GetProperty("addresses").EnumerateArray());
        Assert.Equal(("Input", 10, 4), (address.GetProperty("ioType").GetString(), address.GetProperty("startAddress").GetInt32(), address.GetProperty("length").GetInt32()));
        var segment = Assert.Single(address.GetProperty("subItemPath").EnumerateArray());
        Assert.Equal("DI_32", segment.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, segment.GetProperty("typeIdentifier").ValueKind);

        var write = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "addr", operation = "set_io_address", target = module, address = new { ioType = "Input", startAddress = 10 }, addressChanges = new { startAddress = 40 } },
        });
        Assert.True(write.GetProperty("success").GetBoolean(), write.GetRawText());
        Assert.Equal("DI_32", write.GetProperty("effects")[0].GetProperty("effect").GetProperty("currentSettings").GetProperty("subItemPath").GetString());
        var after = Assert.Single(Result(write).GetProperty("after").EnumerateArray());
        Assert.Equal(40, after.GetProperty("startAddress").GetInt32());
    }
}
