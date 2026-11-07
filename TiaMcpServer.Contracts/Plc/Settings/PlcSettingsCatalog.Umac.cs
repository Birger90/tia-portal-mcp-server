namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #12, the project's user management and access control (UMAC). Evidence: Siemens.Engineering.Base.xml (V21), namespace
/// <c>Siemens.Engineering.Umac</c>: <c>UmacConfigurator</c> (<c>CustomRoles</c>, <c>SystemRoles</c>, <c>ProjectUsers</c>, <c>AnonymousUser</c>,
/// <c>ActivateAnonymousUser()</c>, <c>DeactivateAnonymousUser()</c>, <c>EngineeringFunctionRights</c>, <c>CustomDeviceFunctionRights</c>,
/// <c>UmcUsers</c>, <c>UmcUserGroups</c>), <c>CustomRoleComposition.Create(String, String)</c>/<c>.Find(String)</c>, <c>CustomRole</c>
/// (<c>AssignDeviceFunctionRight(UmacDevice, DeviceFunctionRight)</c>, <c>UnAssignDeviceFunctionRight</c>, <c>GetAssignedDeviceFunctionRights(UmacDevice)</c>,
/// <c>AssignedEngineeringRights</c>, <c>Comment</c>, <c>SessionTimeOut</c>, <c>SetName(String)</c>, <c>Delete()</c>), <c>SystemRole</c>
/// (<c>AssignedEngineeringRights</c>, <c>GetAssignedSystemDeviceFunctionRights(UmacDevice)</c>), <c>ProjectUserComposition.Create(String, SecureString)</c>,
/// <c>ProjectUser</c> (<c>SetPassword</c>, <c>SetName</c>, <c>SetAliasName</c>, <c>Activate()</c>, <c>Deactivate()</c>,
/// <c>Activate/DeactivateRuntimeSessionTimeout()</c>, <c>Comment</c>, <c>SessionTimeOut</c>, <c>Delete()</c>), <c>User.Roles</c>
/// (<c>RoleAssociation.Add/Remove(Role)</c>), <c>EngineeringFunctionRightAssociation.Add/Remove</c>, <c>CustomDeviceFunctionRightComposition.Create(String, String, String)</c>,
/// <c>UmcUser</c>/<c>UmcUserGroup</c> (<c>Activate()</c>, <c>Deactivate()</c>, <c>SetName</c>, <c>Delete()</c>), <c>UmacDevice.AvailableDeviceFunctionRights</c>,
/// <c>PasswordPolicyConfigurator</c>; the Siemens security skill (<c>Project.GetService&lt;UmacConfigurator&gt;()</c>,
/// <c>device.GetService&lt;UmacDevice&gt;()</c>, "EngineeringFunctionRights not empty: the project is protected") and the manual
/// "Modifying project in UMAC" (the function right "Modify project via Openness API" decides whether Openness may change a protected project).
/// </summary>
public static class PlcUmacMethods
{
    public const string GetUmac = "get_umac";
    public const string CreateCustomRole = "create_custom_role";
    public const string UpdateCustomRole = "update_custom_role";
    public const string DeleteCustomRole = "delete_custom_role";
    public const string AssignEngineeringRight = "assign_engineering_right";
    public const string UnassignEngineeringRight = "unassign_engineering_right";
    public const string AssignDeviceFunctionRight = "assign_device_function_right";
    public const string UnassignDeviceFunctionRight = "unassign_device_function_right";
    public const string CreateProjectUser = "create_project_user";
    public const string UpdateProjectUser = "update_project_user";
    public const string DeleteProjectUser = "delete_project_user";
    public const string AssignUserRole = "assign_user_role";
    public const string UnassignUserRole = "unassign_user_role";
    public const string SetAnonymousUser = "set_anonymous_user";
    public const string CreateCustomDeviceFunctionRight = "create_custom_device_function_right";
    public const string UpdateCustomDeviceFunctionRight = "update_custom_device_function_right";
    public const string DeleteCustomDeviceFunctionRight = "delete_custom_device_function_right";
    public const string UpdateUmcUser = "update_umc_user";
    public const string DeleteUmcUser = "delete_umc_user";
    public const string SetUmacPasswordPolicy = "set_umac_password_policy";

    // Package G (V21 API completion; Siemens.Engineering.Base.xml, namespace Siemens.Engineering.Umac and ProjectBase):
    /// <summary>ProjectBase.ProtectProject(String, SecureString): protects the project with an administrator (acknowledge guard).</summary>
    public const string ProtectProject = "protect_project";
    /// <summary>UmcUserComposition.CreateOfflineUmcUser(String).</summary>
    public const string CreateOfflineUmcUser = "create_offline_umc_user";
    /// <summary>UmcUserGroupComposition.CreateOfflineUmcUserGroup(), then UmcUserGroup.SetName(String) when a name is given.</summary>
    public const string CreateOfflineUmcUserGroup = "create_offline_umc_user_group";
    /// <summary>UmcServer.GetUserByName/GetUserGroupByName, then UmcUserComposition.Create(UmcUserInfo) / UmcUserGroupComposition.Create(UmcUserGroupInfo).</summary>
    public const string ImportUmcUser = "import_umc_user";
    /// <summary>plc_read: UmcServer.GetUserByName(String) / GetUserGroupByName(String) (contacts the UMC server).</summary>
    public const string GetUmcServerUser = "get_umc_server_user";
    /// <summary>plc_read: UmcServerConfigurator.CheckConsistency() (contacts the UMC server).</summary>
    public const string CheckUmcConsistency = "check_umc_consistency";

    /// <summary>The reads that contact the UMC server (OnlineObserve: they reach a server, change nothing).</summary>
    public static readonly string[] UmcServerReads = { GetUmcServerUser, CheckUmcConsistency };
}

public static class PlcUmacNames
{
    public const string Configurator = "Siemens.Engineering.Umac.UmacConfigurator";
    public const string Device = "Siemens.Engineering.Umac.UmacDevice";
    public const string PasswordPolicy = "Siemens.Engineering.Umac.PasswordPolicyConfigurator";
    /// <summary>Found among the project's (then the UmacConfigurator's) GetServiceInfos(); how it is obtained is not documented.</summary>
    public const string UmcServerConfigurator = "Siemens.Engineering.Umac.UmcServerConfigurator";

    public static readonly string[] RoleProperties = { "Comment", "SessionTimeOut" };

    public static readonly string[] ProjectUserProperties = { "Comment", "SessionTimeOut" };

    public static readonly string[] DeviceRightProperties = { "Comment" };

    public static readonly string[] PasswordPolicyProperties =
    {
        "MinimumLength", "MinimumNumericCharacterLength", "MinimumSpecialCharacterLength", "IncludesLowerCaseAndUpperCaseCharacters",
        "EnablePasswordAging", "PasswordValidity", "PasswordValidityPrewarningTime", "MinimumUserPasswordsBlockedForReuse",
    };

    /// <summary>The user kinds whose <c>Roles</c> an item changes.</summary>
    public const string ProjectUser = "projectUser";
    public const string UmcUser = "umcUser";
    public const string UmcUserGroup = "umcUserGroup";

    public static readonly string[] UserKinds = { ProjectUser, UmcUser, UmcUserGroup };
    public static readonly string[] UmcKinds = { UmcUser, UmcUserGroup };
}

public static partial class PlcSettingsCatalog
{
    private static void AddUmac()
    {
        const int Issue = 12;
        // get_umac reads the project's UMAC and, per PLC, the UmacDevice rights; it is not project-scoped so the PLCs are listed.
        Add(new(PlcUmacMethods.GetUmac, false, Issue, false, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.CreateCustomRole, true, Issue, true, PlcUmacNames.Configurator, PlcUmacNames.RoleProperties));
        Add(new(PlcUmacMethods.UpdateCustomRole, true, Issue, true, PlcUmacNames.Configurator, PlcUmacNames.RoleProperties));
        Add(new(PlcUmacMethods.DeleteCustomRole, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.AssignEngineeringRight, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.UnassignEngineeringRight, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.AssignDeviceFunctionRight, true, Issue, false, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.UnassignDeviceFunctionRight, true, Issue, false, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.CreateProjectUser, true, Issue, true, PlcUmacNames.Configurator, PlcUmacNames.ProjectUserProperties));
        Add(new(PlcUmacMethods.UpdateProjectUser, true, Issue, true, PlcUmacNames.Configurator, PlcUmacNames.ProjectUserProperties));
        Add(new(PlcUmacMethods.DeleteProjectUser, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.AssignUserRole, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.UnassignUserRole, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.SetAnonymousUser, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.CreateCustomDeviceFunctionRight, true, Issue, true, PlcUmacNames.Configurator, PlcUmacNames.DeviceRightProperties));
        Add(new(PlcUmacMethods.UpdateCustomDeviceFunctionRight, true, Issue, true, PlcUmacNames.Configurator, PlcUmacNames.DeviceRightProperties));
        Add(new(PlcUmacMethods.DeleteCustomDeviceFunctionRight, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.UpdateUmcUser, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.DeleteUmcUser, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.SetUmacPasswordPolicy, true, Issue, true, PlcUmacNames.PasswordPolicy, PlcUmacNames.PasswordPolicyProperties,
            new[] { ("IncludesLowerCaseAndUpperCaseCharacters", "false"), ("EnablePasswordAging", "false") }, requiresSettings: true));
        // Package G.
        Add(new(PlcUmacMethods.ProtectProject, true, Issue, true, PlcUmacNames.Configurator, raises: true));
        Add(new(PlcUmacMethods.CreateOfflineUmcUser, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.CreateOfflineUmcUserGroup, true, Issue, true, PlcUmacNames.Configurator));
        Add(new(PlcUmacMethods.ImportUmcUser, true, Issue, true, PlcUmacNames.UmcServerConfigurator));
        Add(new(PlcUmacMethods.GetUmcServerUser, false, Issue, true, PlcUmacNames.UmcServerConfigurator));
        Add(new(PlcUmacMethods.CheckUmcConsistency, false, Issue, true, PlcUmacNames.UmcServerConfigurator));
    }
}
