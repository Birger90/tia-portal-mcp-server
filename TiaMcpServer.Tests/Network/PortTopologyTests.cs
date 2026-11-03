using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Issue #10: PROFINET port topology (network_read list_port_connections, network_write connect_ports / disconnect_ports). The
/// Siemens-free run against stand-in ports, the catalog and policy, and the hardware-modules FakeWorker end to end. Not
/// live-tested: whether ConnectedPorts ("Internal use only") reads back a link and how TIA Portal rejects an existing link are
/// decided only live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PortTopologyTests
{
    private sealed class Port
    {
        private readonly List<Port> _connected = new();

        public bool Ignore { get; set; }

        public bool Unreadable { get; set; }

        public IReadOnlyList<Port> ConnectedPorts => Unreadable ? throw new Boom() : _connected;

        public void ConnectToPort(Port partner)
        {
            if (Ignore) return;
            _connected.Add(partner);
        }

        public void DisconnectFromPort(Port partner)
        {
            if (!Ignore) _connected.Remove(partner);
        }
    }

    private sealed class Boom : Exception;

    private static HardwarePortConnectionResultInfo NewResult() => new();

    [Fact]
    public void Run_ConnectAndDisconnect_AreReadBack()
    {
        var port = new Port();
        var partner = new Port();
        var connected = NewResult();
        PortTopologyRun.Run(port, partner, connect: true, connected, _ => false);
        Assert.Equal((false, true), (connected.ConnectedBefore, connected.ConnectedAfter));

        var disconnected = NewResult();
        PortTopologyRun.Run(port, partner, connect: false, disconnected, _ => false);
        Assert.Equal((true, false), (disconnected.ConnectedBefore, disconnected.ConnectedAfter));
    }

    [Fact]
    public void Run_IgnoredCall_IsPostconditionFailed_AndTheSamePortIsRefused()
    {
        var port = new Port { Ignore = true };
        var failed = Assert.Throws<RuntimeBindingException>(() => PortTopologyRun.Run(port, new Port(), connect: true, NewResult(), _ => false));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failed.Category);

        var same = Assert.Throws<RuntimeBindingException>(() => PortTopologyRun.Run(port, port, connect: true, NewResult(), _ => false));
        Assert.Equal(WorkerFailureCategories.ValidationError, same.Category);

        var missing = Assert.Throws<RuntimeBindingException>(() => PortTopologyRun.Run(new object(), new object(), connect: true, NewResult(), _ => true));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, missing.Category);
    }

    [Fact]
    public void Run_UnreadableConnectedPorts_LeavesTheResultUnverified()
    {
        var port = new Port { Unreadable = true };
        var result = NewResult();
        PortTopologyRun.Run(port, new Port(), connect: true, result, exception => exception is Boom);
        Assert.Null(result.ConnectedBefore);
        Assert.Null(result.ConnectedAfter);
        Assert.Contains(result.Messages, message => message.Contains("not verified", StringComparison.Ordinal));

        var (partners, error) = PortTopologyRun.Partners(port, exception => exception is Boom);
        Assert.Null(partners);
        Assert.Contains("ConnectedPorts", error);
        // A non-recoverable failure is never turned into "unreadable".
        Assert.ThrowsAny<Exception>(() => PortTopologyRun.Partners(port, _ => false));
    }

    [Fact]
    public void Catalog_RequiresAPartnerDeviceItem()
    {
        var port = new NetworkObjectTarget
        {
            Kind = "deviceItem", DeviceName = "PLC_1",
            ItemPath = new[] { new NetworkDeviceItemPathSegment { Index = 0, Name = "Rack_0", PositionNumber = 0, TypeIdentifier = "System:Rack.S71500" } },
        };
        var device = new NetworkObjectTarget { Kind = "device", DeviceName = "PLC_1" };
        NetworkOperationRequest Op(string operation, NetworkObjectTarget target, NetworkObjectTarget? partner)
            => new() { OperationId = "p", Operation = operation, Target = target, Partner = partner };

        Assert.True(NetworkOperationCatalog.ValidateRead(new[] { Op(HardwareModuleMethods.ListPortConnections, device, null) }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateRead(new[] { Op(HardwareModuleMethods.ListPortConnections, device, port) }).IsValid);
        Assert.True(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.ConnectPorts, port, port) }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.ConnectPorts, port, null) }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.DisconnectPorts, port, device) }).IsValid);
        Assert.False(NetworkOperationCatalog.ValidateWrite(new[] { Op(HardwareModuleMethods.DisconnectPorts, device, port) }).IsValid);
    }

    [Fact]
    public void Policy_ReadObserves_WritesMutate_AndGateAsNetworkTools()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(HardwareModuleMethods.ListPortConnections));
        Assert.Equal("network_read", ProjectCapabilityCatalog.OperationFor(HardwareModuleMethods.ListPortConnections));
        foreach (var method in new[] { HardwareModuleMethods.ConnectPorts, HardwareModuleMethods.DisconnectPorts })
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("network_write", ProjectCapabilityCatalog.OperationFor(method));
        }
    }

    private static object Segment(int index, string name, int position, string type) => new { index, name, positionNumber = position, typeIdentifier = type };

    private static object PortItem(int index, string name, int position) => new
    {
        kind = "deviceItem", deviceName = "PLC_1",
        itemPath = new[]
        {
            Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(0, "PLC_1", 1, "OrderNumber:6ES7 516-3AN01-0AB0/V2.8"),
            Segment(0, "PROFINET interface_1", 32768, "System:InterfacePN"), Segment(index, name, position, "System:PortPN"),
        },
    };

    private static object Dq => new
    {
        kind = "deviceItem", deviceName = "PLC_1",
        itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(2, "DQ 16x24VDC_1", 3, "OrderNumber:6ES7 522-1BH00-0AB0/V2.1") },
    };

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Item(JsonElement root, int index = 0) => root.GetProperty("batch").GetProperty("operations")[index];

    [Fact]
    public async Task Mcp_ListConnectAndDisconnect_ThePortsOfTheCpu()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "hardware-modules");

        var list = await CallAsync(harness, "network_read", new object[] { new { operationId = "l", operation = "list_port_connections", target = new { kind = "device", deviceName = "PLC_1" } } });
        var ports = Item(list).GetProperty("result").GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
        Assert.Equal("Port_1", ports[0].GetProperty("port").GetProperty("name").GetString());
        Assert.Equal("PROFINET interface_1", ports[0].GetProperty("interfaceOwner").GetProperty("name").GetString());
        Assert.Equal(0, ports[0].GetProperty("connectedPorts").GetArrayLength());

        var connect = new { operationId = "c", operation = "connect_ports", target = PortItem(0, "Port_1", 32769), partner = PortItem(1, "Port_2", 32770) };
        var preview = await CallAsync(harness, "network_write", new object[] { connect }, dryRun: true);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        Assert.Equal("false", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("currentSettings").GetProperty("connected").GetString());

        var applied = await CallAsync(harness, "network_write", new object[] { connect });
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.True(Item(applied).GetProperty("result").GetProperty("connectedAfter").GetBoolean());

        var read = await CallAsync(harness, "network_read", new object[] { new { operationId = "l", operation = "list_port_connections", target = PortItem(0, "Port_1", 32769) } });
        var partner = Assert.Single(Item(read).GetProperty("result").GetProperty("ports")[0].GetProperty("connectedPorts").EnumerateArray());
        Assert.Equal("Port_2", partner.GetProperty("port").GetProperty("name").GetString());

        // An existing link is reported (info) and still passed to TIA Portal, which rejects it here.
        var again = await CallAsync(harness, "network_write", new object[] { connect });
        Assert.Contains(again.GetProperty("guards").EnumerateArray(), guard => guard.GetProperty("id").GetString() == "network_port_link_exists");
        Assert.False(again.GetProperty("success").GetBoolean());
        Assert.Equal("engineering_rejected", Item(again).GetProperty("failure").GetProperty("category").GetString());

        var disconnect = new { operationId = "d", operation = "disconnect_ports", target = PortItem(1, "Port_2", 32770), partner = PortItem(0, "Port_1", 32769) };
        var removed = await CallAsync(harness, "network_write", new object[] { disconnect });
        Assert.True(removed.GetProperty("success").GetBoolean(), removed.GetRawText());
        Assert.False(Item(removed).GetProperty("result").GetProperty("connectedAfter").GetBoolean());

        var notPort = await CallAsync(harness, "network_write", new object[] { new { operationId = "q", operation = "connect_ports", target = Dq, partner = PortItem(0, "Port_1", 32769) } });
        Assert.Equal("error", notPort.GetProperty("phase").GetString());
        Assert.Equal("unsupported_capability", notPort.GetProperty("error").GetProperty("category").GetString());
    }
}
