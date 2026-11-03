using System.Text.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// K77 (live 2026-11-03): list_port_connections on the CPU device item failed protocol_error. The real worker built a port's
/// identity by throwing on the null TypeIdentifier of integrated interfaces/ports and fell back to an identity without a device
/// name, which the host contract rejects for the whole payload. The FakeWorker always had type identifiers, so it never showed.
/// The worker now writes "" for a null TypeIdentifier (like read_hardware_config's path segments) and never an identity without
/// its device name; these tests pin the host side of both shapes.
/// </summary>
public sealed class PortListPayloadContractTests
{
    private static DeviceItemPathSegmentInfo Segment(int index, string name, int position, string type)
        => new() { Index = index, Name = name, PositionNumber = position, TypeIdentifier = type };

    private static HardwareObjectIdentityInfo Identity(params DeviceItemPathSegmentInfo[] path)
    {
        var leaf = path[^1];
        return new() { DeviceName = "PLC_1", ItemPath = path.ToList(), Name = leaf.Name, TypeIdentifier = leaf.TypeIdentifier, PositionNumber = leaf.PositionNumber };
    }

    private static readonly DeviceItemPathSegmentInfo Rack = Segment(0, "Rack_0", 0, "System:Rack.S71500");
    private static readonly DeviceItemPathSegmentInfo Cpu = Segment(1, "PLC_1", 1, "OrderNumber:6ES7 516-3AN02-0AB0/V2.9");
    private static readonly DeviceItemPathSegmentInfo X1 = Segment(0, "PROFINET-Schnittstelle_1", 32768, string.Empty);

    private static NetworkOperationRequest Request() => new()
    {
        OperationId = "l",
        Operation = HardwareModuleMethods.ListPortConnections,
        Target = new NetworkObjectTarget
        {
            Kind = "deviceItem", DeviceName = "PLC_1",
            ItemPath = new[] { Rack, Cpu }.Select(s => new NetworkDeviceItemPathSegment { Index = s.Index, Name = s.Name, PositionNumber = s.PositionNumber, TypeIdentifier = s.TypeIdentifier }).ToList(),
        },
    };

    private static HardwarePortListInfo RealShape() => new()
    {
        Target = Identity(Rack, Cpu),
        Ports =
        {
            new HardwarePortInfo
            {
                Port = Identity(Rack, Cpu, X1, Segment(0, "Port_1", 32769, string.Empty)),
                InterfaceOwner = Identity(Rack, Cpu, X1),
                ConnectedPorts = { new HardwarePortPartnerInfo { Port = null, RuntimeType = "Siemens.Engineering.HW.Features.NetworkPort" } },
                Messages = { "A partner port is not the NetworkPort of any device item of the project (e.g. a port of another project); it is listed without a selector." },
            },
            new HardwarePortInfo
            {
                Port = Identity(Rack, Cpu, X1, Segment(1, "Port_2", 32770, string.Empty)),
                InterfaceOwner = null,
                Messages = { "A device item's selector could not be read (x); inspect it with network_read read_hardware_config.", "NetworkInterface.OwnedBy could not be read (y)." },
            },
        },
    };

    [Fact]
    public void RealWorkerShape_IntegratedPortsWithoutTypeIdentifier_IsAccepted()
    {
        var element = HardwareModulePayloadContract.Decode(Request(), WorkerJson.SerializePayload(RealShape()));
        var ports = element.GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
        Assert.Equal(string.Empty, ports[0].GetProperty("port").GetProperty("itemPath")[3].GetProperty("typeIdentifier").GetString());
        Assert.Equal(JsonValueKind.Null, ports[1].GetProperty("interfaceOwner").ValueKind);
    }

    [Fact]
    public void ThePreviousFallbackIdentity_WithoutADeviceName_IsWhatTheContractRejected()
    {
        var shape = RealShape();
        shape.Ports[1].InterfaceOwner = new HardwareObjectIdentityInfo { Name = "PROFINET-Schnittstelle_1", PositionNumber = 32768 };
        Assert.ThrowsAny<JsonException>(() => HardwareModulePayloadContract.Decode(Request(), WorkerJson.SerializePayload(shape)));
    }
}
