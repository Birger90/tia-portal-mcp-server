using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Worker;

/// <summary>Project languages and project texts (plc_read/plc_write, issue #8); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> ProjectLanguagesAsync(string method, ProjectLanguagesRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<ProjectLanguagesRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request => request.ProjectLanguages = prepared, "{}");
    }
}
