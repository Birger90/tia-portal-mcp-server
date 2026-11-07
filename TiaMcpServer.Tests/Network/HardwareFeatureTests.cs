using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Package G hardware features (network_read / network_write) against the hardware-modules FakeWorker stand-ins: subnets of a node,
/// IO controller, IM connection, PC interface assignment, module description, telecontrol data points, communication connections.
/// Not live-tested.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class HardwareFeatureTests
{
    private static object Segment(int index, string name, int position, string? type) => new { index, name, positionNumber = position, typeIdentifier = type };

    private static object Interface(int index, string name, int position) => new
    {
        kind = "deviceItem", deviceName = "PLC_1",
        itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(0, "PLC_1", 1, "OrderNumber:6ES7 516-3AN01-0AB0/V2.8"), Segment(index, name, position, null) },
    };

    private static object Di => new
    {
        kind = "deviceItem", deviceName = "PLC_1",
        itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(1, "DI 16x24VDC_1", 2, "OrderNumber:6ES7 521-1BH00-0AB0/V2.1") },
    };

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, params object[] operations)
    {
        var reply = await harness.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["operations"] = operations });
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Result(JsonElement root, int index = 0) => root.GetProperty("batch").GetProperty("operations")[index].GetProperty("result");

    [Fact]
    public void Catalog_Policy_AndShapes()
    {
        foreach (var method in HardwareFeatureMethods.Read)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
            Assert.Contains(method, HardwareModuleMethods.PublicRead);
        }

        foreach (var method in HardwareFeatureMethods.Write)
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
        Assert.Equal(typeof(HardwareFeatureResultInfo), HardwareModulePayloadContract.ResultType(HardwareFeatureMethods.CreateCommunicationConnection));
    }

    [Fact]
    public async Task Mcp_Features_ReadWriteAndReadBack()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "hardware-modules");
        var x1 = Interface(0, "PROFINET interface_1", 32768);
        var x2 = Interface(1, "PROFINET interface_2", 33024);

        var nodes = await CallAsync(harness, "network_read", new { operationId = "n", operation = "get_interface_nodes", target = x1 });
        Assert.Contains("connected subnet (none)", Result(nodes).GetProperty("items")[0].GetString());
        var created = await CallAsync(harness, "network_write", new { operationId = "s", operation = "create_subnet_and_connect_node", target = x1, newSubnetName = "PN/IE_9" });
        Assert.True(created.GetProperty("success").GetBoolean(), created.GetRawText());
        Assert.Contains(created.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == "network_hardware_feature_changes");
        Assert.Equal("matched", Result(created).GetProperty("readBack").GetString());
        var disconnected = await CallAsync(harness, "network_write", new { operationId = "d", operation = "disconnect_node_from_subnets", target = x1 });
        Assert.True(disconnected.GetProperty("success").GetBoolean(), disconnected.GetRawText());

        var controller = await CallAsync(harness, "network_read", new { operationId = "c", operation = "get_io_controller", target = x1 });
        Assert.Contains("PROFINET IO-System (100)", Result(controller).GetProperty("items")[0].GetString());

        var im = await CallAsync(harness, "network_write", new { operationId = "i", operation = "connect_im_port", target = x1, partner = x2 });
        Assert.True(im.GetProperty("success").GetBoolean(), im.GetRawText());
        var imOff = await CallAsync(harness, "network_write", new { operationId = "o", operation = "disconnect_im_port", target = x1 });
        Assert.True(imOff.GetProperty("success").GetBoolean(), imOff.GetRawText());

        var assign = await CallAsync(harness, "network_write", new { operationId = "a", operation = "assign_pc_interface", target = x1, assignmentMode = "PcStation" });
        Assert.True(assign.GetProperty("success").GetBoolean(), assign.GetRawText());
        var assignment = await CallAsync(harness, "network_read", new { operationId = "g", operation = "get_pc_interface_assignment", target = x1 });
        Assert.Contains("IPC expansion 1", Result(assignment).GetRawText());

        var update = await CallAsync(harness, "network_write", new { operationId = "u", operation = "update_module_description", target = Di });
        Assert.True(update.GetProperty("success").GetBoolean(), update.GetRawText());
        var again = await CallAsync(harness, "network_write", new { operationId = "u2", operation = "update_module_description", target = Di });
        Assert.Equal("postcondition_failed", again.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());

        var connection = await CallAsync(harness, "network_write", new { operationId = "cc", operation = "create_communication_connection", target = x1, partner = x2, connectionType = "S7Connection" });
        Assert.True(connection.GetProperty("success").GetBoolean(), connection.GetRawText());
        var list = await CallAsync(harness, "network_read", new { operationId = "l", operation = "list_communication_connections", target = x1 });
        Assert.Contains("S7Connection_1", Result(list).GetProperty("items")[0].GetString());
        var wrongName = await CallAsync(harness, "network_write", new { operationId = "w", operation = "delete_communication_connection", target = x1, connectionIndex = 0, connectionName = "Other" });
        Assert.Equal("state_changed", wrongName.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());
        var deleted = await CallAsync(harness, "network_write", new { operationId = "x", operation = "delete_communication_connection", target = x1, connectionIndex = 0, connectionName = "S7Connection_1" });
        Assert.True(deleted.GetProperty("success").GetBoolean(), deleted.GetRawText());

        var directory = Path.Combine(Path.GetTempPath(), "tiamcp-telecontrol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "points.csv");
            var exported = await CallAsync(harness, "network_write", new { operationId = "e", operation = "export_telecontrol_data_points", target = Di, filePath = file });
            Assert.True(exported.GetProperty("success").GetBoolean(), exported.GetRawText());
            Assert.True(File.Exists(file));
            var imported = await CallAsync(harness, "network_write", new { operationId = "m", operation = "import_telecontrol_data_points", target = Di, filePath = file });
            Assert.True(imported.GetProperty("success").GetBoolean(), imported.GetRawText());
            Assert.Contains("dataPointsAfter", Result(imported).GetRawText());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        var missing = await CallAsync(harness, "network_read", new { operationId = "z", operation = "get_im_partner_port", target = Di });
        Assert.Equal("unsupported_capability", missing.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());
    }
}
