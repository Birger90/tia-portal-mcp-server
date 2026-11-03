using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Safety;

namespace TiaMcpServer.Worker;

/// <summary>Fail-safe (F-) PLC calls (plc_read/plc_write); they use the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One fail-safe worker method; writes set <c>Confirm</c> like every other data write. No password travels, only its variable name.</summary>
    public Task<WorkerCallResult> PlcSafetyAsync(string method, PlcSafetyRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcSafetyRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcSafety = prepared;
        }, "{}");
    }
}
