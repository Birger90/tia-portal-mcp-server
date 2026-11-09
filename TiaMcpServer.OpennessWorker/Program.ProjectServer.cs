using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Multiuser;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Dispatch of the <c>project_server_*</c> methods (project_server_write); authorization already ran in HandleLine. Like the
/// Multiuser inventory reads these run Portal-only (no project is selected, opened or adopted) under
/// <see cref="MultiuserPortalReadDispatch.Run"/>: the asserted TIA Portal and, when the host sends one, the bound session's
/// identity are checked before and after the call. They are mutating for <see cref="WorkerExecution"/>.
/// </summary>
internal static partial class Program
{
    private static WorkerResponse ProjectServer(WorkerRequest request)
        => Execute(() => MultiuserPortalReadDispatch.Run(_sharedSession, request,
            session => ProjectServerWriteService.Write(session, request.Method, request.ProjectServer!), ValidateProjectServerRequest), mutates: true);

    private static void ValidateProjectServerRequest(WorkerRequest request)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(WorkerRequest.Method), nameof(WorkerRequest.ProtocolVersion), nameof(WorkerRequest.ExpectedSessionIdentity),
            nameof(WorkerRequest.PortalProcessId), nameof(WorkerRequest.ProjectServer),
        };
        var defaults = new WorkerRequest();
        foreach (var property in typeof(WorkerRequest).GetProperties())
            if (!allowed.Contains(property.Name) && !Equals(property.GetValue(request), property.GetValue(defaults)))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{request.Method} accepts no field {property.Name}.");
        if (!ProjectServerMethods.IsWorkerMethod(request.Method) || request.ProjectServer is null
            || request.ProjectServer.Operation != ProjectServerMethods.OperationOf(request.Method))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{request.Method} requires its Project Server request.");
        if (request.PortalProcessId is <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "portalProcessId must be positive.");
    }
}
