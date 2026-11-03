using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcCpuUserMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcCpuUserNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>Issue #13: web server and OPC UA users of the CPU (see <see cref="PlcCpuUserMethods"/> for the evidence). Passwords only from sources.</summary>
public static partial class PlcSettingsRun
{
    private static IEnumerable<PlcSettingsSectionInfo> UserSections(PlcSettingsContext context)
    {
        yield return Section(context, "webserverUsers", context.CpuOwners(), N.WebserverUsers, Array.Empty<string>(), service => new List<PlcSettingsCollectionInfo>
        {
            Collection(context, "WebserverUsers", () => Get(service, "WebserverUsers"), "UserName", new[] { "UserName", "Permissions" }),
        }, note: "Passwords are never readable. The manual: add, delete and password changes need an activated web server; they take effect after a download.");
        yield return Section(context, "simpleWebserverUsers", context.CpuOwners(), N.SimpleWebserverUsers, Array.Empty<string>(), service => new List<PlcSettingsCollectionInfo>
        {
            Collection(context, "WebserverUsers", () => Get(service, "WebserverUsers"), "UserName", new[] { "UserName", "Active", "Permissions" }),
        }, note: "The simple user management documents no Create or Delete; its users are changed with update_simple_webserver_user.");
        yield return Section(context, "opcUaUsers", context.CpuAndItems(), N.OpcUaUsers, Array.Empty<string>(), service => new List<PlcSettingsCollectionInfo>
        {
            Collection(context, "OpcUaUsers", () => Get(service, "OpcUaUsers"), "UserName", new[] { "UserName" }),
        }, note: "The service of the OPC UA submodule (manual); changes need an activated OPC UA server with user name and password authentication.");
    }

    private static bool WriteUsers(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (PlcSettingsCatalog.Find(method) is not { Issue: 13 }) return false;
        var opcUa = method is M.CreateOpcUaUser or M.SetOpcUaUserPassword or M.DeleteOpcUaUser;
        var (service, owner) = RequireService(context, opcUa ? context.CpuAndItems() : context.CpuOwners(), definition.Service!, CpuWhere(context) + (opcUa ? " and its device items" : string.Empty));
        Describe(context, result, owner, definition.Service);
        var userName = info.UserName ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "userName is required. Nothing was changed.");
        result.Target = $"user '{userName}'";
        var compositionName = opcUa ? "OpcUaUsers" : "WebserverUsers";
        var users = Get(service, compositionName)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, $"{ShortName(definition.Service!)}.{compositionName} returned null. Nothing was changed.");
        var userType = compositionName == "OpcUaUsers" ? "OpcUaUser" : method == M.UpdateSimpleWebserverUser ? "SimpleWebserverUser" : "WebserverUser";
        var findDoc = $"{userType}Composition.Find(String)";
        object? Lookup() => Find(users, typeof(string), userName, findDoc);
        object Existing() => Lookup() ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound,
            $"PLC '{context.PlcName}' has no {userType} '{userName}' at {result.Owner}. Nothing was changed.");
        switch (method)
        {
            case M.CreateWebserverUser:
            {
                var create = RequireMethodByShape(users, "Create", p => p.Length == 3 && p[0].ParameterType == typeof(string) && p[1].ParameterType.IsEnum && IsSecureString(p[2]),
                    "Create(String, WebserverUserPermissions, SecureString)");
                var permissions = PermissionsValue(create.GetParameters()[1].ParameterType, info.Permissions);
                WithPassword(context, info.PasswordVariable, "WebserverUser", result, secure => Call(create, users, userName, permissions, secure));
                result.Calls.Add($"WebserverUserComposition.Create(\"{userName}\", WebserverUserPermissions, SecureString)");
                var created = Lookup();
                Exists(result, "user", true, created is not null, $"Web server user '{userName}'", context.PlcName);
                Compare(context, result, "Permissions", RuntimeServiceValues.Project(permissions).Value, null, ReadValue(context, created!, "Permissions"), userType);
                break;
            }

            case M.UpdateWebserverUser:
            {
                var user = Existing();
                if (info.Permissions is not null)
                {
                    var property = RequireProperty(user, "Permissions", writable: true);
                    var text = RuntimeServiceValues.Project(PermissionsValue(property.PropertyType, info.Permissions)).Value!;
                    SetProperties(context, user, userType, new[] { new PlcSettingValueInfo { Name = "Permissions", Value = text } }, new[] { "Permissions" }, result);
                }

                SetUserPassword(context, user, userType, info, result, required: info.Permissions is null);
                break;
            }

            case M.UpdateSimpleWebserverUser:
            {
                var user = Existing();
                if (info.Settings.Count > 0) SetProperties(context, user, userType, info.Settings, definition.Settings, result);
                SetUserPassword(context, user, userType, info, result, required: info.Settings.Count == 0);
                break;
            }

            case M.SetOpcUaUserPassword:
                SetUserPassword(context, Existing(), userType, info, result, required: true);
                break;

            case M.CreateOpcUaUser:
            {
                var create = RequireMethodByShape(users, "Create", p => p.Length == 2 && p[0].ParameterType == typeof(string) && IsSecureString(p[1]), "Create(String, SecureString)");
                WithPassword(context, info.PasswordVariable, "OpcUaUser", result, secure => Call(create, users, userName, secure));
                result.Calls.Add($"OpcUaUserComposition.Create(\"{userName}\", SecureString)");
                Exists(result, "user", true, Lookup() is not null, $"OPC UA user '{userName}'", context.PlcName);
                break;
            }

            case M.DeleteWebserverUser:
            case M.DeleteOpcUaUser:
                Delete(Existing(), $"{userType}.Delete()", result);
                Exists(result, "user", false, Lookup() is not null, $"{userType} '{userName}'", context.PlcName);
                break;
        }

        warnings.Add("User changes of the CPU take effect after a download (manual: they need an activated web server or OPC UA server).");
        return true;
    }

    private static void SetUserPassword(PlcSettingsContext context, object user, string userType, PlcSettingsRequestInfo info, PlcSettingsResultInfo result, bool required)
    {
        if (info.PasswordVariable is null && !required) return;
        var setPassword = RequireMethodByShape(user, "SetPassword", p => p.Length == 1 && IsSecureString(p[0]), "SetPassword(SecureString)");
        WithPassword(context, info.PasswordVariable, userType, result, secure => Call(setPassword, user, secure));
        result.Calls.Add($"{userType}.SetPassword(SecureString)");
    }

    /// <summary>The permissions enum value: the named members combined (a non-flags enum takes one member).</summary>
    private static object PermissionsValue(Type type, IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                $"permissions is required (members: {Library.LibraryRuntimeBinding.MemberList(type)}). Nothing was changed.");
        return ConvertValue(type, string.Join(", ", names), "permissions");
    }
}
