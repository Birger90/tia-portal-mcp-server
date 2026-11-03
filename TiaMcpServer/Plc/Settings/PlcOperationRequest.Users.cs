using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The user fields of the settings family (issues #13, #12).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("The user the item addresses. create_webserver_user, update_webserver_user, delete_webserver_user, update_simple_webserver_user (the CPU's web server users), create_opcua_user, set_opcua_user_password, delete_opcua_user (the OPC UA submodule's users): required. UMAC (issue #12): create_project_user, update_project_user, delete_project_user (project users), assign_user_role, unassign_user_role (with userKind), update_umc_user, delete_umc_user (UMC users or groups): required.")]
    public string? UserName { get; set; }

    [Description("create_webserver_user (required) and update_webserver_user: the web server permissions, WebserverUserPermissions member names (e.g. ReadTag, ModifyTag, ReadTagStatus, ModifyTagStatus, ChangeOperatingMode, FlashLed, ReadFiles, ModifyFiles, DoDiagnosis, AcknowledgeMessages, OpenUserDefinedWebPages, WriteUserDefinedWebPages, ManageUserDefinedWebPages, ChangeSystemParameter, ChangeApplicationParameter, Backup, Restore, WriteFirmware, FAdmin); combined into one value, replacing the user's permissions.")]
    public IReadOnlyList<string>? Permissions { get; set; }
}
