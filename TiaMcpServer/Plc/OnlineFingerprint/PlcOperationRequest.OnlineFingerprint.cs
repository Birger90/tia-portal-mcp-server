using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>Audit N3: the node address of get_station_fingerprint_data (not live-tested).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("get_station_fingerprint_data (required): the node address of the station whose fingerprint data is read (for example its IP address), created with ConfigurationPcInterface.Addresses.Create on the FingerprintDataProvider's own connection configuration; nonblank, without surrounding spaces or control characters.")]
    public string? TargetAddress { get; set; }
}
