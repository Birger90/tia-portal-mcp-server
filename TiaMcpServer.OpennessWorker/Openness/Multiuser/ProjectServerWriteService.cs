using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker.Openness.Multiuser;

/// <summary>
/// The mutations of <c>project_server_write</c> (owner decision 2026-11-09; not live-tested). Every documented V21 member that
/// is not on the compile stubs (<c>ProjectServerComposition.Create</c>, <c>ProjectServer.SetHostName/SetPort/SetProtocol</c>,
/// <c>.DeleteConnection</c>, <c>.AddProjectToServer</c>, <c>.CreateLocalSession</c>, <c>.DeleteLocalSessionFromServer</c>,
/// <c>ProjectServerGroup.AddProjectToServer</c>) is bound at runtime by its exact name and documented parameter list
/// (<c>LibraryReflection.InvokeSignature</c>;
/// enums by their documented full names); a missing member is <c>unsupported_capability</c> and nothing is called. Targets are
/// resolved exactly like the inventory reads (alias, then root or named group, then project name, then session id); a
/// <c>MultiuserException</c> (the documented error of every Multiuser call) or an <c>EngineeringTargetInvocationException</c> is
/// TIA Portal's refusal (<c>engineering_rejected</c>). TIA Portal confirmation dialogs are answered by the item's
/// <c>portalConfirmations</c> policy and recorded (library_write's rule). No member takes credentials.
/// </summary>
internal static class ProjectServerWriteService
{
    private const string MultiuserExceptionType = "Siemens.Engineering.Multiuser.MultiuserException";

    public static WorkerResponse Write(Session session, string method, ProjectServerRequestInfo request)
    {
        var operation = ProjectServerMethods.OperationOf(method)
            ?? throw Validation($"Unsupported Project Server write method '{method}'.");
        if (request.Operation != operation) throw Validation("The Project Server request does not match its worker method.");
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(request.PortalConfirmations))
            throw Validation($"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        var result = new ProjectServerWriteResultInfo
        {
            Operation = operation, ServerAlias = request.ServerAlias ?? string.Empty, PortalConfirmationPolicy = request.PortalConfirmations,
        };
        var log = new PlcDownloadPortalConfirmationLog();
        var accept = string.Equals(request.PortalConfirmations, PlcDownloadPolicy.PortalConfirmationPolicies.Accept, StringComparison.Ordinal);
        var scope = session.AnswerConfirmations(accept,
            (args, answer) => log.Record(args.GetType().FullName, PlcDownloadService.ReadConfirmationText(args),
                answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No),
            owner: method);
        log.AttachScope(scope);
        using (scope)
        {
            try
            {
                Mutate(session.TiaPortal ?? throw new WorkerOperationException(WorkerFailureCategories.PortalLost, "No TIA Portal is attached."),
                    operation, request, result);
            }
            // The documented error of every Multiuser call; checked first in case it derives from EngineeringException.
            catch (Exception ex) when (ex.GetType().FullName == MultiuserExceptionType)
            {
                throw Failure(WorkerFailureCategories.EngineeringRejected, $"TIA Portal rejected the operation: {ex.Message}",
                    WorkerExecution.Describe(ex), result, log);
            }
            catch (NonRecoverableException ex)
            {
                throw Failure(WorkerFailureCategories.PortalLost,
                    $"The TIA Portal connection was lost during {operation}: {ex.Message}. The Project Server change may have completed; inspect it with bind_project before retrying.",
                    null, result, log);
            }
            catch (EngineeringException ex)
            {
                var category = WorkerExecution.Classify(ex, mutates: true);
                throw Failure(category, WorkerExecution.FailureMessage(ex, category), WorkerExecution.Describe(ex), result, log);
            }
            catch (WorkerOperationException ex) when (ex.PlcDownloadOutcome is null)
            {
                throw Failure(ex.FailureCategory, ex.Message, ex.EngineeringError, result, log);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
            {
                throw Failure(WorkerFailureCategories.WorkerOperationFailed, ex.Message, null, result, log);
            }

            Close(result, log);
            return new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(result) };
        }
    }

    private static void Mutate(TiaPortal portal, string operation, ProjectServerRequestInfo request, ProjectServerWriteResultInfo result)
    {
        switch (operation)
        {
            case ProjectServerMethods.CreateServerConnection:
            {
                RequireText(request.ServerAlias, "serverAlias");
                RequireText(request.Host, "host");
                var created = LibraryReflection.InvokeSignature(portal.ProjectServers, "Create", new[]
                {
                    SignatureArgument.Of(request.ServerAlias!), SignatureArgument.Enum(ProjectServerMethods.ProtocolEnum, Protocol(request)),
                    SignatureArgument.Of(request.Host!), SignatureArgument.Of(Port(request)),
                }, "ProjectServerComposition.Create(String, Protocol, String, Int32)") as ProjectServer;
                result.Applied.Add("ProjectServerComposition.Create");
                var server = created ?? FindServer(portal, request.ServerAlias!);
                ReadBack(server, result);
                break;
            }
            case ProjectServerMethods.SetServerConnection:
            {
                var server = FindServer(portal, request.ServerAlias!);
                if (request.Protocol is not null)
                {
                    LibraryReflection.InvokeSignature(server, "SetProtocol",
                        new[] { SignatureArgument.Enum(ProjectServerMethods.ProtocolEnum, Protocol(request)) }, "ProjectServer.SetProtocol(Protocol)");
                    result.Applied.Add("SetProtocol");
                }

                if (request.Host is not null)
                {
                    LibraryReflection.InvokeSignature(server, "SetHostName", new[] { SignatureArgument.Of(request.Host) }, "ProjectServer.SetHostName(String)");
                    result.Applied.Add("SetHostName");
                }

                if (request.Port is not null)
                {
                    LibraryReflection.InvokeSignature(server, "SetPort", new[] { SignatureArgument.Of(Port(request)) }, "ProjectServer.SetPort(Int32)");
                    result.Applied.Add("SetPort");
                }

                ReadBack(server, result);
                break;
            }
            case ProjectServerMethods.DeleteServerConnection:
            {
                var server = FindServer(portal, request.ServerAlias!);
                LibraryReflection.InvokeSignature(server, "DeleteConnection", Array.Empty<SignatureArgument>(), "ProjectServer.DeleteConnection()");
                result.Applied.Add("DeleteConnection");
                break;
            }
            case ProjectServerMethods.AddProjectToServer:
            {
                RequireText(request.ProjectPath, "projectPath");
                if (!File.Exists(request.ProjectPath)) throw Validation("projectPath must be an existing single-user project file.");
                var server = FindServer(portal, request.ServerAlias!);
                var owner = request.GroupIsRoot == true ? (object)server : FindGroup(server, request.GroupName);
                LibraryReflection.InvokeSignature(owner, "AddProjectToServer", new[] { SignatureArgument.Of(new FileInfo(request.ProjectPath!)) },
                    request.GroupIsRoot == true ? "ProjectServer.AddProjectToServer(FileInfo)" : "ProjectServerGroup.AddProjectToServer(FileInfo)");
                result.Applied.Add(request.GroupIsRoot == true ? "ProjectServer.AddProjectToServer" : "ProjectServerGroup.AddProjectToServer");
                break;
            }
            case ProjectServerMethods.CreateLocalSession:
            {
                RequireText(request.SessionName, "sessionName");
                RequireText(request.SessionDirectory, "sessionDirectory");
                var server = FindServer(portal, request.ServerAlias!);
                var project = FindProject(server, request);
                var mode = request.SessionMode ?? ProjectServerMethods.SessionCreationModes[0];
                var created = LibraryReflection.InvokeSignature(server, "CreateLocalSession", new[]
                {
                    SignatureArgument.Of(project, project.GetType()), SignatureArgument.Of(request.SessionName!),
                    SignatureArgument.Of(new DirectoryInfo(request.SessionDirectory!)), SignatureArgument.Enum(ProjectServerMethods.SessionCreationModeEnum, mode),
                }, "ProjectServer.CreateLocalSession(ServerProjectInfo, String, DirectoryInfo, SessionCreationMode)");
                result.Applied.Add("CreateLocalSession");
                if (created is LocalSessionInfo info)
                {
                    result.SessionId = info.SessionId;
                    result.SessionPath = info.ProjectFileInfo?.FullName;
                }

                break;
            }
            case ProjectServerMethods.DeleteLocalSessionFromServer:
            {
                var server = FindServer(portal, request.ServerAlias!);
                var project = FindProject(server, request);
                var sessions = (server.GetLocalSessions(project) ?? throw Incomplete()).Where(row => row is not null && row.SessionId == request.SessionId).ToList();
                if (sessions.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No local session with id {request.SessionId} is listed for this server project. Nothing was deleted.");
                if (sessions.Count > 1) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Several local sessions have id {request.SessionId}. Nothing was deleted.");
                result.SessionId = sessions[0].SessionId;
                result.SessionPath = sessions[0].ProjectFileInfo?.FullName;
                LibraryReflection.InvokeSignature(server, "DeleteLocalSessionFromServer",
                    new[] { SignatureArgument.Of(project, project.GetType()), SignatureArgument.Of(sessions[0], sessions[0].GetType()) },
                    "ProjectServer.DeleteLocalSessionFromServer(ServerProjectInfo, LocalSessionInfo)");
                result.Applied.Add("DeleteLocalSessionFromServer");
                break;
            }
            default:
                throw Validation($"Unsupported Project Server operation '{operation}'.");
        }
    }

    private static void ReadBack(ProjectServer server, ProjectServerWriteResultInfo result)
    {
        try
        {
            result.HostAfter = server.Host;
            result.PortAfter = server.Port;
        }
        catch (EngineeringException ex) when (OpennessExceptions.IsRecoverable(ex))
        {
            result.HostAfter = null;
            result.PortAfter = null;
        }
    }

    private static ProjectServer FindServer(TiaPortal portal, string alias)
    {
        RequireText(alias, "serverAlias");
        var matches = new List<ProjectServer>();
        foreach (var server in portal.ProjectServers)
        {
            if (server is null) throw Incomplete();
            if (string.Equals(server.ServerName, alias, StringComparison.Ordinal)) matches.Add(server);
        }

        return matches.Count switch
        {
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No Project Server connection has the alias '{alias}'. Nothing was changed."),
            1 => matches[0],
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Several Project Server connections have the alias '{alias}'. Nothing was changed."),
        };
    }

    private static ProjectServerGroup FindGroup(ProjectServer server, string? name)
    {
        RequireText(name, "group.name");
        var groups = (server.GetProjectServerGroups() ?? throw Incomplete()).Where(group => group is not null && group.Name == name).ToList();
        return groups.Count switch
        {
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The server lists no group '{name}'. Nothing was changed."),
            1 => groups[0],
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"The server lists several groups '{name}'. Nothing was changed."),
        };
    }

    private static ServerProjectInfo FindProject(ProjectServer server, ProjectServerRequestInfo request)
    {
        RequireText(request.ServerProjectName, "serverProjectName");
        var rows = (request.GroupIsRoot == true ? server.GetServerProjects() : FindGroup(server, request.GroupName).GetServerProjects()) ?? throw Incomplete();
        var matches = rows.Where(row => row is not null && row.ProjectName == request.ServerProjectName).ToList();
        return matches.Count switch
        {
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The server lists no project '{request.ServerProjectName}' there. Nothing was changed."),
            1 => matches[0],
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"The server lists several projects '{request.ServerProjectName}' there. Nothing was changed."),
        };
    }

    private static string Protocol(ProjectServerRequestInfo request)
        => request.Protocol is { } protocol && ProjectServerMethods.Protocols.Contains(protocol, StringComparer.Ordinal)
            ? protocol : throw Validation($"protocol must be one of: {string.Join(", ", ProjectServerMethods.Protocols)}.");

    private static int Port(ProjectServerRequestInfo request)
        => request.Port is { } port && port is > 0 and <= 65535 ? port : throw Validation("port must be between 1 and 65535.");

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Validation($"{name} is required.");
    }

    private static void Close(ProjectServerWriteResultInfo result, PlcDownloadPortalConfirmationLog log)
        => log.CloseInto((entries, observed, complete) =>
        {
            result.PortalConfirmations = entries;
            result.PortalConfirmationsObserved = observed;
            result.PortalConfirmationsComplete = complete;
        });

    private static WorkerOperationException Failure(string category, string message, EngineeringErrorInfo? engineeringError,
        ProjectServerWriteResultInfo evidence, PlcDownloadPortalConfirmationLog log)
    {
        Close(evidence, log);
        return new(category, message, null, engineeringError: engineeringError, plcDownloadOutcome: WorkerJson.SerializePayload(evidence));
    }

    private static WorkerOperationException Validation(string message) => new(WorkerFailureCategories.ValidationError, message);

    private static WorkerOperationException Incomplete()
        => new(WorkerFailureCategories.WorkerOperationFailed, "Project Server returned incomplete or unreadable inventory evidence. Nothing was changed.");
}
