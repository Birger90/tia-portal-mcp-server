using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Loadable;

namespace TiaMcpServer.Worker;

/// <summary>plc_write generate_loadable (issue #26); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> GenerateLoadableAsync(PlcLoadableRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcLoadableRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(PlcLoadableMethods.GenerateLoadable, null, request => request.PlcLoadable = prepared, "{}");
    }
}
