using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Multiuser;

/// <summary>
/// Scripted Project Server state for project_server_write (TIA_MCP_FAKE_WORKER_PROJECT_SERVERS=1): one connection
/// <c>PsrvA</c> with the root project <c>LineA</c> (one local session, id 1) and the group <c>Plant</c> with <c>LineB</c>.
/// The inventory reads (list_server_connections, list_server_groups, list_server_projects, list_local_sessions) read this
/// state, so a write followed by its read-back is a real round trip. Object access only, no Siemens evidence. Markers: an
/// uploaded file named <c>Rejected.ap21</c> fails like a MultiuserException; a session named <c>AskDialog</c> raises one
/// TIA Portal confirmation answered by the item's policy.
/// </summary>
internal sealed class ProjectServerScenario
{
    private sealed class Server
    {
        public required string Alias { get; init; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Protocol { get; set; } = "Https";
        public List<string> RootProjects { get; } = new();
        public Dictionary<string, List<string>> Groups { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<MultiuserLocalSessionInfo>> Sessions { get; } = new(StringComparer.Ordinal);
    }

    private readonly List<Server> _servers = new();

    public static bool Declared => Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_PROJECT_SERVERS") == "1";

    public ProjectServerScenario()
    {
        var server = new Server { Alias = "PsrvA", Host = "mu-a.example", Port = 8735 };
        server.RootProjects.Add("LineA");
        server.Groups["Plant"] = new List<string> { "LineB" };
        server.Sessions[Key(true, null, "LineA")] = new List<MultiuserLocalSessionInfo>
        {
            new() { SessionId = 1, ProjectPath = Path.Combine(Path.GetTempPath(), "psrv-fixture", "LineA_1", "LineA_1.als21") },
        };
        _servers.Add(server);
    }

    private static string Key(bool? root, string? group, string? project) => $"{(root == true ? "/" : group)}|{project}";

    public WorkerResponse Inspect(WorkerRequest request, int? portal)
    {
        var observation = new ProjectServerConnectionObservation
            { State = "connected", ObservationSource = "explicitRead", ObservedAt = DateTimeOffset.Parse("2026-11-09T12:00:00Z") };
        if (request.Method == "list_server_connections")
            return Ok(new MultiuserServerConnectionsInfo
            {
                Connections = _servers.Select(s => new MultiuserServerConnectionInfo { ServerAlias = s.Alias, Host = s.Host, Port = s.Port }).ToList()
            }, portal);
        var server = _servers.FirstOrDefault(s => s.Alias == request.MultiuserServerAlias);
        if (server is null) return Fail(WorkerFailureCategories.TargetNotFound, "No remote target matches the exact selector.");
        var identity = new MultiuserRemoteIdentity
        {
            ServerAlias = server.Alias, Host = server.Host, Port = server.Port,
            Group = request.MultiuserGroupIsRoot is { } root ? new() { IsRoot = root, Name = request.MultiuserGroupName } : null,
            ServerProjectName = request.MultiuserServerProjectName,
        };
        List<string>? Projects() => request.MultiuserGroupIsRoot == true ? server.RootProjects
            : request.MultiuserGroupName is { } name && server.Groups.TryGetValue(name, out var rows) ? rows : null;
        switch (request.Method)
        {
            case "list_server_groups":
                return Ok(new MultiuserServerGroupsInfo
                {
                    RemoteIdentity = identity, ConnectionObservation = observation,
                    Groups = server.Groups.Keys.Select(name => new ProjectServerGroupIdentity { IsRoot = false, Name = name }).ToList(),
                }, portal);
            case "list_server_projects":
                return Projects() is { } projects
                    ? Ok(new MultiuserServerProjectsInfo
                    {
                        RemoteIdentity = identity, ConnectionObservation = observation,
                        Projects = projects.Select(name => new MultiuserServerProjectInfo { Name = name }).ToList(),
                    }, portal)
                    : Fail(WorkerFailureCategories.TargetNotFound, "No remote target matches the exact selector.");
            case "list_local_sessions":
                if (Projects() is not { } listed || !listed.Contains(request.MultiuserServerProjectName!))
                    return Fail(WorkerFailureCategories.TargetNotFound, "No remote target matches the exact selector.");
                return Ok(new MultiuserLocalSessionsInfo
                {
                    RemoteIdentity = identity, ConnectionObservation = observation,
                    Sessions = (server.Sessions.GetValueOrDefault(Key(request.MultiuserGroupIsRoot, request.MultiuserGroupName, request.MultiuserServerProjectName)) ?? new())
                        .Select(row => new MultiuserLocalSessionInfo { SessionId = row.SessionId, ProjectPath = row.ProjectPath }).ToList(),
                }, portal);
            default:
                return Fail(WorkerFailureCategories.ValidationError, "Unsupported scripted inventory method.");
        }
    }

    public WorkerResponse Write(WorkerRequest request, int? portal, bool acceptAllowed)
    {
        var info = request.ProjectServer!;
        var operation = ProjectServerMethods.OperationOf(request.Method)!;
        var result = new ProjectServerWriteResultInfo
            { Operation = operation, ServerAlias = info.ServerAlias ?? string.Empty, PortalConfirmationPolicy = info.PortalConfirmations };
        WorkerResponse Failed(string category, string message)
        {
            var failure = Fail(category, message);
            failure.PlcDownloadOutcome = WorkerJson.SerializePayload(result);
            return failure;
        }

        var server = _servers.FirstOrDefault(s => s.Alias == info.ServerAlias);
        if (operation != ProjectServerMethods.CreateServerConnection && server is null)
            return Failed(WorkerFailureCategories.TargetNotFound, $"No Project Server connection has the alias '{info.ServerAlias}'. Nothing was changed.");
        switch (operation)
        {
            case ProjectServerMethods.CreateServerConnection:
                server = new Server { Alias = info.ServerAlias!, Host = info.Host!, Port = info.Port!.Value, Protocol = info.Protocol! };
                _servers.Add(server);
                result.Applied.Add("ProjectServerComposition.Create");
                result.HostAfter = server.Host;
                result.PortAfter = server.Port;
                break;
            case ProjectServerMethods.SetServerConnection:
                if (info.Protocol is not null) { server!.Protocol = info.Protocol; result.Applied.Add("SetProtocol"); }
                if (info.Host is not null) { server!.Host = info.Host; result.Applied.Add("SetHostName"); }
                if (info.Port is not null) { server!.Port = info.Port.Value; result.Applied.Add("SetPort"); }
                result.HostAfter = server!.Host;
                result.PortAfter = server.Port;
                break;
            case ProjectServerMethods.DeleteServerConnection:
                _servers.Remove(server!);
                result.Applied.Add("DeleteConnection");
                break;
            case ProjectServerMethods.AddProjectToServer:
            {
                var name = Path.GetFileNameWithoutExtension(info.ProjectPath)!;
                if (name == "Rejected")
                {
                    var rejected = Failed(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the operation: The project could not be added to the server.");
                    rejected.EngineeringError = new EngineeringErrorInfo { Type = "MultiuserException", Message = "The project could not be added to the server." };
                    return rejected;
                }

                var list = info.GroupIsRoot == true ? server!.RootProjects : server!.Groups.GetValueOrDefault(info.GroupName!);
                if (list is null) return Failed(WorkerFailureCategories.TargetNotFound, $"The server lists no group '{info.GroupName}'. Nothing was changed.");
                list.Add(name);
                result.Applied.Add(info.GroupIsRoot == true ? "ProjectServer.AddProjectToServer" : "ProjectServerGroup.AddProjectToServer");
                break;
            }
            case ProjectServerMethods.CreateLocalSession:
            {
                var key = Key(info.GroupIsRoot, info.GroupName, info.ServerProjectName);
                if (!server!.Sessions.TryGetValue(key, out var sessions)) server.Sessions[key] = sessions = new();
                if (info.SessionName == "AskDialog")
                {
                    var yes = acceptAllowed && info.PortalConfirmations == PlcDownloadPolicy.PortalConfirmationPolicies.Accept;
                    result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo
                    {
                        Type = "Siemens.Engineering.ConfirmationEventArgs", Text = "Create the local session?",
                        Answer = yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No,
                    });
                    result.PortalConfirmationsObserved = 1;
                }

                var id = sessions.Count == 0 ? 1 : sessions.Max(row => row.SessionId) + 1;
                var path = Path.Combine(info.SessionDirectory!, info.SessionName!, info.SessionName + ".als21");
                sessions.Add(new MultiuserLocalSessionInfo { SessionId = id, ProjectPath = path });
                result.Applied.Add("CreateLocalSession");
                result.SessionId = id;
                result.SessionPath = path;
                break;
            }
            case ProjectServerMethods.DeleteLocalSessionFromServer:
            {
                var sessions = server!.Sessions.GetValueOrDefault(Key(info.GroupIsRoot, info.GroupName, info.ServerProjectName));
                var row = sessions?.FirstOrDefault(session => session.SessionId == info.SessionId);
                if (row is null) return Failed(WorkerFailureCategories.TargetNotFound, $"No local session with id {info.SessionId} is listed for this server project. Nothing was deleted.");
                sessions!.Remove(row);
                result.SessionId = row.SessionId;
                result.SessionPath = row.ProjectPath;
                result.Applied.Add("DeleteLocalSessionFromServer");
                break;
            }
        }

        return Ok(result, portal);
    }

    private static WorkerResponse Ok<T>(T payload, int? portal)
        => new() { Success = true, Payload = WorkerJson.SerializePayload(payload), PortalProcessId = portal };

    private static WorkerResponse Fail(string category, string message)
        => new() { Success = false, FailureCategory = category, Error = message };
}
