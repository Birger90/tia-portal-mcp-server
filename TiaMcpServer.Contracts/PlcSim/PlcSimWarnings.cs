namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>Fixed warning texts shared by host and worker so they can be deduplicated exactly.</summary>
public static class PlcSimWarnings
{
    public const string UpdateTagListColdRestart =
        "update_tag_list calls IInstance.UpdateTagList(). An unverified secondary source reports that this can cold-restart the virtual CPU; check operatingState and retentive data afterwards.";
}
