// Stand-in of the NetworkPort service of Siemens.Engineering.Base.xml (V21): ConnectedPorts, Interface, ConnectToPort and
// DisconnectFromPort. ConnectToPort throws for an existing link, as the Siemens skill devices-and-hardware reports; the stand-in also
// refuses to disconnect a missing link. Member shapes and a simple link model only; no evidence of Siemens behavior.
namespace Siemens.Engineering.HW.Features
{
    public sealed class NetworkPort
    {
        private readonly List<NetworkPort> _connected = new();

        /// <summary>Scripted: when true, ConnectToPort returns without linking (a silently ignored call).</summary>
        public bool IgnoreConnect { get; set; }

        public IReadOnlyList<NetworkPort> ConnectedPorts => _connected;

        public object? Interface => null;

        public void ConnectToPort(NetworkPort partner)
        {
            if (_connected.Contains(partner)) throw new InvalidOperationException("The port is already connected to the partner port.");
            if (IgnoreConnect) return;
            _connected.Add(partner);
            partner._connected.Add(this);
        }

        public void DisconnectFromPort(NetworkPort partner)
        {
            if (!_connected.Remove(partner)) throw new InvalidOperationException("The port is not connected to the partner port.");
            partner._connected.Remove(this);
        }
    }
}
