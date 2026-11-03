using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Security;

namespace TiaMcpServer.Worker;

/// <summary>CPU protection calls (plc_read/plc_write); they use the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One CPU protection worker method; writes set <c>Confirm</c> like every other data write. Never carries a password.</summary>
    public Task<WorkerCallResult> PlcSecurityAsync(string method, PlcSecurityRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcSecurityRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request =>
        {
            request.PlcSecurity = prepared;
        }, "{}");
    }
}
