using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// network_read list_port_connections and network_write connect_ports / disconnect_ports (GitHub issue #10, not live-tested).
/// The V21 reference (Siemens.Engineering.Base.xml) documents the <c>NetworkPort</c> service with <c>ConnectToPort</c>,
/// <c>DisconnectFromPort</c>, <c>ConnectedPorts</c> ("Internal use only") and <c>Interface</c>, without types and without a
/// <c>Parent</c>, and the Siemens skill retrieves it with <c>deviceItem.GetService&lt;NetworkPort&gt;()</c>. It is not in the
/// compile stubs, so it is taken from the device item's <c>GetServiceInfos()</c> by exact full name and retrieved with
/// <c>IServiceProvider.GetService(Type)</c>; the calls are the Siemens-free <see cref="PortTopologyRun"/>. A partner port is
/// named by the device item whose port service equals it (<c>object.Equals</c>) among every device item of the project.
/// Cable attributes of a port interconnection (the manual's "Attributes of port inter-connection") are attributes of the port
/// device item: inspect_network_object reads them and set_hardware_attributes writes them; no name is assumed here.
/// </summary>
internal static partial class HardwareModuleService
{
    public static HardwarePortListInfo ListPortConnections(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var result = new HardwarePortListInfo { Target = target.Identity };
        var roots = target.Item is null ? ((Device)target.Object).DeviceItems.Cast<DeviceItem>() : new[] { target.Item };
        var ports = new List<(DeviceItem Item, object Port)>();
        foreach (var item in roots.SelectMany(Descendants))
            if (PortOf(item, result.Messages) is { } port) ports.Add((item, port));

        List<(DeviceItem Item, object Port)>? index = null;
        foreach (var (item, port) in ports)
        {
            // K77: a port whose selector cannot be read keeps the target's device name and its own name (no item path, the
            // message says why); the host contract rejects an identity without a device name.
            var info = new HardwarePortInfo { Port = IdentityOf(item, result.Messages) ?? new HardwareObjectIdentityInfo { DeviceName = target.Identity.DeviceName, Name = SafeName(item) } };
            info.InterfaceOwner = InterfaceOwner(port, info.Messages);
            var (partners, error) = PortTopologyRun.Partners(port, Recoverable);
            if (partners is null)
            {
                info.ConnectedPortsState = NetworkPortNames.ReadStates.Unreadable;
                info.Messages.Add(error ?? "NetworkPort.ConnectedPorts could not be read.");
            }
            else
            {
                foreach (var partner in partners)
                {
                    index ??= ProjectPorts(project, result.Messages);
                    var owner = index.FirstOrDefault(entry => Equals(entry.Port, partner));
                    info.ConnectedPorts.Add(new HardwarePortPartnerInfo
                    {
                        Port = owner.Item is null ? null : IdentityOf(owner.Item, info.Messages),
                        RuntimeType = partner.GetType().FullName ?? partner.GetType().Name,
                    });
                    if (owner.Item is null) info.Messages.Add("A partner port is not the NetworkPort of any device item of the project (e.g. a port of another project); it is listed without a selector.");
                }
            }

            result.Ports.Add(info);
        }

        if (ports.Count == 0) result.Messages.Add($"{Describe(target.Identity)} has no device item that lists {NetworkPortNames.Service} among its services.");
        return result;
    }

    public static HardwarePortConnectionResultInfo ConnectPorts(ProjectBase project, HardwareModuleRequestInfo request)
        => ConnectOrDisconnect(project, request, connect: true);

    public static HardwarePortConnectionResultInfo DisconnectPorts(ProjectBase project, HardwareModuleRequestInfo request)
        => ConnectOrDisconnect(project, request, connect: false);

    private static HardwarePortConnectionResultInfo ConnectOrDisconnect(ProjectBase project, HardwareModuleRequestInfo request, bool connect)
    {
        var operation = connect ? HardwareModuleMethods.ConnectPorts : HardwareModuleMethods.DisconnectPorts;
        var source = Resolve(project, request.Target, "target");
        var partner = Resolve(project, request.Partner, "partner");
        var sourceItem = RequireItem(source, operation);
        var partnerItem = RequireItem(partner, operation);
        var messages = new List<string>();
        var port = PortOf(sourceItem, messages) ?? throw NoPort(source.Identity);
        var partnerPort = PortOf(partnerItem, messages) ?? throw NoPort(partner.Identity);
        var result = new HardwarePortConnectionResultInfo { Operation = operation, Port = source.Identity, Partner = partner.Identity, Messages = messages };
        Translate(() =>
        {
            PortTopologyRun.Run(port, partnerPort, connect, result, Recoverable);
            return true;
        });
        return result;
    }

    /// <summary>The port service of the item, or null when it does not list one; a listed port that GetService does not return is a message.</summary>
    private static object? PortOf(DeviceItem item, List<string> messages)
    {
        var offered = TechnologyObjectRuntime.ServiceTypes(item);
        var port = TechnologyObjectRuntime.TryService(item, offered, NetworkPortNames.Service, out var unavailable);
        if (unavailable is not null) messages.Add($"Device item '{SafeName(item)}': {unavailable}");
        return port;
    }

    /// <summary>Every device item of the project with its port service (built once per read, only when a partner must be named).</summary>
    private static List<(DeviceItem Item, object Port)> ProjectPorts(ProjectBase project, List<string> messages)
    {
        var ports = new List<(DeviceItem, object)>();
        foreach (var device in ProjectDeviceEnumerator.Enumerate(project))
            foreach (var item in device.DeviceItems.Cast<DeviceItem>().SelectMany(Descendants))
                if (PortOf(item, messages) is { } port) ports.Add((item, port));
        return ports;
    }

    private static HardwareObjectIdentityInfo? InterfaceOwner(object port, List<string> messages)
    {
        var read = TechnologyObjectRuntime.Read(port, NetworkPortNames.Interface);
        if (!read.Found)
        {
            messages.Add("NetworkPort.Interface could not be read.");
            return null;
        }

        if (read.Value is not NetworkInterface networkInterface)
        {
            if (read.Value is not null) messages.Add($"NetworkPort.Interface returned a '{read.Value.GetType().FullName}', not a NetworkInterface.");
            return null;
        }

        try
        {
            return networkInterface.OwnedBy is { } owner ? IdentityOf(owner, messages) : null;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception) && exception is not EngineeringObjectDisposedException)
        {
            messages.Add($"NetworkInterface.OwnedBy could not be read ({exception.Message}).");
            return null;
        }
    }

    private static IEnumerable<DeviceItem> Descendants(DeviceItem item)
    {
        yield return item;
        foreach (DeviceItem child in item.DeviceItems)
            foreach (var nested in Descendants(child))
                yield return nested;
    }

    private static string SafeName(DeviceItem item)
    {
        try
        {
            return item.Name;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return "(unreadable name)";
        }
    }

    private static WorkerOperationException NoPort(HardwareObjectIdentityInfo identity)
        => new(WorkerFailureCategories.UnsupportedCapability,
            RuntimeServiceValues.NotListed(Describe(identity), NetworkPortNames.Service) + " Select a port device item (read_hardware_config, list_port_connections). Nothing was changed.");

    /// <summary>Recoverable Openness errors make a read "unreadable"; Portal loss and disposal propagate.</summary>
    private static bool Recoverable(Exception exception)
        => exception is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering) && engineering is not EngineeringObjectDisposedException;
}
