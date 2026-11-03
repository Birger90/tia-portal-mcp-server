using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The fields of the OPC UA operations (issue #24); plcName, newName, filePath and overwrite are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("OPC UA interface operations: server (ServerInterfaces, e.g. a companion specification or own server interface), simatic (SimaticInterfaces) or referenceNamespace (ReferenceNamespaces). Required.")]
    public string? InterfaceKind { get; set; }

    [Description("OPC UA interface operations: the interface name (exact, case-sensitive). Required.")]
    public string? InterfaceName { get; set; }

    [Description("set_opcua_interface: Enabled ('Enable ... and download to PLC'); set_opcua_namespace_permission: the permission value passed to SetPermission(String, Boolean). Generic attribute API with a GetAttributeInfos writability check.")]
    public bool? Enabled { get; set; }

    [Description("set_opcua_interface (simatic, referenceNamespace): UseStringNodeIds.")]
    public bool? UseStringNodeIds { get; set; }

    [Description("set_opcua_interface (referenceNamespace): GenerateNodes ('Generate nodes based on local data mapping').")]
    public bool? GenerateNodes { get; set; }

    [Description("set_opcua_namespace_restriction, set_opcua_namespace_permission: the namespace by its NamespaceUri (exact).")]
    public string? NamespaceUri { get; set; }

    [Description("set_opcua_namespace_restriction: SessionRequired.")]
    public bool? SessionRequired { get; set; }

    [Description("set_opcua_namespace_restriction: SigningRequired.")]
    public bool? SigningRequired { get; set; }

    [Description("set_opcua_namespace_restriction: EncryptionRequired.")]
    public bool? EncryptionRequired { get; set; }

    [Description("set_opcua_namespace_restriction: ApplyRestrictionsToBrowse.")]
    public bool? ApplyRestrictionsToBrowse { get; set; }

    // roleName is shared with the UMAC operations (PlcOperationRequest.Umac.cs, issue #12); its description covers both.

    [Description("set_opcua_role: the new role name (RoleMapping.SetRoleName).")]
    public string? NewRoleName { get; set; }

    [Description("create_opcua_role (required), set_opcua_role: the defining namespace of the role (SetDefiningNamespace).")]
    public string? DefiningNamespace { get; set; }

    [Description("set_opcua_role: the mapped project role (RoleMapping.SetProjectRole), e.g. a user management role of the project.")]
    public string? ProjectRole { get; set; }

    [Description("compare_software with compareTarget secondary_plc (issue #20): the path of an open secondary project (library_read list_secondary_projects) whose PLC targetName is compared with.")]
    public string? TargetProject { get; set; }

    [Description("set_opcua_namespace_permission: the permission name passed to NamespacePermission.SetPermission(String, Boolean); the documented boolean properties are Browse, Read, Write, Call, ReceiveEvents, ReadRolePermissions (read back when the name is one of them).")]
    public string? Permission { get; set; }
}
