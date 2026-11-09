using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.ProjectServers;

/// <summary>
/// The read-back of project_server_write through the bind_project inventory reads (owner decision 2026-11-09): connections by
/// list_server_connections (alias, host, port; the protocol has no documented read), uploads by list_server_projects (a new
/// server project appeared), local sessions by list_local_sessions (the created id is listed, the deleted id is gone). A failed
/// item is read back too and never verified (K58). Keys: the connection, then the server content below it, so a later change or
/// delete of the connection supersedes the earlier items on it.
/// </summary>
internal sealed class ProjectServerVerificationPolicy(ProjectServerWriteDomain domain) : IWriteVerificationPolicy<ProjectServerOperationRequest>
{
    public string ReadTool => "bind_project";

    public IReadOnlyList<string> ObjectKeys(ProjectServerOperationRequest item, bool asLater)
    {
        var server = "srv\n" + item.ServerAlias;
        var group = item.Group?.IsRoot == true ? "root" : "group:" + item.Group?.Name;
        return item.Operation switch
        {
            ProjectServerMethods.CreateServerConnection or ProjectServerMethods.SetServerConnection or ProjectServerMethods.DeleteServerConnection => [server],
            ProjectServerMethods.AddProjectToServer => [$"{server}\nprojects\n{group}\nupload:{item.OperationId}"],
            ProjectServerMethods.CreateLocalSession => [$"{server}\nsessions\n{group}\n{item.ServerProjectName}\nnew:{item.OperationId}"],
            _ => [$"{server}\nsessions\n{group}\n{item.ServerProjectName}\n{item.SessionId}"],
        };
    }

    public bool HasReadBack(ProjectServerOperationRequest item)
        => item.Operation != ProjectServerMethods.SetServerConnection || item.Host is not null || item.Port is not null;

    public string? WorkerCheckEvidence(ProjectServerOperationRequest item)
        => item.Operation == ProjectServerMethods.SetServerConnection
            ? "only the protocol was set and no documented member reads a connection's protocol back."
            : null;

    public string? SupersessionEvidence(ProjectServerOperationRequest item, ProjectServerOperationRequest later)
        => item.Operation is ProjectServerMethods.CreateServerConnection or ProjectServerMethods.SetServerConnection ? null
            : later.Operation == ProjectServerMethods.DeleteServerConnection
                ? $"the connection '{item.ServerAlias}' was deleted later in this call, so the server-side result can no longer be read back through it; inspect it after creating a connection again."
            : later.Operation is ProjectServerMethods.SetServerConnection or ProjectServerMethods.CreateServerConnection
                ? $"the connection '{item.ServerAlias}' was changed later in this call, so the server-side result is not verified; inspect it with bind_project."
            : null;

    public async Task<WriteOperationVerification> ReadBackAsync(ProjectServerOperationRequest item, StructuredOperationFailure? failure)
    {
        var id = item.OperationId;
        switch (item.Operation)
        {
            case ProjectServerMethods.CreateServerConnection:
            case ProjectServerMethods.SetServerConnection:
            case ProjectServerMethods.DeleteServerConnection:
            {
                var read = await domain.ReadAsync(domain.Inventory("list_server_connections", null, null, null), info => info.ServerConnections).ConfigureAwait(false);
                if (read.Error is { } error) return Unread(id, "serverConnection", error.Message);
                var rows = read.Value!.Connections.Where(row => row.ServerAlias == item.ServerAlias).ToArray();
                if (item.Operation == ProjectServerMethods.DeleteServerConnection)
                    return new(id, rows.Length == 0, "serverConnection", "absent", rows.Length == 0 ? "absent" : $"{rows.Length} listed",
                        rows.Length == 0 ? $"Operation '{id}': the connection '{item.ServerAlias}' is no longer listed by list_server_connections."
                            : $"Operation '{id}': the connection '{item.ServerAlias}' is still listed by list_server_connections.");
                if (rows.Length != 1)
                    return new(id, false, "serverConnection", Endpoint(item.Host, item.Port), rows.Length == 0 ? "absent" : $"{rows.Length} listed",
                        $"Operation '{id}': list_server_connections lists {rows.Length} connection(s) with the alias '{item.ServerAlias}'.");
                var row = rows[0];
                var matches = (item.Host is null || string.Equals(row.Host, item.Host, StringComparison.Ordinal)) && (item.Port is null || row.Port == item.Port);
                return new(id, matches, "serverConnection", Endpoint(item.Host, item.Port), Endpoint(row.Host, row.Port),
                    $"Operation '{id}': list_server_connections reads {row.Host}:{row.Port} for '{item.ServerAlias}'"
                    + (matches ? "." : ", not the requested value.")
                    + (item.Protocol is null ? string.Empty : " The protocol has no documented read and is not verified."));
            }
            case ProjectServerMethods.AddProjectToServer:
            {
                var read = await domain.ReadAsync(domain.Inventory("list_server_projects", item.ServerAlias, item.Group, null), info => info.ServerProjects).ConfigureAwait(false);
                if (read.Error is { } error) return Unread(id, "serverProject", error.Message);
                var names = read.Value!.Projects.Select(project => project.Name).ToArray();
                var stem = Path.GetFileNameWithoutExtension(item.ProjectPath);
                var before = domain.Planned.GetValueOrDefault(id) is { ResolvedLater: false, ProjectsBefore: { } listed } ? listed : null;
                if (before is null)
                {
                    var listedNow = names.Contains(stem, StringComparer.Ordinal);
                    return new(id, listedNow, "serverProject", stem, listedNow ? stem : "not listed",
                        $"Operation '{id}': " + (listedNow ? $"a server project '{stem}' is listed (the listing before the call was not read, so it may have existed before)."
                            : $"no server project '{stem}' is listed; TIA Portal may have named it differently. Inspect with bind_project list_server_projects."));
                }

                var added = names.Except(before, StringComparer.Ordinal).ToArray();
                return new(id, added.Length > 0, "serverProject", stem, added.Length == 0 ? "no new project" : string.Join(", ", added),
                    added.Length > 0 ? $"Operation '{id}': list_server_projects lists the new server project(s) {string.Join(", ", added.Select(name => $"'{name}'"))}."
                        : $"Operation '{id}': list_server_projects lists no project that was not listed before the call.");
            }
            case ProjectServerMethods.CreateLocalSession:
            case ProjectServerMethods.DeleteLocalSessionFromServer:
            {
                var read = await domain.ReadAsync(domain.Inventory("list_local_sessions", item.ServerAlias, item.Group, item.ServerProjectName), info => info.LocalSessions).ConfigureAwait(false);
                if (read.Error is { } error) return Unread(id, "localSession", error.Message);
                var sessions = read.Value!.Sessions;
                if (item.Operation == ProjectServerMethods.DeleteLocalSessionFromServer)
                {
                    var gone = sessions.All(session => session.SessionId != item.SessionId);
                    return new(id, gone, "localSession", "absent", gone ? "absent" : "listed",
                        $"Operation '{id}': local session {item.SessionId} is " + (gone ? "no longer listed" : "still listed") + $" by list_local_sessions (scope {read.Value.Scope}).");
                }

                var created = domain.Results.GetValueOrDefault(id);
                if (created?.SessionId is { } sessionId)
                {
                    var row = sessions.FirstOrDefault(session => session.SessionId == sessionId);
                    var samePath = row is not null && (created.SessionPath is null
                        || string.Equals(Path.GetFullPath(row.ProjectPath), Path.GetFullPath(created.SessionPath), StringComparison.OrdinalIgnoreCase));
                    return new(id, samePath, "localSession", $"{sessionId} {created.SessionPath}", row is null ? "not listed" : $"{row.SessionId} {row.ProjectPath}",
                        $"Operation '{id}': " + (samePath ? $"list_local_sessions lists session {sessionId} at {row!.ProjectPath}; open it with open_project."
                            : $"list_local_sessions does not list session {sessionId} at the path CreateLocalSession returned."));
                }

                var before = domain.Planned.GetValueOrDefault(id)?.SessionsBefore;
                var fresh = before is null ? [] : sessions.Where(session => !before.Contains(session.SessionId)).ToArray();
                return new(id, fresh.Length == 1, "localSession", "one new session", fresh.Length == 0 ? "none" : string.Join(", ", fresh.Select(s => $"{s.SessionId} {s.ProjectPath}")),
                    $"Operation '{id}': CreateLocalSession returned no readable session; list_local_sessions lists {fresh.Length} session(s) that were not listed before the call.");
            }
            default:
                return Unread(id, "serverConnection", "Unknown operation.");
        }
    }

    private static string? Endpoint(string? host, int? port) => host is null && port is null ? null : $"{host ?? "*"}:{port?.ToString() ?? "*"}";

    private static WriteOperationVerification Unread(string id, string check, string message)
        => new(id, false, check, null, null, $"Operation '{id}': the state could not be read back ({message}). Inspect it with bind_project before retrying.");
}
