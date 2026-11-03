namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #13, web server and OPC UA user management of the CPU. Evidence: Siemens.Engineering.Step7.xml (V21):
/// <c>HW.Features.WebserverUserManagement.WebserverUsers</c>, <c>HW.WebserverUserComposition.Create(String, WebserverUserPermissions,
/// SecureString)</c>, <c>.Find(String)</c>, <c>HW.WebserverUser.UserName</c>, <c>.Permissions</c>, <c>.SetPassword(SecureString)</c>,
/// <c>.Delete()</c>; <c>HW.Features.SimpleWebserverUserManagement.WebserverUsers</c>, <c>HW.SimpleWebserverUserComposition.Find(String)</c>,
/// <c>HW.SimpleWebserverUser.UserName</c>, <c>.Active</c>, <c>.Permissions</c>, <c>.SetPassword(SecureString)</c>;
/// <c>HW.Features.OpcUaUserManagement.OpcUaUsers</c>, <c>HW.OpcUaUserComposition.Create(String, SecureString)</c>, <c>.Find(String)</c>,
/// <c>HW.OpcUaUser.UserName</c>, <c>.SetPassword(SecureString)</c>, <c>.Delete()</c>; and the V21 manual section "Accessing web server and
/// OPC UA user management" (web server: the CPU's service, only while the web server is activated; OPC UA: the service of the OPC UA
/// submodule, only while the server is activated with user name and password authentication).
/// </summary>
public static class PlcCpuUserMethods
{
    public const string CreateWebserverUser = "create_webserver_user";
    public const string UpdateWebserverUser = "update_webserver_user";
    public const string DeleteWebserverUser = "delete_webserver_user";
    public const string UpdateSimpleWebserverUser = "update_simple_webserver_user";
    public const string CreateOpcUaUser = "create_opcua_user";
    public const string SetOpcUaUserPassword = "set_opcua_user_password";
    public const string DeleteOpcUaUser = "delete_opcua_user";
}

public static class PlcCpuUserNames
{
    public const string WebserverUsers = "Siemens.Engineering.HW.Features.WebserverUserManagement";
    public const string SimpleWebserverUsers = "Siemens.Engineering.HW.Features.SimpleWebserverUserManagement";
    public const string OpcUaUsers = "Siemens.Engineering.HW.Features.OpcUaUserManagement";

    public static readonly string[] SimpleUserProperties = { "Active", "Permissions" };

    /// <summary>The most permissions one item names.</summary>
    public const int MaxPermissions = 30;
}

public static partial class PlcSettingsCatalog
{
    private static void AddUsers()
    {
        const int Issue = 13;
        Add(new(PlcCpuUserMethods.CreateWebserverUser, true, Issue, false, PlcCpuUserNames.WebserverUsers));
        Add(new(PlcCpuUserMethods.UpdateWebserverUser, true, Issue, false, PlcCpuUserNames.WebserverUsers));
        Add(new(PlcCpuUserMethods.DeleteWebserverUser, true, Issue, false, PlcCpuUserNames.WebserverUsers));
        Add(new(PlcCpuUserMethods.UpdateSimpleWebserverUser, true, Issue, false, PlcCpuUserNames.SimpleWebserverUsers, PlcCpuUserNames.SimpleUserProperties,
            // Permissions: every value but the most restrictive (None) or empty lowers, combined flags included; judged by the host (security review F8).
            new[] { ("Active", "true") }));
        Add(new(PlcCpuUserMethods.CreateOpcUaUser, true, Issue, false, PlcCpuUserNames.OpcUaUsers));
        Add(new(PlcCpuUserMethods.SetOpcUaUserPassword, true, Issue, false, PlcCpuUserNames.OpcUaUsers, raises: true));
        Add(new(PlcCpuUserMethods.DeleteOpcUaUser, true, Issue, false, PlcCpuUserNames.OpcUaUsers));
    }
}
