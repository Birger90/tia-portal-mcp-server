using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Network;

/// <summary>
/// Stateful <c>network-io-system</c> fixture for the PROFINET IO-system chain (K28): an S7-1500 station <c>PLC_1</c> whose
/// CPU item owns the PROFINET node <c>node-plc</c> and an IO controller, and an ET 200SP station <c>ET200SP_1</c> whose
/// interface module owns the node <c>node-et</c> and only an IO connector. No subnet exists at the start. Handles
/// <c>read_hardware_config</c>, <c>create_subnet</c>, <c>configure_network_device</c> (subnet and IO-system changes) and
/// <c>create_io_system</c>; reads observe earlier writes in the same process. Scripted refusals: <c>create_io_system</c> on a
/// node without IO controller is <c>target_kind_unsupported</c>, on a node without subnet <c>engineering_rejected</c>.
/// This models object access only; it is no evidence of Siemens behavior (create_io_system is not live-tested).
/// </summary>
sealed class IoSystemScenario
{
    private sealed class Station(string device, string deviceType, string rackName, string rackType, string ownerName, string ownerType,
        int ownerPosition, string nodeId, bool controller)
    {
        public string Device { get; } = device;
        public string DeviceType { get; } = deviceType;
        public string RackName { get; } = rackName;
        public string RackType { get; } = rackType;
        public string OwnerName { get; } = ownerName;
        public string OwnerType { get; } = ownerType;
        public int OwnerPosition { get; } = ownerPosition;
        public string NodeId { get; } = nodeId;
        public bool Controller { get; } = controller;
        public string? SubnetId { get; set; }
        public (string SubnetId, int Number)? IoSystem { get; set; }

        public List<NetworkInterfacePathSegmentInfo> InterfacePath() => new()
        {
            new() { Name = RackName, PositionNumber = 0, TypeIdentifier = RackType },
            new() { Name = OwnerName, PositionNumber = OwnerPosition, TypeIdentifier = OwnerType },
        };
    }

    private sealed class SubnetState(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public List<IoSystemInfo> IoSystems { get; } = new();
    }

    private readonly List<Station> _stations = new()
    {
        new("PLC_1", "System:Device.S71500", "Rack_0", "System:Rack.S71500", "PLC_1", "OrderNumber:6ES7 516-3AN01-0AB0/V2.8", 1, "node-plc", controller: true),
        new("ET200SP_1", "System:Device.ET200SP", "Rack_0", "System:Rack.ET200SP", "IM 155-6 PN ST", "OrderNumber:6ES7 155-6AU01-0BN0/V4.1", 0, "node-et", controller: false),
    };

    private readonly List<SubnetState> _subnets = new();
    private int _nextSubnet = 1;

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            return request.Method switch
            {
                "read_hardware_config" => Ok(Hardware()),
                "create_subnet" => Ok(CreateSubnet(request)),
                "configure_network_device" => Ok(Configure(request)),
                NetworkIoSystemMethods.CreateIoSystem => Ok(CreateIoSystem(request)),
                _ => Fail(WorkerFailureCategories.ValidationError, $"unexpected network-io-system method '{request.Method}'"),
            };
        }
        catch (Refusal refusal)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = refusal.Category, Error = refusal.Message,
                EngineeringError = refusal.Category == WorkerFailureCategories.EngineeringRejected
                    ? new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = refusal.Message }
                    : null,
            }, WorkerJson.Envelope);
        }
    }

    private sealed class Refusal(string category, string message) : Exception(message)
    {
        public string Category { get; } = category;
    }

    private SubnetLifecycleResultInfo CreateSubnet(WorkerRequest request)
    {
        var subnet = new SubnetState($"subnet-{_nextSubnet++}", request.SubnetName!);
        _subnets.Add(subnet);
        return new SubnetLifecycleResultInfo { SubnetId = subnet.Id, Name = subnet.Name };
    }

    private ConfigureNetworkDeviceResultInfo Configure(WorkerRequest request)
    {
        var station = FindStation(request.NetworkObjectTarget);
        var result = new ConfigureNetworkDeviceResultInfo { DeviceName = request.DeviceName! };
        if (request.SubnetId is { } subnetId)
        {
            _ = FindSubnet(subnetId);
            station.SubnetId = subnetId;
            result.AppliedSettings[NetworkSettingKeys.Subnet] = subnetId;
        }

        if (request.IoSystemNumber is { } number)
        {
            var subnet = FindSubnet(request.IoSystemSubnetId ?? station.SubnetId!);
            var ioSystem = subnet.IoSystems.SingleOrDefault(candidate => candidate.Number == number)
                ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No IO system with number {number} on subnet '{subnet.Id}'.");
            if (station.SubnetId != subnet.Id)
                throw new Refusal(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the operation: the node is not connected to the IO system's subnet.");
            station.IoSystem = (subnet.Id, number);
            if (!ioSystem.ConnectedDeviceNames.Contains(station.Device)) ioSystem.ConnectedDeviceNames.Add(station.Device);
            result.AppliedSettings[NetworkSettingKeys.IoSystem] = number.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    private CreateIoSystemResultInfo CreateIoSystem(WorkerRequest request)
    {
        var station = FindStation(request.NetworkObjectTarget);
        if (!station.Controller)
            throw new Refusal(WorkerFailureCategories.TargetKindUnsupported,
                $"create_io_system: the interface of node '{station.NodeId}' of device '{station.Device}' has no IO controller (IoControllers is empty). Nothing was changed.");
        if (station.SubnetId is null)
            throw new Refusal(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the operation: the interface is not connected to a subnet.");
        var subnet = FindSubnet(station.SubnetId);
        var number = 100 + subnet.IoSystems.Count;
        var name = string.IsNullOrEmpty(request.NewIoSystemName) ? $"PROFINET IO-System ({number})" : request.NewIoSystemName!;
        subnet.IoSystems.Add(new IoSystemInfo
        {
            Name = name, Number = number, IoControllerName = station.Device, Selectable = true,
            Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.IoSystem, SubnetId = subnet.Id, Number = number },
        });
        station.IoSystem = (subnet.Id, number);
        return new CreateIoSystemResultInfo
        {
            DeviceName = station.Device, NodeId = station.NodeId, RequestedName = request.NewIoSystemName, Name = name, Number = number, SubnetId = subnet.Id,
        };
    }

    private Station FindStation(NetworkObjectSelectorInfo? target)
    {
        var station = _stations.SingleOrDefault(candidate => string.Equals(candidate.Device, target?.DeviceName, StringComparison.OrdinalIgnoreCase)
            && candidate.NodeId == target?.NodeId)
            ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No node '{target?.NodeId}' on device '{target?.DeviceName}'.");
        if (target!.InterfacePath is { } path && !path.Select(segment => (segment.Name, segment.PositionNumber))
            .SequenceEqual(station.InterfacePath().Select(segment => (segment.Name, segment.PositionNumber))))
            throw new Refusal(WorkerFailureCategories.TargetEvidenceMismatch, "The interface path does not match the node's owner.");
        return station;
    }

    private SubnetState FindSubnet(string id)
        => _subnets.SingleOrDefault(subnet => subnet.Id == id) ?? throw new Refusal(WorkerFailureCategories.TargetNotFound, $"No subnet with subnetId '{id}'.");

    private HardwareConfigInfo Hardware() => new()
    {
        DiscoveryEvidence = new HardwareDiscoveryEvidenceInfo { Scope = "project", Complete = true },
        RootDeviceCount = _stations.Count,
        Devices = _stations.Select(Device).ToList(),
        Subnets = _subnets.Select(Project).ToList(),
    };

    private static DeviceInfo Device(Station station)
    {
        DeviceItemPathSegmentInfo Segment(int index, string name, int position, string type) => new() { Index = index, Name = name, PositionNumber = position, TypeIdentifier = type };
        var rackPath = new List<DeviceItemPathSegmentInfo> { Segment(0, station.RackName, 0, station.RackType) };
        var ownerPath = rackPath.Append(Segment(0, station.OwnerName, station.OwnerPosition, station.OwnerType)).ToList();
        var owner = new DeviceItemInfo
        {
            Name = station.OwnerName, TypeIdentifier = station.OwnerType, PositionNumber = station.OwnerPosition, Selectable = true,
            Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.DeviceItem, DeviceName = station.Device, ItemPath = ownerPath },
        };
        owner.NetworkInterfaces.Add(new NetworkInterfaceInfo
        {
            Name = "PROFINET interface_1", Selectable = true,
            Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.NetworkInterface, DeviceName = station.Device, ItemPath = ownerPath },
            Nodes =
            {
                new NodeInfo
                {
                    Name = "PROFINET interface_1", NodeId = station.NodeId, NodeType = "Ethernet", Selectable = true,
                    Selector = new NetworkObjectSelectorInfo
                    {
                        Kind = NetworkObjectKinds.Node, DeviceName = station.Device, NodeId = station.NodeId, InterfacePath = station.InterfacePath(),
                    },
                    ConnectionEvidence = new NetworkNodeConnectionInfo
                    {
                        Complete = true, SubnetId = station.SubnetId, IoSystemSubnetId = station.IoSystem?.SubnetId, IoSystemNumber = station.IoSystem?.Number,
                    },
                },
            },
        });
        var rack = new DeviceItemInfo
        {
            Name = station.RackName, TypeIdentifier = station.RackType, PositionNumber = 0, Selectable = true,
            Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.DeviceItem, DeviceName = station.Device, ItemPath = rackPath },
            Items = { owner },
        };
        return new DeviceInfo { Name = station.Device, TypeIdentifier = station.DeviceType, Items = { rack } };
    }

    private SubnetInfo Project(SubnetState subnet) => new()
    {
        Name = subnet.Name, SubnetId = subnet.Id, NetworkType = SubnetLifecycleContract.Ethernet, TypeIdentifier = "System:Subnet.Ethernet",
        Selectable = true, Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Subnet, SubnetId = subnet.Id },
        ConnectionEvidence = new NetworkSubnetConnectionsInfo
        {
            Complete = true,
            Nodes = _stations.Where(station => station.SubnetId == subnet.Id).Select(station => new NetworkNodeIdentityInfo
            {
                DeviceName = station.Device, NodeId = station.NodeId, InterfacePath = station.InterfacePath(),
            }).ToList(),
        },
        ConnectedNodeNames = _stations.Where(station => station.SubnetId == subnet.Id).Select(_ => "PROFINET interface_1").ToList(),
        IoSystems = subnet.IoSystems.ToList(),
    };

    private static string Ok<T>(T payload) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(payload) }, WorkerJson.Envelope);

    private static string Fail(string category, string message)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message }, WorkerJson.Envelope);
}
