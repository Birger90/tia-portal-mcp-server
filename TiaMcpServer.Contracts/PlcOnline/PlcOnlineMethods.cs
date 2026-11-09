namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Worker method and public operation names of the online/offline compare, online state, station
/// upload, update-program and watch/force table operations. Each <c>plc_read</c>/<c>plc_write</c>
/// operation uses its public name as its worker method (like <c>list_tag_tables</c>); the two
/// confirming tools have their own worker methods. They feed <c>OperationPolicyCatalog</c>,
/// <c>ProjectCapabilityCatalog</c> and the worker dispatch switch.
/// </summary>
public static class PlcOnlineMethods
{
    // plc_read: offline observation (Observe).
    public const string CompareSoftware = "compare_software";
    public const string GetOnlineState = "get_online_state";
    public const string ListStationUploadInterfaces = "list_station_upload_interfaces";
    public const string ListWatchAndForceTables = "list_watch_and_force_tables";
    public const string ReadWatchTable = "read_watch_table";
    public const string ReadForceTable = "read_force_table";

    /// <summary>
    /// Offline hardware compare (GitHub issue #7; Siemens.Engineering.Base.xml, V21): <c>HardwareObject.CompareTo(IHardwareCompareTarget)</c>
    /// of the PLC's station or CPU against another PLC's or a master copy, when that object implements the interface at runtime.
    /// </summary>
    public const string CompareHardware = "compare_hardware";

    // plc_read: SimaticML export through a temporary file (TemporaryExport).
    public const string GetWatchTableContent = "get_watch_table_content";
    public const string GetForceTableContent = "get_force_table_content";

    // plc_read: connects to the target but changes nothing (OnlineObserve).
    public const string CompareToOnline = "compare_to_online";

    /// <summary>
    /// Online hardware compare (issue #7): <c>HardwareObject.CompareTo(IHardwareCompareTarget)</c> with the CPU's <c>OnlineProvider</c>
    /// as the target, only when the provider implements <c>IHardwareCompareTarget</c> at runtime (the XML names no implementer).
    /// </summary>
    public const string CompareHardwareToOnline = "compare_hardware_to_online";

    /// <summary>
    /// Audit N3 (not live-tested): <c>FingerprintDataProvider.GetFingerprintData(ConfigurationAddress, OnlineConfigurationDelegate)</c>
    /// reads the fingerprint data of the station at a node address (quick station compare); see <see cref="PlcFingerprintData"/>.
    /// </summary>
    public const string GetStationFingerprintData = "get_station_fingerprint_data";

    // plc_write: offline project mutations (ProjectMutation).
    public const string CreateWatchTable = "create_watch_table";
    public const string DeleteWatchTable = "delete_watch_table";
    public const string CreateWatchTableGroup = "create_watch_table_group";
    public const string DeleteWatchTableGroup = "delete_watch_table_group";
    public const string UpdateWatchTableContent = "update_watch_table_content";
    public const string UpdateForceTableContent = "update_force_table_content";
    public const string UpdateProgram = "update_program";

    /// <summary>The documented V21 <c>PlcSoftware</c> member update_program invokes by exact runtime lookup (its return type is undocumented).</summary>
    public const string UpdateProgramMemberName = "UpdateProgram";

    // plc_tia_online: going online/offline changes TIA Portal's connection to the target (OnlineControl).
    public const string OnlineTool = "plc_tia_online";
    public const string GoOnline = "plc_go_online";
    public const string GoOffline = "plc_go_offline";

    // plc_station_upload: reads a station from a device and adds it to the offline project (ProjectMutation).
    public const string StationUpload = "plc_station_upload";

    public static IReadOnlyList<string> ObserveMethods { get; } = new[]
    {
        CompareSoftware, GetOnlineState, ListStationUploadInterfaces, ListWatchAndForceTables, ReadWatchTable, ReadForceTable,
        CompareHardware,
    };

    public static IReadOnlyList<string> TemporaryExportMethods { get; } = new[] { GetWatchTableContent, GetForceTableContent };

    public static IReadOnlyList<string> OnlineObserveMethods { get; } = new[] { CompareToOnline, CompareHardwareToOnline, GetStationFingerprintData };

    public static IReadOnlyList<string> ProjectMutationMethods { get; } = new[]
    {
        CreateWatchTable, DeleteWatchTable, CreateWatchTableGroup, DeleteWatchTableGroup,
        UpdateWatchTableContent, UpdateForceTableContent, UpdateProgram, StationUpload,
    };

    public static IReadOnlyList<string> OnlineControlMethods { get; } = new[] { GoOnline, GoOffline };

    /// <summary>Watch/force table and update-program methods: project content, delivered for every open container.</summary>
    public static IReadOnlyList<string> ContentReadMethods { get; } = new[]
    {
        ListWatchAndForceTables, ReadWatchTable, ReadForceTable, GetWatchTableContent, GetForceTableContent,
    };

    public static IReadOnlyList<string> ContentWriteMethods { get; } = new[]
    {
        CreateWatchTable, DeleteWatchTable, CreateWatchTableGroup, DeleteWatchTableGroup,
        UpdateWatchTableContent, UpdateForceTableContent, UpdateProgram,
    };

    /// <summary>Compare, online and station-upload methods and tools: standalone projects only, like plc_download.</summary>
    public static IReadOnlyList<string> StandaloneOnlyOperations { get; } = new[]
    {
        CompareToOnline, CompareSoftware, GetOnlineState, ListStationUploadInterfaces, OnlineTool, StationUpload,
        CompareHardware, CompareHardwareToOnline, GetStationFingerprintData,
    };

    /// <summary>The four compare operations (one result type, <c>PlcCompareResultInfo</c>).</summary>
    public static bool IsCompare(string? method) => method is CompareToOnline or CompareSoftware or CompareHardware or CompareHardwareToOnline;

    /// <summary>The two compares that reach the device (online legitimation evidence, Offline restore).</summary>
    public static bool IsOnlineCompare(string? method) => method is CompareToOnline or CompareHardwareToOnline;

    /// <summary>The two hardware compares.</summary>
    public static bool IsHardwareCompare(string? method) => method is CompareHardware or CompareHardwareToOnline;

    /// <summary>The worker methods behind the plc_tia_online tool, gated by its catalog operation.</summary>
    public static bool IsOnlineToolMethod(string? method) => method is GoOnline or GoOffline;
}
