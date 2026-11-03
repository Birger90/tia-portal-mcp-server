using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Worker;

/// <summary>Hardware-module and tag-move calls; they use the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One hardware-module worker method; writes set <c>Confirm</c> like every other data write.</summary>
    public Task<WorkerCallResult> HardwareModuleAsync(string method, HardwareModuleRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<HardwareModuleRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request =>
        {
            request.HardwareModule = prepared;
        }, "{}");
    }

    public Task<WorkerCallResult> MoveTagAsync(PlcTagMoveRequestInfo move)
    {
        var prepared = JsonSerializer.Deserialize<PlcTagMoveRequestInfo>(JsonSerializer.Serialize(move, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(PlcTagMoveMethods.MoveTag, null, request =>
        {
            request.TagMove = prepared;
        }, "{}");
    }
}
