using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

/// <summary>
/// project_server_write (owner decision 2026-11-09): one Portal-level Project Server mutation. Like the bind_project inventory
/// it needs no binding: a verified binding pins the TIA Portal (a different <paramref name="portalProcessId"/> is refused
/// before anything is sent) and its identity is sent and re-checked; an unbound session names the TIA Portal by
/// <paramref name="portalProcessId"/> or uses the one the worker attaches to.
/// </summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> ProjectServerAsync(string method, ProjectServerRequestInfo info, int? portalProcessId)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<ProjectServerRequestInfo>(JsonSerializer.Serialize(info, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return ExecuteSerializedBindingOperationAsync(async () =>
        {
            var before = BindingSnapshot;
            if (!ProjectServerMethods.IsWorkerMethod(method))
                return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, "Not a Project Server write method.")
                    with { DispatchState = WorkerDispatchState.NotSent };
            if (before.IsVerified && portalProcessId is { } pid && pid != before.PortalProcessId)
                return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                    "project_server_write acts on the TIA Portal of the bound project. Use bind_project to select another TIA Portal.")
                    with { DispatchState = WorkerDispatchState.NotSent };
            var sent = new WorkerRequest
            {
                Method = method,
                PortalProcessId = before.IsVerified ? before.PortalProcessId : portalProcessId,
                ExpectedSessionIdentity = before.IsVerified ? before.ToWorkerIdentity() : null,
                ProjectServer = prepared,
            };
            var result = await InvokeWorkerAsync(sent).ConfigureAwait(false);
            if (before.IsVerified && result.Success)
                result = ValidateOrPromoteSessionIdentity(result, before);
            if (result.Success && (result.PortalProcessId is null or <= 0
                || (sent.PortalProcessId is not null && result.PortalProcessId != sent.PortalProcessId)))
                result = WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                    "The worker did not report the asserted Portal attachment; the Project Server change may have completed. Inspect it with bind_project.",
                    result.Warnings) with { DispatchState = result.DispatchState, IsPostOperationFailure = true };
            return result;
        });
    }
}
