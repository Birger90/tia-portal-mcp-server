using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.ProjectServers;

/// <summary>
/// project_server_write guards (owner decision 2026-11-09). Deleting a local session from the server, deleting a server
/// connection and uploading a project to a server are acknowledge guards: read-write asks the human for the call, full
/// satisfies them by policy. Everything else is informative: TIA Portal decides (owner principle: never stricter than TIA).
/// </summary>
public static class ProjectServerGuardDefinitions
{
    public const string DeletesLocalSession = "project_server_deletes_local_session";
    public const string DeletesConnection = "project_server_deletes_connection";
    public const string UploadsProject = "project_server_uploads_project";
    public const string ChangesConnection = "project_server_changes_connection";
    public const string CreatesConnection = "project_server_creates_connection";
    public const string CreatesLocalSession = "project_server_creates_local_session";
    public const string AliasExists = "project_server_alias_exists";
    public const string BoundSession = "project_server_bound_session";
    public const string ResolvedLater = "project_server_resolved_at_mutation";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new WriteGuardDefinition(DeletesLocalSession, WriteGuardSeverities.Acknowledge,
            "The item deletes a local session from the Project Server (DeleteLocalSessionFromServer). Unsaved or not checked-in changes of that session may be lost; TIA Portal does not offer an undo."),
        new WriteGuardDefinition(DeletesConnection, WriteGuardSeverities.Acknowledge,
            "The item deletes a Project Server connection of this TIA Portal instance (DeleteConnection). Local sessions of that server cannot reach it until a connection is created again."),
        new WriteGuardDefinition(UploadsProject, WriteGuardSeverities.Acknowledge,
            "The item uploads a single-user project to a Project Server (AddProjectToServer); it becomes a server project other users of that server can see and open."),
        new WriteGuardDefinition(ChangesConnection, WriteGuardSeverities.Info,
            "The item changes the host, port or protocol of a Project Server connection of this TIA Portal instance; its local sessions then reach the new endpoint."),
        new WriteGuardDefinition(CreatesConnection, WriteGuardSeverities.Info,
            "The item adds a Project Server connection to this TIA Portal instance (ProjectServerComposition.Create)."),
        new WriteGuardDefinition(CreatesLocalSession, WriteGuardSeverities.Info,
            "The item creates a local session of a server project on the server and in a local directory (CreateLocalSession); open it with open_project."),
        new WriteGuardDefinition(AliasExists, WriteGuardSeverities.Info,
            "A connection with this alias is already listed; TIA Portal decides whether it accepts a second one."),
        new WriteGuardDefinition(BoundSession, WriteGuardSeverities.Info,
            "The local session to delete is the project this MCP session is bound to; TIA Portal decides what happens to the open session."),
        new WriteGuardDefinition(ResolvedLater, WriteGuardSeverities.Info,
            "The item acts on a connection an earlier item of this call creates or changes, so its group, project or session is resolved only when it runs."),
    });
}
