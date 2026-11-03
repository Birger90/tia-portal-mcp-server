using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Safety;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the fail-safe (F-) PLC methods; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcSafety(WorkerRequest request)
    {
        var info = request.PlcSafety ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the fail-safe request.");
        return WithActiveContext(request, session =>
        {
            var project = session.EngineeringRoot!;
            switch (request.Method)
            {
                case PlcSafetyMethods.GetAdministration:
                    return Success(PlcSafetyService.GetAdministration(project, session.TiaPortal, info));
                case PlcSafetyMethods.ListBlocks:
                    return Success(PlcSafetyService.ListBlocks(project, info));
                default:
                    var (result, warnings) = PlcSafetyService.Write(project, request.Method, info);
                    return new WorkerResponse
                    {
                        Success = true,
                        Payload = WorkerJson.SerializePayload(result),
                        Warnings = warnings.Count > 0 ? warnings : null,
                    };
            }
        });
    }
}
