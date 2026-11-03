using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Names of the PROFINET port topology members (GitHub issue #10). Evidence: the V21 IntelliSense file Siemens.Engineering.Base.xml,
/// <c>T:Siemens.Engineering.HW.Features.NetworkPort</c> ("Represents a port on a device item"),
/// <c>M:...NetworkPort.ConnectToPort(Siemens.Engineering.HW.Features.NetworkPort)</c>,
/// <c>M:...NetworkPort.DisconnectFromPort(Siemens.Engineering.HW.Features.NetworkPort)</c>,
/// <c>P:...NetworkPort.ConnectedPorts</c> (summary "Internal use only"; the Siemens skill devices-and-hardware reads the current
/// links from it and guards <c>ConnectToPort</c> with it) and <c>P:...NetworkPort.Interface</c> ("The interface supported by this
/// port"). The XML documents no return or property types and no <c>Parent</c> of the port service, so the members are bound at
/// runtime and a partner port is identified by comparing it (<c>object.Equals</c>) with the port service of every device item.
/// </summary>
public static class NetworkPortNames
{
    public const string Service = "Siemens.Engineering.HW.Features.NetworkPort";
    public const string ConnectToPort = "ConnectToPort";
    public const string DisconnectFromPort = "DisconnectFromPort";
    public const string ConnectedPorts = "ConnectedPorts";
    public const string Interface = "Interface";

    /// <summary>States of a <see cref="HardwarePortInfo.ConnectedPortsState"/>.</summary>
    public static class ReadStates
    {
        /// <summary><c>ConnectedPorts</c> was read and enumerated.</summary>
        public const string Read = "read";

        /// <summary><c>ConnectedPorts</c> is missing on this installation or threw a recoverable error; the partners are unknown.</summary>
        public const string Unreadable = "unreadable";

        public static IReadOnlyList<string> All { get; } = new[] { Read, Unreadable };
    }
}

/// <summary>One partner of a port: the device item whose <c>NetworkPort</c> equals the partner, or null when none of the project's items does.</summary>
public sealed class HardwarePortPartnerInfo
{
    /// <summary>The partner port's device item; null when no device item of the project offers that port service.</summary>
    public HardwareObjectIdentityInfo? Port { get; set; }

    /// <summary>The runtime type of the partner object <c>ConnectedPorts</c> returned.</summary>
    public string RuntimeType { get; set; } = string.Empty;
}

/// <summary>One port device item (a device item that lists <c>NetworkPort</c> among its services) with its topology links.</summary>
public sealed class HardwarePortInfo
{
    public HardwareObjectIdentityInfo Port { get; set; } = new();

    /// <summary>The device item owning the port's <c>Interface</c> (<c>NetworkInterface.OwnedBy</c>); null when unreadable.</summary>
    public HardwareObjectIdentityInfo? InterfaceOwner { get; set; }

    /// <summary><see cref="NetworkPortNames.ReadStates"/>.</summary>
    public string ConnectedPortsState { get; set; } = NetworkPortNames.ReadStates.Read;

    /// <summary>The partners <c>ConnectedPorts</c> lists; empty when unreadable.</summary>
    public List<HardwarePortPartnerInfo> ConnectedPorts { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of list_port_connections: every port device item of the target (the device, or the item and its sub-items).</summary>
public sealed class HardwarePortListInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public List<HardwarePortInfo> Ports { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of connect_ports and disconnect_ports: the link before and the read-back after.</summary>
public sealed class HardwarePortConnectionResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public HardwareObjectIdentityInfo Port { get; set; } = new();

    public HardwareObjectIdentityInfo Partner { get; set; } = new();

    /// <summary>Whether <c>ConnectedPorts</c> of the port contained the partner before the call; null when unreadable.</summary>
    public bool? ConnectedBefore { get; set; }

    /// <summary>The read-back after the call; null when <c>ConnectedPorts</c> is unreadable (then the write is trusted, not verified).</summary>
    public bool? ConnectedAfter { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The Siemens-free calls on live <c>NetworkPort</c> services, shared by the worker and the FakeWorker. <c>ConnectToPort</c> and
/// <c>DisconnectFromPort</c> are bound by exact name and an argument assignable from the partner's runtime type
/// (<see cref="RuntimeServiceValues.Invoke"/>; missing is api_member_missing). Nothing is refused that TIA Portal allows: a
/// link that already exists (connect) or does not exist (disconnect) is still passed to TIA Portal, which decides (the Siemens
/// skill reports that ConnectToPort throws for an existing link). The write is read back from <c>ConnectedPorts</c>: anything but
/// the requested state is postcondition_failed; an unreadable <c>ConnectedPorts</c> leaves the result unverified with a message.
/// </summary>
public static class PortTopologyRun
{
    /// <summary>The partners <c>ConnectedPorts</c> returns; (null, reason) when it is missing or unreadable.</summary>
    public static (List<object>? Partners, string? Error) Partners(object port, Func<Exception, bool> recoverable)
    {
        var property = Library.LibraryRuntimeBinding.Property(port.GetType(), NetworkPortNames.ConnectedPorts, out var missing);
        if (property is null) return (null, missing);
        object? value;
        try
        {
            value = property.GetValue(port, null);
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            if (!recoverable(exception.InnerException))
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            return (null, $"NetworkPort.ConnectedPorts could not be read ({exception.InnerException.GetType().Name}: {exception.InnerException.Message}).");
        }

        if (value is null) return (new List<object>(), null);
        try
        {
            return (RuntimeServiceValues.Elements(value, "NetworkPort.ConnectedPorts").Where(element => element is not null).Select(element => element!).ToList(), null);
        }
        catch (RuntimeBindingException exception)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>True when <paramref name="partners"/> lists <paramref name="partner"/> (<c>object.Equals</c>, never reference identity).</summary>
    public static bool Contains(IEnumerable<object> partners, object partner) => partners.Any(candidate => Equals(candidate, partner));

    /// <summary>Connects or disconnects and reads the link back; the result's before/after states and messages are filled in.</summary>
    public static void Run(object port, object partner, bool connect, HardwarePortConnectionResultInfo result, Func<Exception, bool> recoverable)
    {
        if (Equals(port, partner))
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                "The port and the partner are the same port; name the port on the other device. Nothing was changed.");
        var (before, beforeError) = Partners(port, recoverable);
        result.ConnectedBefore = before is null ? null : Contains(before, partner);
        if (beforeError is not null) result.Messages.Add("Before the call: " + beforeError);
        var member = connect ? NetworkPortNames.ConnectToPort : NetworkPortNames.DisconnectFromPort;
        RuntimeServiceValues.Invoke(port, member, new[] { partner.GetType() }, new[] { partner }, $"NetworkPort.{member}(NetworkPort)");
        var (after, afterError) = Partners(port, recoverable);
        if (after is null)
        {
            result.ConnectedAfter = null;
            result.Messages.Add($"NetworkPort.{member} returned, but the link could not be read back ({afterError}); the result is not verified. "
                + "Inspect it with network_read list_port_connections.");
            return;
        }

        result.ConnectedAfter = Contains(after, partner);
        if (result.ConnectedAfter != connect)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"NetworkPort.{member} returned, but ConnectedPorts of the port {(connect ? "does not list" : "still lists")} the partner port. "
                + "Inspect the ports with network_read list_port_connections before retrying.");
    }
}
