using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc.Loadable;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of plc_write generate_loadable; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcLoadable(WorkerRequest request)
    {
        var info = request.PlcLoadable ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the loadable request.");
        return WithEngineeringRoot(request, project => Success(PlcLoadableService.Generate(project, info)));
    }
}
