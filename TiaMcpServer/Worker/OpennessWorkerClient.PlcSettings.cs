using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Settings;

namespace TiaMcpServer.Worker;

/// <summary>Settings family calls (plc_read/plc_write; issues #15, #13, #12, #14, #21, #25); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One settings worker method. Never carries a password, only password-source tokens.</summary>
    public Task<WorkerCallResult> PlcSettingsAsync(string method, PlcSettingsRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcSettingsRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request => request.PlcSettings = prepared, "{}");
    }
}
