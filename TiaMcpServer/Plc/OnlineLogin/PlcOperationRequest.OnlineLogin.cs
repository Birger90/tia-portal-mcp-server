using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>Audit N2: the user of a user-based PLC login for the plc_read online compares (not live-tested).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("compare_to_online, compare_hardware_to_online and get_station_fingerprint_data only: " + PlcDownload.PlcOnlineUserDescription.Text)]
    public PlcDownload.PlcOnlineUserArgument? OnlineUser { get; set; }
}
