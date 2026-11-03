using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.Worker;

/// <summary>
/// Worker calls of the compare, online-state, plc_tia_online, plc_station_upload, watch/force table and
/// update_program operations. Every one runs through the verified binding and capability gates; the
/// three online actions set the worker-internal confirmation fence, like plc_download.
/// </summary>
public partial class OpennessWorkerClient
{
    /// <summary>compare_to_online or compare_software with the caller's caps.</summary>
    public Task<WorkerCallResult> ComparePlcAsync(string method, PlcOnlineRequestInfo compare)
        => SendBoundProjectRequestAsync(method, null, request => request.PlcOnline = compare, "{}");

    /// <summary>Online state and the offline online-path listing of every PLC, or of the PLCs <paramref name="plcName"/> names.</summary>
    public Task<WorkerCallResult> GetOnlineStateAsync(string? plcName)
        => SendBoundProjectRequestAsync(PlcOnlineMethods.GetOnlineState, null, request => request.PlcName = plcName, "{}");

    /// <summary>The project's station-upload modes and PC interfaces, read offline.</summary>
    public Task<WorkerCallResult> ListStationUploadInterfacesAsync()
        => SendBoundProjectRequestAsync(PlcOnlineMethods.ListStationUploadInterfaces, null, _ => { }, "{}");

    /// <summary>plc_go_online or plc_go_offline of exactly the PLC and path the host resolved.</summary>
    public Task<WorkerCallResult> PlcOnlineActionAsync(string method, PlcOnlineRequestInfo action)
        => SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcOnline = action;
        }, "{}");

    /// <summary>plc_station_upload of exactly the mode, PC interface and address the host resolved.</summary>
    public Task<WorkerCallResult> PlcStationUploadAsync(PlcOnlineRequestInfo upload)
        => SendBoundProjectRequestAsync(PlcOnlineMethods.StationUpload, null, request =>
        {
            request.PlcOnline = upload;
        }, "{}");

    public Task<WorkerCallResult> ListWatchAndForceTablesAsync(string? plcName)
        => SendBoundProjectRequestAsync(PlcOnlineMethods.ListWatchAndForceTables, null, request => request.PlcName = plcName, "{}");

    /// <summary>read_watch_table or read_force_table; <paramref name="maxEntries"/> travels in <c>PlcOnline.MaxElements</c>.</summary>
    public Task<WorkerCallResult> ReadWatchOrForceTableAsync(string method, string? plcName, string tableName, string? groupPath, int maxEntries)
        => SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcName = plcName;
            request.TableName = tableName;
            request.FolderPath = groupPath;
            request.PlcOnline = new PlcOnlineRequestInfo { PlcName = plcName, MaxElements = maxEntries };
        }, "{}");

    /// <summary>get_watch_table_content or get_force_table_content: the SimaticML text is the payload.</summary>
    public Task<WorkerCallResult> GetWatchOrForceTableContentAsync(string method, string? plcName, string tableName, string? groupPath)
        => SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcName = plcName;
            request.TableName = tableName;
            request.FolderPath = groupPath;
        }, string.Empty);

    /// <summary>One watch/force table write or update_program.</summary>
    public Task<WorkerCallResult> WatchTableWriteAsync(string method, string? plcName, string? tableName, string? groupPath,
        string? content = null, string? expectedContentHash = null)
        => SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcName = plcName;
            request.TableName = tableName;
            request.FolderPath = groupPath;
            request.Content = content;
            request.ExpectedContentHash = expectedContentHash;
        }, "{}");
}
