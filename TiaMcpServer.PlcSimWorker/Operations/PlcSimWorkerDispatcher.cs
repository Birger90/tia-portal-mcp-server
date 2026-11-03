using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.PlcSimWorker.Runtime;

namespace TiaMcpServer.PlcSimWorker.Operations;

/// <summary>
/// One request in, one response out, in the Openness worker's envelope: the <c>hello</c> handshake
/// and protocol check come before authorization, and authorization comes before any PLCSIM API
/// call. Requests are handled one at a time by the worker's single read loop.
/// </summary>
internal sealed class PlcSimWorkerDispatcher
{
    private readonly McpAccessMode _mode;
    private readonly PlcSimOperationHandler _handler;

    public PlcSimWorkerDispatcher(McpAccessMode mode, PlcSimOperationHandler handler)
    {
        _mode = mode;
        _handler = handler;
    }

    public WorkerResponse HandleLine(string line)
    {
        WorkerRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope);
        }
        catch (JsonException exception)
        {
            return Failure(WorkerFailureCategories.ValidationError, $"Worker request was invalid JSON: {exception.Message}", null);
        }

        return request is null
            ? Failure(WorkerFailureCategories.ValidationError, "Worker request was empty.", null)
            : Handle(request);
    }

    public WorkerResponse Handle(WorkerRequest request)
    {
        if (string.Equals(request.Method, "hello", StringComparison.Ordinal))
        {
            if (!string.Equals(request.ProtocolVersion, PlcSimWorkerProtocol.Version, StringComparison.Ordinal))
                return Failure(WorkerFailureCategories.ValidationError,
                    $"PLCSIM worker protocol mismatch. Host requested '{request.ProtocolVersion ?? "(missing)"}', worker requires '{PlcSimWorkerProtocol.Version}'.", null);
            return new WorkerResponse
            {
                Success = true,
                Payload = "{}",
                ProtocolVersion = PlcSimWorkerProtocol.Version,
                Capabilities = PlcSimWorkerProtocol.RequiredCapabilities.ToList()
            };
        }

        if (!string.Equals(request.ProtocolVersion, PlcSimWorkerProtocol.Version, StringComparison.Ordinal))
            return Failure(WorkerFailureCategories.ValidationError,
                $"PLCSIM worker protocol mismatch. Request carried '{request.ProtocolVersion ?? "(missing)"}', worker requires '{PlcSimWorkerProtocol.Version}'. No PLCSIM API call was made.", null);

        var denial = WorkerOperationAuthorization.Authorize(_mode, request.Method);
        if (denial is not null)
            return denial;

        if (!PlcSimMethods.IsPlcSimMethod(request.Method))
            return Failure(WorkerFailureCategories.ValidationError, $"Unsupported PLCSIM worker method '{request.Method}'.", null);

        var warnings = new List<string>();
        WorkerResponse response;
        try
        {
            var payload = _handler.Execute(request.Method, request.PlcSim, warnings);
            response = new WorkerResponse
            {
                Success = true,
                Payload = WorkerJson.SerializePayload(payload),
                Warnings = warnings.Count == 0 ? null : warnings
            };
        }
        catch (PlcSimOperationException exception)
        {
            response = Failure(exception.Category, exception.Message, warnings);
        }
        catch (Exception exception)
        {
            response = Failure(WorkerFailureCategories.WorkerOperationFailed, $"{exception.GetType().Name}: {exception.Message}", warnings);
        }

        // Explicit, whatever level reported the failure (item, tag, test step or the whole request).
        if (_handler.RestartRequired)
            response.WorkerRestartRequired = true;
        return response;
    }

    private static WorkerResponse Failure(string category, string error, List<string>? warnings)
        => new()
        {
            Success = false,
            FailureCategory = category,
            Error = error,
            Warnings = warnings is { Count: > 0 } ? warnings : null
        };
}
