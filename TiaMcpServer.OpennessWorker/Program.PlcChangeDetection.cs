using TiaMcpServer.Contracts.Plc.ChangeDetection;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc.ChangeDetection;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of plc_read get_plc_checksums and get_fingerprints; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcChangeDetection(WorkerRequest request)
    {
        var info = request.PlcChangeDetection ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the change-detection request.");
        return WithEngineeringRoot(request, project => request.Method == PlcChangeDetectionMethods.GetPlcChecksums
            ? Success(PlcChangeDetectionService.ReadChecksums(project, info))
            : Success(PlcChangeDetectionService.ReadFingerprints(project, info)));
    }
}
