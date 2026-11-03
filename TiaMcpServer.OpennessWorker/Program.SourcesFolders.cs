using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.PlcOnline;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Dispatch of the external-source, tag-comment, group-rename, group create/delete, software-unit and
/// accessible-device methods
/// (<see cref="PlcSourceFolderMethods"/>); authorization already ran in HandleLine. Every write requires the
/// worker-internal confirmation fence like the other data writes. Content handlers use the bound engineering
/// root, so a local session reaches them where the capability gate allows it (the scan is standalone-only).
/// </summary>
internal static partial class Program
{
    private static WorkerResponse SourcesFolders(WorkerRequest request)
    {
        var info = request.PlcSourceFolder ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the external-source/folder request.");
        if (request.Method == PlcSourceFolderMethods.ScanAccessibleDevices)
        {
            // Like compare_to_online: a fixed-No confirmation scope (never the session-wide Yes of read-write/full);
            // the service closes it after it projected the result (or when the scan failed), this using is the safety net.
            var (confirmations, scope) = PlcOnlineConfirmationScope.OpenDeclining(_sharedSession, request.Method, PlcDownloadService.ReadConfirmationText);
            using (scope)
                return WithEngineeringRoot(request, project => Success(PlcAccessibleDeviceService.Scan(project, info, confirmations)));
        }

        return WithEngineeringRoot(request, project => request.Method switch
        {
            PlcSourceFolderMethods.ListExternalSources => Success(PlcExternalSourceService.List(project, info)),
            PlcSourceFolderMethods.ReadTagComments => Success(PlcTagCommentService.Read(project, info)),
            PlcSourceFolderMethods.ImportExternalSource => Success(PlcExternalSourceService.Import(project, info)),
            PlcSourceFolderMethods.GenerateBlocksFromSource => Success(PlcExternalSourceService.Generate(project, info)),
            PlcSourceFolderMethods.DeleteExternalSource => Success(PlcExternalSourceService.Delete(project, info)),
            PlcSourceFolderMethods.ExportSourceFile => Success(PlcExternalSourceService.ExportSourceFile(project, info)),
            var method when PlcSourceFolderMethods.GroupRenames.Contains(method, StringComparer.Ordinal)
                => Success(PlcGroupRenameService.Rename(project, method, info)),
            var method when PlcSourceFolderMethods.GroupCreatesDeletes.Contains(method, StringComparer.Ordinal)
                || PlcSourceFolderMethods.SoftwareUnitOperations.Contains(method, StringComparer.Ordinal)
                => Success(PlcContainerService.Execute(project, method, info)),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"Unsupported external-source or folder method '{request.Method}'."),
        });
    }
}
