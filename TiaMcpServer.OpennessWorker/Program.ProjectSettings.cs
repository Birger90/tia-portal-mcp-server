using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of plc_write set_project_compilation_settings; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse ProjectCompilationSettings(WorkerRequest request)
    {
        var info = request.ProjectCompilationSettings ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the compilation settings request.");
        return WithActiveContext(request, session => Success(ProjectCompilationSettingsService.Set(session.RequireStandaloneOwner().Project, info)));
    }
}
