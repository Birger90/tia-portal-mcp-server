using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ProjectServers;

[McpServerToolType]
public class ProjectServerWriteTools
{
    [McpServerTool(Name = ProjectServerMethods.WriteTool, ReadOnly = false, Destructive = true, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(ProjectServerGuardedWriteResponse))]
    [Description("Project Server (Multiuser) administration of one TIA Portal instance (not live-tested): up to 50 ordered items on the "
        + "Project Server connections of TIA Portal and on the servers they reach. Needs no bound project; with a binding it acts in the "
        + "bound project's TIA Portal, otherwise in portalProcessId's (or the one the server attaches to). Operations: "
        + "create_server_connection (serverAlias, protocol Https|Http, host, port: ProjectServerComposition.Create), "
        + "set_server_connection (serverAlias; host, port and/or protocol: SetHostName/SetPort/SetProtocol), "
        + "delete_server_connection (serverAlias: DeleteConnection), "
        + "add_project_to_server (serverAlias, group, projectPath of an existing .ap21: ProjectServer/ProjectServerGroup.AddProjectToServer), "
        + "create_local_session (serverAlias, group, serverProjectName, sessionName, sessionDirectory, sessionMode Multiuser (default)|Exclusive: "
        + "CreateLocalSession; open the returned .als21 path with open_project to bind to it), "
        + "delete_local_session_from_server (serverAlias, group, serverProjectName, sessionId from bind_project list_local_sessions: "
        + "DeleteLocalSessionFromServer). group is {isRoot:true,name:null} or {isRoot:false,name} as in bind_project. "
        + "The caller chooses every server: never act on a server the user did not name. Targets are planned from the bind_project "
        + "inventory (a missing alias, group, project or session refuses the whole call before any item runs); a later item may use a "
        + "connection an earlier item creates. Guards: deleting a local session, deleting a connection and uploading a project are "
        + "acknowledge guards: read-write asks the user to confirm the call, full satisfies them by policy; other items never prompt. "
        + "No item takes a password; server authentication is TIA Portal's own. Every item takes portalConfirmations: TIA Portal "
        + "confirmation dialogs are answered No (decline, default; recorded, the item fails) or Yes (accept; recorded, a warning). "
        + "Omitted dryRun or false executes; true previews targets and guards. Stops at the first failed item without rollback or "
        + "replay. Verification reads back through list_server_connections (host, port; the protocol has no documented read), "
        + "list_server_projects (a new project is listed) and list_local_sessions (the created id is listed, a deleted id is gone).")]
    public static Task<CallToolResult> ProjectServerWrite(OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Ordered Project Server items.")] ProjectServerOperationRequest[] operations,
        [Description("True previews without mutation or confirmation; omitted or false executes.")] bool dryRun = false,
        [Description("Optional TIA Portal process (bind_project list_portals) when no project is bound; with a binding it must be the bound project's.")] int? portalProcessId = null,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => execution.RunAsync(new ProjectServerWriteDomain(workerClient, portalProcessId),
            new WriteCall<ProjectServerOperationRequest>(operations ?? [], dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            new ProjectServerBindingStrategy(workerClient), cancellationToken);
}

/// <summary>
/// project_server_write needs no binding: it pins whatever the session holds (unbound, configured or verified), so the call runs
/// under one lease and a binding change during the call is refused like every other write. It never grounds, adopts or opens.
/// </summary>
public sealed class ProjectServerBindingStrategy(OpennessWorkerClient workerClient) : IWriteBindingStrategy<ProjectServerOperationRequest>
{
    public Task<WriteBindingPreparation> PrepareAsync(WriteCall<ProjectServerOperationRequest> call, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var method in ProjectServerMethods.WorkerMethods)
        {
            var denial = workerClient.AccessPolicy.Authorize(method);
            if (denial is not null)
                return Task.FromResult(WriteBindingPreparation.Rejected(new(denial.FailureCategory ?? WorkerFailureCategories.AccessDenied,
                    denial.Error ?? "project_server_write is not allowed in this access mode.")));
        }

        return Task.FromResult(WriteBindingPreparation.Prepared(workerClient.BindingSnapshot));
    }
}
