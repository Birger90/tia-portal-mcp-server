using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.Plc.DeviceData;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of plc_write download_to_folder and the online master secret (issue #16); authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcDeviceData(WorkerRequest request)
    {
        var info = request.PlcDeviceData ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the device data request.");
        info.Operation = request.Method;
        // Like plc_download (security review F6): until the item's evidence is collected, every TIA Portal confirmation is
        // answered by the item's portalConfirmations policy (No by default) and recorded; the service closes the scope before
        // it copies the evidence (it then lingers until the next request starts), this using is the safety net.
        var accept = string.Equals(info.PortalConfirmations, PlcDownloadPolicy.PortalConfirmationPolicies.Accept, StringComparison.Ordinal);
        var (log, scope) = PlcAlarmConfirmationScope.Open(_sharedSession, request.Method, accept, PlcDownloadService.ReadConfirmationText);
        using (scope)
        {
            return WithEngineeringRoot(request, project => Success(PlcDeviceDataService.Write(project, info, log)));
        }
    }
}
