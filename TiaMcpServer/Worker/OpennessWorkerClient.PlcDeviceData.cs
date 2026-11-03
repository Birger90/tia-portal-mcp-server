using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.DeviceData;

namespace TiaMcpServer.Worker;

/// <summary>plc_write download_to_folder and the online master secret (issue #16); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> PlcDeviceDataAsync(string method, PlcDeviceDataRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcDeviceDataRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request => request.PlcDeviceData = prepared, "{}");
    }
}
