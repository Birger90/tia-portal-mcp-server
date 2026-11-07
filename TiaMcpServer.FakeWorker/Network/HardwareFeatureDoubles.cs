// Package G stand-ins with the member names of Siemens.Engineering.Base.xml (V21): Node (Name, ConnectedSubnet, CreateAndConnectToSubnet,
// DisconnectFromSubnet), NetworkInterface (Nodes, IoConnectors), IoConnector.GetIoController, IoController.IoSystem, ImConnection
// (GetPartnerPort, Connect, Disconnect), PcInterfaceAssignment (AssignInterface overloads, GetAvailableIPCExpansions, properties),
// ModuleDescriptionUpdater (CanUpdate, UpdateModuleDescription), TelecontrolManagement (ExportDataPoints, ImportDataPoints,
// TelecontrolDataPoints), CommunicationManagement.Connections with ConnectionComposition.Create<T>(Node, DeviceItem, Node) and
// Connection (Name, ConnectionType, IsValid, LocalSubnetName, PartnerSubnetName, LocalTarget, PartnerTarget, Delete). Member shapes and a
// simple state model only; no evidence of Siemens behavior.
namespace Siemens.Engineering.HW
{
    public enum PcInterfaceAssignmentMode
    {
        None,
        PcStation,
        SoftwarePlc,
    }

    public sealed class Subnet(string name)
    {
        public string Name { get; } = name;
    }

    public sealed class Node(string name)
    {
        public string Name { get; } = name;

        public Subnet? ConnectedSubnet { get; private set; }

        public Subnet CreateAndConnectToSubnet(string name) => ConnectedSubnet = new Subnet(name);

        public void DisconnectFromSubnet() => ConnectedSubnet = null;
    }

    public sealed class IoSystemStandIn(string name)
    {
        public string Name { get; } = name;
    }

    public sealed class IoController(object owner)
    {
        public object Parent { get; } = owner;

        public IoSystemStandIn IoSystem { get; } = new("PROFINET IO-System (100)");
    }

    public sealed class IoConnector(IoController controller)
    {
        public IoController GetIoController() => controller;
    }
}

namespace Siemens.Engineering.HW.Features
{
    public sealed class NetworkInterface
    {
        public List<Siemens.Engineering.HW.Node> Nodes { get; } = new() { new("PROFINET interface_1") };

        public List<Siemens.Engineering.HW.IoConnector> IoConnectors { get; } = new();
    }

    public sealed class ImConnection
    {
        public object? Partner { get; private set; }

        public object? GetPartnerPort() => Partner;

        public void Connect(object partnerPort) => Partner = partnerPort;

        public void Disconnect() => Partner = null;
    }

    public sealed class PcInterfaceAssignment
    {
        public Siemens.Engineering.HW.PcInterfaceAssignmentMode PcInterfaceAssignmentMode { get; private set; }

        public string HardwareResource { get; set; } = "Intel(R) I210";

        public string IpcExpansion { get; set; } = string.Empty;

        public object? SoftwarePlc { get; private set; }

        public void AssignInterface(Siemens.Engineering.HW.PcInterfaceAssignmentMode interfaceAssignmentFor) => PcInterfaceAssignmentMode = interfaceAssignmentFor;

        public void AssignInterface(Siemens.Engineering.HW.PcInterfaceAssignmentMode interfaceAssignmentFor, object softwareTarget)
        {
            PcInterfaceAssignmentMode = interfaceAssignmentFor;
            SoftwarePlc = softwareTarget;
        }

        public IEnumerable<string> GetAvailableIPCExpansions() => new[] { "IPC expansion 1" };
    }

    public sealed class ModuleDescriptionUpdater
    {
        public bool CanUpdate { get; private set; } = true;

        public bool UpdateModuleDescription()
        {
            if (!CanUpdate) return false;
            CanUpdate = false;
            return true;
        }
    }

    public sealed class TelecontrolManagement
    {
        public List<object> TelecontrolDataPoints { get; } = new() { new object() };

        public void ExportDataPoints(FileInfo exportFile) => File.WriteAllText(exportFile.FullName, "Name;Tag\nDP_1;Start\n");

        public void ImportDataPoints(FileInfo importFile)
        {
            foreach (var _ in File.ReadAllLines(importFile.FullName).Skip(1)) TelecontrolDataPoints.Add(new object());
        }
    }

    public sealed class CommunicationManagement
    {
        public Siemens.Engineering.HW.CommunicationConnections.ConnectionComposition Connections { get; } = new();
    }
}

namespace Siemens.Engineering.HW.CommunicationConnections
{
    using System.Collections;

    public abstract class Connection
    {
        internal ConnectionComposition? Owner { get; set; }

        public string Name { get; set; } = string.Empty;

        public abstract string ConnectionType { get; }

        public bool IsValid => true;

        public string LocalSubnetName { get; set; } = string.Empty;

        public string PartnerSubnetName { get; set; } = string.Empty;

        public object? LocalTarget { get; set; }

        public object? PartnerTarget { get; set; }

        public void Delete() => Owner?.Items.Remove(this);
    }

    public sealed class S7Connection : Connection
    {
        public override string ConnectionType => "S7Connection";
    }

    public sealed class TcpConnection : Connection
    {
        public override string ConnectionType => "TcpConnection";
    }

    public sealed class ConnectionComposition : IEnumerable<Connection>
    {
        public List<Connection> Items { get; } = new();

        public T Create<T>(Siemens.Engineering.HW.Node localInterface, object partnerTarget, Siemens.Engineering.HW.Node partnerInterface) where T : Connection, new()
        {
            var connection = new T
            {
                Owner = this, Name = $"{typeof(T).Name}_{Items.Count + 1}", PartnerTarget = partnerTarget,
                LocalSubnetName = localInterface.ConnectedSubnet?.Name ?? string.Empty, PartnerSubnetName = partnerInterface.ConnectedSubnet?.Name ?? string.Empty,
            };
            Items.Add(connection);
            return connection;
        }

        public IEnumerator<Connection> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
