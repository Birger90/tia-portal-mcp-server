using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Worker;

/// <summary>plc_write set_project_compilation_settings (K10); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> ProjectCompilationSettingsAsync(ProjectCompilationSettingsRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<ProjectCompilationSettingsRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(ProjectCompilationSettingsMethods.Set, null, request =>
        {
            request.ProjectCompilationSettings = prepared;
        }, "{}");
    }
}
