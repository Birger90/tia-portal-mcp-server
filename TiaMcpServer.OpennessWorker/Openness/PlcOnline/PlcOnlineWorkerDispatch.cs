using Siemens.Engineering;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// Worker dispatch of the compare, online, station-upload, watch/force table and update-program
/// methods, behind one arm of <c>Program</c>'s switch (after authorization, inside
/// <c>WithEngineeringRoot</c>). Returns the payload text: declared JSON for every method except the two
/// SimaticML exports, whose payload is the document itself (like get_block_content).
/// </summary>
public static class PlcOnlineWorkerDispatch
{
    private static readonly HashSet<string> Methods = new(
        PlcOnlineMethods.ObserveMethods.Concat(PlcOnlineMethods.TemporaryExportMethods).Concat(PlcOnlineMethods.OnlineObserveMethods)
            .Concat(PlcOnlineMethods.ProjectMutationMethods).Concat(PlcOnlineMethods.OnlineControlMethods),
        StringComparer.Ordinal);

    public static bool Handles(string? method) => method is not null && Methods.Contains(method);

    /// <summary>Every mutation and online action requires the worker-internal confirmation fence, like the other writes and plc_download.</summary>
    public static bool RequiresConfirm(string method)
        => PlcOnlineMethods.ProjectMutationMethods.Contains(method) || PlcOnlineMethods.OnlineControlMethods.Contains(method);

    /// <param name="confirmations">The call's confirmation log (see <see cref="PlcOnlineConfirmationScope"/>), for the methods that answer in a scope.</param>
    /// <param name="deferred">Work go_online leaves active until the next request starts (its fail-closed legitimation handler).</param>
    public static string Invoke(ProjectBase project, WorkerRequest request, PlcDownloadPortalConfirmationLog? confirmations = null,
        TiaPortal? portal = null, DeferredUntilNextRequest? deferred = null)
    {
        Action<int> sleep = System.Threading.Thread.Sleep;
        var online = request.PlcOnline;
        // read_watch_table/read_force_table carry their entry cap in PlcOnline.MaxElements; the host always sets it.
        var maxEntries = online?.MaxElements ?? PlcWatchTablePolicy.DefaultMaxEntries;
        return request.Method switch
        {
            PlcOnlineMethods.CompareToOnline => Json(NothingDeclined(PlcCompareService.CompareToOnline(project, Require(online), sleep), confirmations)),
            PlcOnlineMethods.CompareSoftware => Json(PlcCompareService.CompareSoftware(project, Require(online))),
            PlcOnlineMethods.CompareHardware => Json(PlcHardwareCompareService.CompareHardware(project, Require(online))),
            PlcOnlineMethods.CompareHardwareToOnline => Json(NothingDeclined(PlcHardwareCompareService.CompareHardwareToOnline(project, Require(online), sleep), confirmations)),
            PlcOnlineMethods.GetStationFingerprintData => Json(PlcFingerprintDataService.Read(project, portal, Require(online), confirmations)),
            PlcOnlineMethods.GetOnlineState => Json(PlcOnlineService.ListOnlineState(project, request.PlcName)),
            PlcOnlineMethods.ListStationUploadInterfaces => Json(PlcOnlineService.ListStationUploadInterfaces(project)),
            PlcOnlineMethods.GoOnline => Json(PlcOnlineService.GoOnline(project, Require(online), sleep, confirmations, deferred)),
            PlcOnlineMethods.GoOffline => Json(PlcOnlineService.GoOffline(project, Require(online), sleep, confirmations)),
            PlcOnlineMethods.StationUpload => Json(PlcOnlineService.StationUpload(project, Require(online), confirmations)),
            PlcOnlineMethods.ListWatchAndForceTables => Json(PlcWatchTableService.List(project, request.PlcName)),
            PlcOnlineMethods.ReadWatchTable => Json(PlcWatchTableService.ReadWatchTable(project, request.PlcName, request.TableName, request.FolderPath, maxEntries)),
            PlcOnlineMethods.ReadForceTable => Json(PlcWatchTableService.ReadForceTable(project, request.PlcName, request.TableName, request.FolderPath, maxEntries)),
            PlcOnlineMethods.GetWatchTableContent => PlcWatchTableService.ExportWatchTable(project, request.PlcName, request.TableName, request.FolderPath),
            PlcOnlineMethods.GetForceTableContent => PlcWatchTableService.ExportForceTable(project, request.PlcName, request.TableName, request.FolderPath),
            PlcOnlineMethods.CreateWatchTable => Json(PlcWatchTableService.CreateWatchTable(project, request.PlcName, request.TableName, request.FolderPath)),
            PlcOnlineMethods.DeleteWatchTable => Json(PlcWatchTableService.DeleteWatchTable(project, request.PlcName, request.TableName, request.FolderPath)),
            PlcOnlineMethods.CreateWatchTableGroup => Json(PlcWatchTableService.CreateGroup(project, request.PlcName, request.FolderPath)),
            PlcOnlineMethods.DeleteWatchTableGroup => Json(PlcWatchTableService.DeleteGroup(project, request.PlcName, request.FolderPath)),
            PlcOnlineMethods.UpdateWatchTableContent => Json(PlcWatchTableService.UpdateWatchTableContent(project, request.PlcName, request.TableName,
                request.FolderPath, request.Content, request.ExpectedContentHash)),
            PlcOnlineMethods.UpdateForceTableContent => Json(PlcWatchTableService.UpdateForceTableContent(project, request.PlcName, request.TableName,
                request.FolderPath, request.Content, request.ExpectedContentHash)),
            PlcOnlineMethods.UpdateProgram => Json(PlcWatchTableService.UpdateProgram(project, request.PlcName, portal)),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported worker method '{request.Method}'."),
        };
    }

    /// <summary>
    /// compare_to_online's scope answered every TIA Portal confirmation No. A declined dialog means the
    /// compare may not reflect the device, so the item fails and names the dialogs instead of returning
    /// the tree.
    /// </summary>
    private static PlcCompareResultInfo NothingDeclined(PlcCompareResultInfo result, PlcDownloadPortalConfirmationLog? confirmations)
    {
        if (confirmations is null) return result;
        List<PlcDownloadPortalConfirmationInfo> declined = new();
        var observed = 0;
        confirmations.CloseInto((entries, count, _) =>
        {
            declined = entries;
            observed = count;
        });
        if (observed == 0) return result;
        var listed = string.Join("; ", declined.Take(3).Select(entry => entry.Text is null ? entry.Type : $"{entry.Type}: {entry.Text}"));
        throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            $"TIA Portal raised {observed} confirmation dialog(s) during compare_to_online and each was answered No ({listed}"
            + (observed > 3 ? "; ..." : string.Empty) + "), so the compare result was discarded. Go online with plc_tia_online first, "
            + "answer the dialog there (portalConfirmations), then compare again.");
    }

    private static PlcOnlineRequestInfo Require(PlcOnlineRequestInfo? request)
        => request ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "The PlcOnline request is missing.");

    private static string Json<T>(T payload) => WorkerJson.SerializePayload(payload);
}
