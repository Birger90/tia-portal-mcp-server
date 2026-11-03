using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.ChangeDetection;

namespace TiaMcpServer.Worker;

/// <summary>plc_read get_plc_checksums and get_fingerprints (issue #6); the same bound-project gates as every content read.</summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> PlcChangeDetectionAsync(string method, PlcChangeDetectionRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcChangeDetectionRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request => request.PlcChangeDetection = prepared, "{}");
    }
}
