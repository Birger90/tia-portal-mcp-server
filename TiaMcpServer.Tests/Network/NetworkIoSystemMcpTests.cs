using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// K28 end to end through the MCP surface against the stateful <c>network-io-system</c> FakeWorker fixture: create a subnet,
/// connect the CPU node and the ET 200SP node, create the PROFINET IO system on the CPU node, attach the ET 200SP node to it.
/// Object access only; create_io_system is not live-tested.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class NetworkIoSystemMcpTests
{
    private const string Scenario = "network-io-system";

    private static object Node(string deviceName, string nodeId, string ownerName, int ownerPosition, string rackType, string ownerType) => new
    {
        kind = "node",
        deviceName,
        nodeId,
        interfacePath = new object[]
        {
            new { name = "Rack_0", positionNumber = 0, typeIdentifier = rackType },
            new { name = ownerName, positionNumber = ownerPosition, typeIdentifier = ownerType },
        },
    };

    private static object PlcNode => Node("PLC_1", "node-plc", "PLC_1", 1, "System:Rack.S71500", "OrderNumber:6ES7 516-3AN01-0AB0/V2.8");

    private static object EtNode => Node("ET200SP_1", "node-et", "IM 155-6 PN ST", 0, "System:Rack.ET200SP", "OrderNumber:6ES7 155-6AU01-0BN0/V4.1");

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

    private static string[] GuardIds(JsonElement root) => root.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    private static Task<McpProtocolTestHarness> StartAsync(TempAuditDirectory audit)
        => McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

    [Fact]
    public async Task CreateSubnet_ConnectNodes_CreateIoSystem_AttachTheEt200sp()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);

        var subnet = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "subnet", operation = "create_subnet", subnet = new { name = "PN/IE_1", networkType = "Ethernet" } },
        });
        Assert.True(subnet.GetProperty("success").GetBoolean(), subnet.GetRawText());
        var subnetId = Result(subnet).GetProperty("subnetId").GetString()!;

        var connect = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "plc", operation = "configure_network_device", target = PlcNode, changes = new { subnet = new { subnetId } } },
            new { operationId = "et", operation = "configure_network_device", target = EtNode, changes = new { subnet = new { subnetId } } },
        });
        Assert.True(connect.GetProperty("success").GetBoolean(), connect.GetRawText());

        var create = new { operationId = "io", operation = "create_io_system", target = PlcNode, ioSystem = new { name = "PROFINET IO-System" } };
        var preview = await CallAsync(harness, "network_write", new object[] { create }, dryRun: true);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        Assert.Contains("network_io_system_create_unverified", GuardIds(preview));
        Assert.DoesNotContain("network_io_system_node_unconnected", GuardIds(preview));
        var effect = preview.GetProperty("effects")[0].GetProperty("effect");
        Assert.Equal(subnetId, effect.GetProperty("currentSettings").GetProperty("Subnet").GetString());
        Assert.Equal("PROFINET IO-System", effect.GetProperty("requestedSettings").GetProperty("ioSystemName").GetString());
        Assert.Equal("node-plc", effect.GetProperty("target").GetProperty("nodeId").GetString());

        var created = await CallAsync(harness, "network_write", new object[] { create });
        Assert.True(created.GetProperty("success").GetBoolean(), created.GetRawText());
        var result = Result(created);
        Assert.Equal(subnetId, result.GetProperty("subnetId").GetString());
        Assert.Equal("PROFINET IO-System", result.GetProperty("name").GetString());
        var number = result.GetProperty("number").GetInt32();
        Assert.Equal("trusted", created.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());

        var attach = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "attach", operation = "configure_network_device", target = EtNode, changes = new { ioSystem = new { subnetId, number } } },
        });
        Assert.True(attach.GetProperty("success").GetBoolean(), attach.GetRawText());
        Assert.Equal(number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Result(attach).GetProperty("appliedSettings").GetProperty("IoSystem").GetString());

        var read = await CallAsync(harness, "network_read", new object[] { new { operationId = "hw", operation = "read_hardware_config" } });
        var ioSystem = Assert.Single(Result(read).GetProperty("subnets")[0].GetProperty("ioSystems").EnumerateArray());
        Assert.Equal(number, ioSystem.GetProperty("number").GetInt32());
        Assert.Equal("PLC_1", ioSystem.GetProperty("ioControllerName").GetString());
        Assert.Equal(new[] { "ET200SP_1" }, ioSystem.GetProperty("connectedDeviceNames").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public async Task CreateIoSystem_OnAnUnconnectedNode_InformsAndTiaPortalDecides()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "io", operation = "create_io_system", target = PlcNode },
        });

        Assert.Contains("network_io_system_node_unconnected", GuardIds(root));
        Assert.False(root.GetProperty("success").GetBoolean());
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("engineering_rejected", item.GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task CreateIoSystem_OnANodeWithoutIoController_FailsTheItem()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var subnet = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "subnet", operation = "create_subnet", subnet = new { name = "PN/IE_1", networkType = "Ethernet" } },
        });
        var subnetId = Result(subnet).GetProperty("subnetId").GetString()!;
        await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "et", operation = "configure_network_device", target = EtNode, changes = new { subnet = new { subnetId } } },
        });

        var root = await CallAsync(harness, "network_write", new object[] { new { operationId = "io", operation = "create_io_system", target = EtNode } });

        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("target_kind_unsupported", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("no IO controller", item.GetProperty("failure").GetProperty("message").GetString());
    }

    [Fact]
    public async Task CreateIoSystem_WithADeviceItemTarget_IsAValidationError()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new
            {
                operationId = "io", operation = "create_io_system",
                target = new { kind = "deviceItem", deviceName = "PLC_1", itemPath = new[] { new { index = 0, name = "Rack_0", positionNumber = 0, typeIdentifier = "System:Rack.S71500" } } },
            },
        });
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("'target.kind' must be 'node'", root.GetProperty("error").GetProperty("message").GetString());
    }
}
