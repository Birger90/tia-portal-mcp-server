using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;

namespace TiaMcpServer.Worker;

/// <summary>Technology object calls (plc_read/plc_write); they use the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One technology object worker method; writes set <c>Confirm</c> like every other data write.</summary>
    public Task<WorkerCallResult> TechnologyObjectAsync(string method, TechnologyObjectRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<TechnologyObjectRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request =>
        {
            request.TechnologyObject = prepared;
        }, "{}");
    }
}
