using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The issue #16 guards: download_to_folder is informative; the online master secret is an acknowledge guard (the operation changes
/// the online PLC), so read-write confirms every call with such an item and full runs it under policy.
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string DownloadToFolderWritesFiles = "plc_download_to_folder_writes_files";
    public const string MasterSecretOnline = "plc_master_secret_online";

    private static IEnumerable<WriteGuardDefinition> DeviceDataDefinitions() => new[]
    {
        new WriteGuardDefinition(DownloadToFolderWritesFiles, WriteGuardSeverities.Info,
            "download_to_folder writes the PLC's download data (hardware and software) into a caller directory; TIA Portal may compile first. A directory that is not empty is refused unless overwrite:true (then existing files there may be replaced). TIA Portal dialogs are answered by portalConfirmations (decline by default). No device is touched."),
    };

    private static IEnumerable<WriteGuardDefinition> DeviceDataAcknowledgeDefinitions() => new[]
    {
        new WriteGuardDefinition(MasterSecretOnline, WriteGuardSeverities.Acknowledge,
            "The item sets or deletes the master secret of the ONLINE PLC (OnlineProvider.SetPlcMasterSecret / ResetPlcMasterSecret). It acts on the connected controller, not on the offline project, and cannot be read back. The secret comes only from a password source."),
    };
}
