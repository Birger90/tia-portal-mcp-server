using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.OpcUa;

namespace TiaMcpServer.Worker;

/// <summary>plc_read get_opcua_configuration and the plc_write OPC UA operations (issue #24); the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> PlcOpcUaAsync(string method, PlcOpcUaRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<PlcOpcUaRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request => request.PlcOpcUa = prepared, "{}");
    }
}
