using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc.Security;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the CPU protection methods; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcSecurity(WorkerRequest request)
    {
        var info = request.PlcSecurity ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the CPU protection request.");
        return WithEngineeringRoot(request, project =>
        {
            if (request.Method == PlcSecurityMethods.GetSecurity)
                return Success(PlcSecurityService.Read(project, info));
            var (result, warnings) = PlcSecurityService.Write(project, request.Method, info);
            return new WorkerResponse
            {
                Success = true,
                Payload = WorkerJson.SerializePayload(result),
                Warnings = warnings.Count > 0 ? warnings : null,
            };
        });
    }
}
