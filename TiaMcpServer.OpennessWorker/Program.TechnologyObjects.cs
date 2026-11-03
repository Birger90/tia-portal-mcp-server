using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the technology object methods; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse TechnologyObject(WorkerRequest request)
    {
        var info = request.TechnologyObject ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the technology object request.");
        return WithEngineeringRoot(request, project =>
        {
            switch (request.Method)
            {
                case TechnologyObjectMethods.List:
                    return Success(TechnologyObjectService.List(project, info));
                case TechnologyObjectMethods.GetParameters:
                    return Success(TechnologyObjectService.GetParameters(project, info));
                case TechnologyObjectMethods.GetHardwareConnection:
                    return Success(TechnologyObjectHardware.Read(project, info));
                case TechnologyObjectMethods.GetContent:
                    return RawPayload(TechnologyObjectService.GetContent(project, info));
                default:
                    var (result, warnings) = TechnologyObjectService.Write(project, request.Method, info);
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
