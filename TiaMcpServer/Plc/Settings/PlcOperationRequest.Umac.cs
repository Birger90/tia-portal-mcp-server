using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The UMAC fields of the settings family (issue #12); userName, name, newName, settings and passwordVariable are shared. roleName is also the OPC UA role name (issue #24).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("UMAC (issue #12): the role. assign_engineering_right, unassign_engineering_right, assign_device_function_right, unassign_device_function_right (a custom role, CustomRoles.Find), assign_user_role, unassign_user_role (a custom or system role): required. OPC UA role operations (issue #24): the role name (RoleMapping.Name). create_opcua_role: RoleMappingComposition.Create(name, definingNamespace); add_opcua_standard_role: AddStandardOpcUaRole(name).")]
    public string? RoleName { get; set; }

    [Description("UMAC: the function right. assign_engineering_right/unassign_engineering_right: an engineering function right of the project by name (EngineeringFunctionRights.Find). assign_device_function_right/unassign_device_function_right: a device function right of the PLC (UmacDevice.AvailableDeviceFunctionRights) by name, else by its Openness identifier. Required.")]
    public string? RightName { get; set; }

    [Description("UMAC: whose roles change or which UMC object is meant. assign_user_role/unassign_user_role: projectUser (default), umcUser or umcUserGroup. update_umc_user/delete_umc_user: umcUser (default) or umcUserGroup.")]
    public string? UserKind { get; set; }

    [Description("update_project_user: the new alias name (ProjectUser.SetAliasName).")]
    public string? AliasName { get; set; }

    [Description("UMAC: update_project_user and update_umc_user: true calls Activate(), false Deactivate(); set_anonymous_user (required): true ActivateAnonymousUser(), false DeactivateAnonymousUser().")]
    public bool? Active { get; set; }

    [Description("update_project_user: true ActivateRuntimeSessionTimeout(), false DeactivateRuntimeSessionTimeout().")]
    public bool? RuntimeSessionTimeout { get; set; }

    [Description("create_custom_device_function_right (required): the group of the custom device function right (CustomDeviceFunctionRightComposition.Create(name, group, comment)).")]
    public string? Group { get; set; }
}
