using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc.Settings;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the settings family (issues #15, #13, #12, #14, #21, #25); authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcSettings(WorkerRequest request)
    {
        var info = request.PlcSettings ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the settings request.");
        return WithEngineeringRoot(request, project =>
        {
            if (PlcSettingsCatalog.IsRead(request.Method))
                return Success(PlcSettingsService.Read(project, request.Method, info));
            var (result, warnings) = PlcSettingsService.Write(project, request.Method, info);
            return new WorkerResponse
            {
                Success = true,
                Payload = WorkerJson.SerializePayload(result),
                Warnings = warnings.Count > 0 ? warnings : null,
            };
        });
    }
}
