using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Contracts.Multiuser;

/// <summary>
/// Public operation and worker method names of <c>project_server_write</c> (owner decision 2026-11-09): the Project Server
/// (Multiuser) administration of one TIA Portal instance. It acts on <c>TiaPortal.ProjectServers</c> and on the servers those
/// connections reach, never on a bound project, so it needs no binding (a bound session only pins the TIA Portal it runs in).
/// Every operation has the worker method <c>project_server_&lt;operation&gt;</c> (<see cref="ForOperation"/>); planning and
/// read-back use the existing <c>bind_project</c> inventory methods.
/// </summary>
public static class ProjectServerMethods
{
    public const string WriteTool = "project_server_write";

    public const string Prefix = "project_server_";

    /// <summary><c>ProjectServerComposition.Create(String alias, Protocol, String host, Int32 port)</c>.</summary>
    public const string CreateServerConnection = "create_server_connection";

    /// <summary><c>ProjectServer.SetHostName(String)</c>, <c>.SetPort(Int32)</c>, <c>.SetProtocol(Protocol)</c>.</summary>
    public const string SetServerConnection = "set_server_connection";

    /// <summary><c>ProjectServer.DeleteConnection()</c>.</summary>
    public const string DeleteServerConnection = "delete_server_connection";

    /// <summary><c>ProjectServer.AddProjectToServer(FileInfo)</c> (root) or <c>ProjectServerGroup.AddProjectToServer(FileInfo)</c>.</summary>
    public const string AddProjectToServer = "add_project_to_server";

    /// <summary><c>ProjectServer.CreateLocalSession(ServerProjectInfo, String, DirectoryInfo, SessionCreationMode)</c>.</summary>
    public const string CreateLocalSession = "create_local_session";

    /// <summary><c>ProjectServer.DeleteLocalSessionFromServer(ServerProjectInfo, LocalSessionInfo)</c>.</summary>
    public const string DeleteLocalSessionFromServer = "delete_local_session_from_server";

    public static IReadOnlyList<string> Operations { get; } = Array.AsReadOnly(new[]
    {
        CreateServerConnection, SetServerConnection, DeleteServerConnection, AddProjectToServer, CreateLocalSession, DeleteLocalSessionFromServer
    });

    /// <summary>Every worker method (one per operation).</summary>
    public static IReadOnlyList<string> WorkerMethods { get; } = Array.AsReadOnly(Operations.Select(ForOperation).ToArray());

    /// <summary>The documented protocol member names (<c>Siemens.Engineering.Multiuser.Protocol</c>).</summary>
    public static IReadOnlyList<string> Protocols { get; } = Array.AsReadOnly(new[] { "Https", "Http" });

    /// <summary>The documented session creation modes (<c>Siemens.Engineering.Multiuser.SessionCreationMode</c>).</summary>
    public static IReadOnlyList<string> SessionCreationModes { get; } = Array.AsReadOnly(new[] { "Multiuser", "Exclusive" });

    public const string ProtocolEnum = "Siemens.Engineering.Multiuser.Protocol";
    public const string SessionCreationModeEnum = "Siemens.Engineering.Multiuser.SessionCreationMode";

    public static string ForOperation(string operation) => Prefix + operation;

    public static string? OperationOf(string? method)
        => method is not null && method.StartsWith(Prefix, StringComparison.Ordinal)
            && Operations.Contains(method.Substring(Prefix.Length), StringComparer.Ordinal) ? method.Substring(Prefix.Length) : null;

    public static bool IsWorkerMethod(string? method) => OperationOf(method) is not null;

    /// <summary>The operations whose read-write call is confirmed by an acknowledge guard (delete, upload).</summary>
    public static bool IsAcknowledged(string? operation)
        => operation is DeleteServerConnection or DeleteLocalSessionFromServer or AddProjectToServer;
}

/// <summary>One <c>project_server_write</c> item as the worker receives it; only the fields of its operation are set.</summary>
public sealed class ProjectServerRequestInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary>The connection's alias (<c>ProjectServer.ServerName</c>); create_server_connection: the new alias.</summary>
    public string? ServerAlias { get; set; }

    public string? Host { get; set; }

    public int? Port { get; set; }

    /// <summary>A <see cref="ProjectServerMethods.Protocols"/> member name.</summary>
    public string? Protocol { get; set; }

    /// <summary>Null for operations without a group; true for the server root, false with <see cref="GroupName"/>.</summary>
    public bool? GroupIsRoot { get; set; }

    public string? GroupName { get; set; }

    public string? ServerProjectName { get; set; }

    /// <summary>add_project_to_server: the single-user project file (.ap21) passed as <c>FileInfo</c>.</summary>
    public string? ProjectPath { get; set; }

    public string? SessionName { get; set; }

    public string? SessionDirectory { get; set; }

    /// <summary>A <see cref="ProjectServerMethods.SessionCreationModes"/> member name.</summary>
    public string? SessionMode { get; set; }

    public int? SessionId { get; set; }

    /// <summary><c>decline</c> (default) or <c>accept</c>: how TIA Portal confirmation dialogs raised by the item are answered.</summary>
    public string PortalConfirmations { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;
}

/// <summary>What one <c>project_server_write</c> mutation did, read inside the worker call where a documented read exists.</summary>
public sealed class ProjectServerWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string ServerAlias { get; set; } = string.Empty;

    /// <summary>The documented Siemens calls made, in order (e.g. <c>SetHostName</c>, <c>SetPort</c>).</summary>
    public List<string> Applied { get; set; } = new();

    /// <summary>create/set_server_connection: <c>ProjectServer.Host</c> read after the call (null: not read or unreadable).</summary>
    public string? HostAfter { get; set; }

    /// <summary>create/set_server_connection: <c>ProjectServer.Port</c> read after the call.</summary>
    public int? PortAfter { get; set; }

    /// <summary>create_local_session / delete_local_session_from_server: the session's <c>LocalSessionInfo.SessionId</c>.</summary>
    public int? SessionId { get; set; }

    /// <summary>create_local_session / delete_local_session_from_server: <c>LocalSessionInfo.ProjectFileInfo</c> (the .als21 path).</summary>
    public string? SessionPath { get; set; }

    /// <summary>The policy the item ran under.</summary>
    public string PortalConfirmationPolicy { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new();

    public int PortalConfirmationsObserved { get; set; }

    public bool PortalConfirmationsComplete { get; set; } = true;
}
