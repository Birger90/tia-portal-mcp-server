using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;

namespace TiaMcpServer.Worker;

/// <summary>Library calls; they use the same bound-project gates as every content call.</summary>
public partial class OpennessWorkerClient
{
    /// <summary>One library_* worker method; writes set <c>Confirm</c> like every other data write.</summary>
    public Task<WorkerCallResult> LibraryAsync(string method, LibraryRequestInfo info)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<LibraryRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(method, null, request =>
        {
            request.Library = prepared;
        }, "{}");
    }
}
