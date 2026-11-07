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
    public const string LoadsActualValuesOnline = "plc_loads_actual_values_online";

    private static IEnumerable<WriteGuardDefinition> DeviceDataDefinitions() => new[]
    {
        new WriteGuardDefinition(DownloadToFolderWritesFiles, WriteGuardSeverities.Info,
            "download_to_folder writes the PLC's download data (hardware and software) into a caller directory; TIA Portal may compile first. A directory that is not empty is refused unless overwrite:true (then existing files there may be replaced). TIA Portal dialogs are answered by portalConfirmations (decline by default). No device is touched."),
    };

    private static IEnumerable<WriteGuardDefinition> DeviceDataAcknowledgeDefinitions() => new[]
    {
        new WriteGuardDefinition(MasterSecretOnline, WriteGuardSeverities.Acknowledge,
            "The item sets or deletes the master secret of the ONLINE PLC (OnlineProvider.SetPlcMasterSecret / ResetPlcMasterSecret). It acts on the connected controller, not on the offline project, and cannot be read back. The secret comes only from a password source."),
        new WriteGuardDefinition(LoadsActualValuesOnline, WriteGuardSeverities.Acknowledge,
            "The item overwrites the ACTUAL values of a data block in the RUNNING, ONLINE PLC (ValueService.LoadSnapshotAsActualValues / LoadStartValuesAsActualValues) with the DB's stored snapshot or its start values. The process may react at once (setpoints, recipes, counters, interlocks); a real machine can move. It acts on the connected controller (a real CPU or PLCSIM), cannot be undone by the server and is not read back. Check that the plant is in a safe state."),
    };
}
