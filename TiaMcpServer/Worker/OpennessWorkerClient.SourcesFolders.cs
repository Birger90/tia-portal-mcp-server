using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Worker;

/// <summary>External-source, group-rename and accessible-device calls; they use the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One <see cref="PlcSourceFolderMethods"/> worker method; writes set <c>Confirm</c> like every other data write.</summary>
    public Task<WorkerCallResult> PlcSourceFolderAsync(string method, PlcSourceFolderRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcSourceFolderRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcSourceFolder = prepared;
        }, "{}");
    }
}
