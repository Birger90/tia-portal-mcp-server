using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.BlockProtection;

namespace TiaMcpServer.Worker;

/// <summary>Block know-how and write protection calls (plc_read/plc_write, issue #5); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One block protection worker method. Carries password-source tokens only, never a password.</summary>
    public Task<WorkerCallResult> PlcBlockProtectionAsync(string method, PlcBlockProtectionRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcBlockProtectionRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request => request.PlcBlockProtection = prepared, "{}");
    }
}
