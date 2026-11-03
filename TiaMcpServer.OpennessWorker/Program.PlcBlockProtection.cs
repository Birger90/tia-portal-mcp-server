using TiaMcpServer.Contracts.Plc.BlockProtection;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc.BlockProtection;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the block know-how and write protection methods (issue #5); authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcBlockProtection(WorkerRequest request)
    {
        var info = request.PlcBlockProtection ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the block protection request.");
        return WithEngineeringRoot(request, project => request.Method == PlcBlockProtectionMethods.GetBlockProtection
            ? Success(PlcBlockProtectionService.Read(project, info))
            : Success(PlcBlockProtectionService.Write(project, request.Method, info)));
    }
}
