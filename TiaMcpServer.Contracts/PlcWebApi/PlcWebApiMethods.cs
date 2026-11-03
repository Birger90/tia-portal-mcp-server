namespace TiaMcpServer.Contracts.PlcWebApi;

/// <summary>
/// Operation names of the in-host SIMATIC S7-1500 Web API client (<c>plc_online_read</c> and
/// <c>plc_online_write</c>). <c>OperationPolicyCatalog</c> is one flat namespace shared with the
/// workers, so every method carries the <c>webapi_</c> prefix. Reads are <c>Observe</c>; every
/// method that changes a real controller is <c>OnlineControl</c>. These methods never reach the
/// Openness or PLCSIM worker and carry no project identity.
/// </summary>
public static class PlcWebApiMethods
{
    public const string Prefix = "webapi_";

    public const string Ping = "webapi_ping";
    public const string Version = "webapi_version";
    public const string Permissions = "webapi_permissions";
    public const string ReadOperatingMode = "webapi_read_operating_mode";
    public const string ReadModeSelector = "webapi_read_mode_selector";
    public const string ReadCpuType = "webapi_read_cpu_type";
    public const string ReadRuntimeInformation = "webapi_read_runtime_information";
    public const string ReadSystemTime = "webapi_read_system_time";
    public const string ReadProjectInformation = "webapi_read_project_information";
    public const string BrowseTags = "webapi_browse_tags";
    public const string ReadTags = "webapi_read_tags";
    public const string ReadDiagnosticBuffer = "webapi_read_diagnostic_buffer";
    public const string BrowseAlarms = "webapi_browse_alarms";
    public const string BrowseModules = "webapi_browse_modules";
    public const string ReadModuleStatus = "webapi_read_module_status";
    public const string ReadModuleLeds = "webapi_read_module_leds";
    public const string BrowseSyslog = "webapi_browse_syslog";

    /// <summary>
    /// Reads an offline watch or tag table through Openness (bound project) and its symbols through
    /// <c>PlcProgram.Read</c>; the Openness reads it makes are their own <c>Observe</c> worker methods.
    /// </summary>
    public const string ReadWatchTableLive = "webapi_read_watch_table_live";

    public const string ChangeOperatingMode = "webapi_change_operating_mode";
    public const string WriteTags = "webapi_write_tags";
    public const string AcknowledgeAlarm = "webapi_acknowledge_alarm";
    public const string FlashLeds = "webapi_flash_leds";
    public const string SetSystemTime = "webapi_set_system_time";
    public const string CreateBackup = "webapi_create_backup";
    public const string RestoreBackup = "webapi_restore_backup";

    /// <summary>Read methods, classified <c>Observe</c>.</summary>
    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[]
    {
        Ping, Version, Permissions, ReadOperatingMode, ReadModeSelector, ReadCpuType, ReadRuntimeInformation,
        ReadSystemTime, ReadProjectInformation, BrowseTags, ReadTags, ReadDiagnosticBuffer, BrowseAlarms,
        BrowseModules, ReadModuleStatus, ReadModuleLeds, BrowseSyslog, ReadWatchTableLive,
    });

    /// <summary>Methods that change a real controller online, classified <c>OnlineControl</c>.</summary>
    public static IReadOnlyList<string> Control { get; } = Array.AsReadOnly(new[]
    {
        ChangeOperatingMode, WriteTags, AcknowledgeAlarm, FlashLeds, SetSystemTime, CreateBackup, RestoreBackup,
    });

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Read.Concat(Control).ToArray());

    /// <summary>Maps a public plc_online_read/plc_online_write operation name to its method.</summary>
    public static string ForOperation(string operation) => Prefix + operation;

    public static bool IsWebApiMethod(string? method)
        => method is not null && All.Contains(method, StringComparer.Ordinal);
}
