using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.ProjectServers;

/// <summary>One project_server_write item. Only the fields an operation documents are accepted (the domain refuses the others).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectServerOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;

    [Description("The Project Server connection's exact alias as bind_project list_server_connections reports it; create_server_connection: the new alias.")]
    public string? ServerAlias { get; set; }

    [Description("create_server_connection (required), set_server_connection: the server host name.")]
    public string? Host { get; set; }

    [Description("create_server_connection (required), set_server_connection: the server port, 1-65535.")]
    public int? Port { get; set; }

    [Description("create_server_connection (required), set_server_connection: Https or Http (Siemens.Engineering.Multiuser.Protocol).")]
    public string? Protocol { get; set; }

    [Description("add_project_to_server, create_local_session, delete_local_session_from_server: {isRoot:true,name:null} for the server root or {isRoot:false,name} for a group, as bind_project uses it.")]
    public MultiuserGroupSelector? Group { get; set; }

    [Description("create_local_session, delete_local_session_from_server: the server project's exact name (bind_project list_server_projects).")]
    public string? ServerProjectName { get; set; }

    [Description("add_project_to_server: absolute path of the single-user project file (.ap21) that is uploaded.")]
    public string? ProjectPath { get; set; }

    [Description("create_local_session: the new local session's name.")]
    public string? SessionName { get; set; }

    [Description("create_local_session: absolute directory the local session is created in.")]
    public string? SessionDirectory { get; set; }

    [Description("create_local_session: Multiuser (default) or Exclusive (Siemens.Engineering.Multiuser.SessionCreationMode).")]
    public string? SessionMode { get; set; }

    [Description("delete_local_session_from_server: the session id as bind_project list_local_sessions reports it.")]
    public int? SessionId { get; set; }

    [Description("How TIA Portal confirmation dialogs raised while the item runs are answered: decline (default: No, recorded, the item fails) or accept (Yes, recorded, a warning).")]
    public string? PortalConfirmations { get; set; }
}
