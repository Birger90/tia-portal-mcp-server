using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcUmacMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcUmacNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>Issue #12: UMAC of the project (see <see cref="PlcUmacMethods"/> for the evidence). Passwords only from sources.</summary>
public static partial class PlcSettingsRun
{
    private static (object Configurator, object Owner) Configurator(PlcSettingsContext context)
        => RequireService(context, new[] { RequireProject(context) }, N.Configurator, "The project");

    /// <summary>The UMAC device of a PLC: the CPU device item first, then the device (the skill calls <c>device.GetService&lt;UmacDevice&gt;()</c>).</summary>
    private static IEnumerable<object> UmacDeviceOwners(PlcSettingsContext context)
    {
        if (context.Cpu is not null) yield return context.Cpu;
        if (context.Device is not null) yield return context.Device;
    }

    private static PlcSettingsReadInfo ReadUmac(IReadOnlyList<PlcSettingsContext> plcs, PlcSettingsContext? project, PlcSettingsReadInfo result)
    {
        if (project is null) return result;
        var owners = project.Project is null ? Array.Empty<object>() : new[] { project.Project };
        object? configurator = null;
        result.Project.Add(Section(project, "umac", owners, N.Configurator, Array.Empty<string>(), service =>
        {
            configurator = service;
            return new List<PlcSettingsCollectionInfo>
            {
                Collection(project, "EngineeringFunctionRights", () => Get(service, "EngineeringFunctionRights"), "Name", new[] { "Name", "Identifier" }),
                Collection(project, "CustomRoles", () => Get(service, "CustomRoles"), "Name", new[] { "Name", "Identifier", "Comment", "SessionTimeOut", "AssignedEngineeringRights" },
                    special: (role, property) => property == "AssignedEngineeringRights" ? Names(project, role, property) : null),
                Collection(project, "SystemRoles", () => Get(service, "SystemRoles"), "Name", new[] { "Name", "Identifier", "AssignedEngineeringRights" },
                    special: (role, property) => property == "AssignedEngineeringRights" ? Names(project, role, property) : null),
                Collection(project, "ProjectUsers", () => Get(service, "ProjectUsers"), "Name",
                    new[] { "Name", "AliasName", "Comment", "IsActive", "IsRuntimeSessionTimeoutActive", "SessionTimeOut", "ProjectUserAuthenticationType", "Roles" },
                    special: (user, property) => property == "Roles" ? Names(project, user, property) : null),
                Collection(project, "UmcUsers", () => Get(service, "UmcUsers"), "Name", new[] { "Name", "DomainId", "IsActive", "Roles" },
                    special: (user, property) => property == "Roles" ? Names(project, user, property) : null),
                Collection(project, "UmcUserGroups", () => Get(service, "UmcUserGroups"), "Name", new[] { "Name", "Description", "DomainId", "IsActive", "Roles" },
                    special: (group, property) => property == "Roles" ? Names(project, group, property) : null),
                Collection(project, "CustomDeviceFunctionRights", () => Get(service, "CustomDeviceFunctionRights"), "Name", new[] { "Name", "Group", "Identifier", "Comment" }),
                Collection(project, "AnonymousUser", () => Get(service, "AnonymousUser") is { } anonymous ? new[] { anonymous } : Array.Empty<object>(), "Name",
                    new[] { "Name", "IsActive", "Roles" }, special: (user, property) => property == "Roles" ? Names(project, user, property) : null),
            };
        }, note: "The project is protected when EngineeringFunctionRights is not empty (Siemens security skill). In a protected project Openness may change data only with the function right \"Modify project via Openness API\" (manual \"Modifying project in UMAC\"). Passwords are never readable."));
        result.Project.Add(Section(project, "umacPasswordPolicy", owners, N.PasswordPolicy, N.PasswordPolicyProperties));
        foreach (var plc in plcs)
        {
            var entry = new PlcSettingsPlcInfo { PlcName = plc.PlcName ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            entry.Sections.Add(Section(plc, "umacDevice", UmacDeviceOwners(plc), N.Device, Array.Empty<string>(), device =>
            {
                var collections = new List<PlcSettingsCollectionInfo>
                {
                    Collection(plc, "AvailableDeviceFunctionRights", () => Get(device, "AvailableDeviceFunctionRights"), "Name", new[] { "Name", "Group", "Identifier" }),
                };
                if (configurator is not null)
                {
                    collections.Add(AssignedRights(plc, configurator, device, "CustomRoles", "GetAssignedDeviceFunctionRights"));
                    collections.Add(AssignedRights(plc, configurator, device, "SystemRoles", "GetAssignedSystemDeviceFunctionRights"));
                }

                return collections;
            }));
            result.Plcs.Add(entry);
        }

        return result;
    }

    /// <summary>Per role, the device function rights it holds on the device (<c>GetAssigned...DeviceFunctionRights(UmacDevice)</c>).</summary>
    private static PlcSettingsCollectionInfo AssignedRights(PlcSettingsContext context, object configurator, object device, string roles, string method)
        => Collection(context, $"{roles}Rights", () => Get(configurator, roles), "Name", new[] { "Name", "AssignedRights" },
            special: (role, property) =>
            {
                if (property != "AssignedRights") return null;
                try
                {
                    var bound = RequireMethodByShape(role, method, p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(device), $"{method}(UmacDevice)");
                    var names = Enumerate(Call(bound, role, device)).Select(r => context.NameOf(r) ?? r?.ToString() ?? "(null)");
                    return RuntimeServiceValues.Project(string.Join(", ", names));
                }
                catch (RuntimeBindingException exception)
                {
                    return new RuntimeValueInfo { State = RuntimeValueStates.Missing, Error = exception.Message };
                }
                catch (Exception exception) when (context.Recoverable(exception))
                {
                    return new RuntimeValueInfo { State = RuntimeValueStates.Unreadable, Error = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}") };
                }
            });

    private static bool WriteUmac(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (PlcSettingsCatalog.Find(method) is not { Issue: 12 }) return false;
        if (method == M.SetUmacPasswordPolicy)
        {
            var (policy, policyOwner) = RequireService(context, new[] { RequireProject(context) }, N.PasswordPolicy, "The project");
            Describe(context, result, policyOwner, N.PasswordPolicy);
            SetProperties(context, policy, "PasswordPolicyConfigurator", info.Settings, definition.Settings, result);
            return true;
        }

        var (configurator, owner) = Configurator(context);
        Describe(context, result, owner, N.Configurator);
        string Required(string? value, string field) => value ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"{field} is required. Nothing was changed.");
        object Composition(string name) => Get(configurator, name)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, $"UmacConfigurator.{name} returned null. Nothing was changed.");
        object? FindIn(string composition, string name) => Find(Composition(composition), typeof(string), name, $"{composition}.Find(String)");
        object RequireIn(string composition, string name, string what) => FindIn(composition, name)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The project has no {what} '{name}'. Nothing was changed.");
        switch (method)
        {
            case M.CreateCustomRole:
            {
                var name = Required(info.Name, "name");
                result.Target = $"custom role '{name}'";
                var comment = info.Settings.FirstOrDefault(s => s.Name == "Comment")?.Value ?? string.Empty;
                var create = RequireMethod(Composition("CustomRoles"), "Create", new[] { typeof(string), typeof(string) }, "CustomRoleComposition.Create(String, String)");
                var role = Call(create, Composition("CustomRoles"), name, comment);
                result.Calls.Add($"CustomRoleComposition.Create(\"{name}\", comment)");
                Exists(result, "role", true, FindIn("CustomRoles", name) is not null, $"Custom role '{name}'", null);
                var rest = info.Settings.Where(s => s.Name != "Comment").ToList();
                if (rest.Count > 0) SetProperties(context, role ?? RequireIn("CustomRoles", name, "custom role"), "CustomRole", rest, definition.Settings, result);
                return true;
            }

            case M.UpdateCustomRole:
            case M.UpdateCustomDeviceFunctionRight:
            {
                var role = method == M.UpdateCustomRole;
                var composition = role ? "CustomRoles" : "CustomDeviceFunctionRights";
                var what = role ? "custom role" : "custom device function right";
                var type = role ? "CustomRole" : "CustomDeviceFunctionRight";
                var name = Required(info.Name, "name");
                result.Target = $"{what} '{name}'";
                var target = RequireIn(composition, name, what);
                if (info.Settings.Count > 0) SetProperties(context, target, type, info.Settings, definition.Settings, result);
                if (info.NewName is not null)
                {
                    var setName = RequireMethod(target, "SetName", new[] { typeof(string) }, $"{type}.SetName(String)");
                    Call(setName, target, info.NewName);
                    result.Calls.Add($"{type}.SetName(\"{info.NewName}\")");
                    Exists(result, "newName", true, FindIn(composition, info.NewName) is not null, $"{what} '{info.NewName}'", null);
                }

                return true;
            }

            case M.DeleteCustomRole:
            case M.DeleteCustomDeviceFunctionRight:
            {
                var role = method == M.DeleteCustomRole;
                var composition = role ? "CustomRoles" : "CustomDeviceFunctionRights";
                var what = role ? "custom role" : "custom device function right";
                var name = Required(info.Name, "name");
                result.Target = $"{what} '{name}'";
                Delete(RequireIn(composition, name, what), $"{(role ? "CustomRole" : "CustomDeviceFunctionRight")}.Delete()", result);
                Exists(result, "object", false, FindIn(composition, name) is not null, $"{what} '{name}'", null);
                return true;
            }

            case M.CreateCustomDeviceFunctionRight:
            {
                var name = Required(info.Name, "name");
                result.Target = $"custom device function right '{name}'";
                var comment = info.Settings.FirstOrDefault(s => s.Name == "Comment")?.Value ?? string.Empty;
                var rights = Composition("CustomDeviceFunctionRights");
                var create = RequireMethod(rights, "Create", new[] { typeof(string), typeof(string), typeof(string) }, "CustomDeviceFunctionRightComposition.Create(String, String, String)");
                Call(create, rights, name, Required(info.Group, "group"), comment);
                result.Calls.Add($"CustomDeviceFunctionRightComposition.Create(\"{name}\", \"{info.Group}\", comment)");
                Exists(result, "right", true, FindIn("CustomDeviceFunctionRights", name) is not null, $"Custom device function right '{name}'", null);
                return true;
            }

            case M.AssignEngineeringRight:
            case M.UnassignEngineeringRight:
            {
                var roleName = Required(info.RoleName, "roleName");
                var rightName = Required(info.RightName, "rightName");
                result.Target = $"custom role '{roleName}'";
                var role = RequireIn("CustomRoles", roleName, "custom role");
                var right = RequireIn("EngineeringFunctionRights", rightName, "engineering function right");
                var assigned = Get(role, "AssignedEngineeringRights")
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "CustomRole.AssignedEngineeringRights returned null. Nothing was changed.");
                var add = method == M.AssignEngineeringRight;
                var member = RequireMethodByShape(assigned, add ? "Add" : "Remove", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(right),
                    $"EngineeringFunctionRightAssociation.{(add ? "Add" : "Remove")}(EngineeringFunctionRight)");
                Call(member, assigned, right);
                result.Calls.Add($"CustomRole.AssignedEngineeringRights.{(add ? "Add" : "Remove")}('{rightName}')");
                Exists(result, rightName, add, Enumerate(Get(role, "AssignedEngineeringRights")).Any(r => Equals(r, right)), $"Engineering function right '{rightName}' of role '{roleName}'", null);
                return true;
            }

            case M.AssignDeviceFunctionRight:
            case M.UnassignDeviceFunctionRight:
            {
                var roleName = Required(info.RoleName, "roleName");
                var rightName = Required(info.RightName, "rightName");
                var (device, _) = RequireService(context, UmacDeviceOwners(context), N.Device, $"Neither the CPU of PLC '{context.PlcName}' nor its device");
                result.Target = $"custom role '{roleName}'";
                var role = RequireIn("CustomRoles", roleName, "custom role");
                var available = Elements(Get(device, "AvailableDeviceFunctionRights"), "UmacDevice.AvailableDeviceFunctionRights").Where(r => r is not null).ToList();
                var matches = available.Where(r => context.NameOf(r) == rightName).ToList();
                if (matches.Count == 0) matches = available.Where(r => ReadValue(context, r!, "Identifier").Value == rightName).ToList();
                if (matches.Count != 1)
                    throw new RuntimeBindingException(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                        $"PLC '{context.PlcName}' offers {(matches.Count == 0 ? "no" : "several")} device function right(s) named or identified '{rightName}' (UmacDevice.AvailableDeviceFunctionRights). Nothing was changed.");
                var right = matches[0]!;
                var add = method == M.AssignDeviceFunctionRight;
                var name = add ? "AssignDeviceFunctionRight" : "UnAssignDeviceFunctionRight";
                var member = RequireMethodByShape(role, name, p => p.Length == 2 && p[0].ParameterType.IsInstanceOfType(device) && p[1].ParameterType.IsInstanceOfType(right),
                    $"CustomRole.{name}(UmacDevice, DeviceFunctionRight)");
                Call(member, role, device, right);
                result.Calls.Add($"CustomRole.{name}(UmacDevice of PLC '{context.PlcName}', '{rightName}')");
                var getAssigned = RequireMethodByShape(role, "GetAssignedDeviceFunctionRights", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(device), "GetAssignedDeviceFunctionRights(UmacDevice)");
                Exists(result, rightName, add, Enumerate(Call(getAssigned, role, device)).Any(r => Equals(r, right)), $"Device function right '{rightName}' of role '{roleName}'", context.PlcName);
                return true;
            }

            case M.CreateProjectUser:
            {
                var userName = Required(info.UserName, "userName");
                result.Target = $"project user '{userName}'";
                var users = Composition("ProjectUsers");
                var create = RequireMethodByShape(users, "Create", p => p.Length == 2 && p[0].ParameterType == typeof(string) && IsSecureString(p[1]), "ProjectUserComposition.Create(String, SecureString)");
                object? created = null;
                WithPassword(context, info.PasswordVariable, "ProjectUser", result, secure => created = Call(create, users, userName, secure));
                result.Calls.Add($"ProjectUserComposition.Create(\"{userName}\", SecureString)");
                Exists(result, "user", true, FindIn("ProjectUsers", userName) is not null, $"Project user '{userName}'", null);
                if (info.Settings.Count > 0) SetProperties(context, created ?? RequireIn("ProjectUsers", userName, "project user"), "ProjectUser", info.Settings, definition.Settings, result);
                return true;
            }

            case M.UpdateProjectUser:
            {
                var userName = Required(info.UserName, "userName");
                result.Target = $"project user '{userName}'";
                var user = RequireIn("ProjectUsers", userName, "project user");
                if (info.Settings.Count > 0) SetProperties(context, user, "ProjectUser", info.Settings, definition.Settings, result);
                if (info.AliasName is not null) CallAndCompare(context, user, "SetAliasName", info.AliasName, "AliasName", "ProjectUser", result);
                if (info.Active is { } active) Toggle(context, user, active ? "Activate" : "Deactivate", "IsActive", active, "ProjectUser", result);
                if (info.RuntimeSessionTimeout is { } timeout)
                    Toggle(context, user, timeout ? "ActivateRuntimeSessionTimeout" : "DeactivateRuntimeSessionTimeout", "IsRuntimeSessionTimeoutActive", timeout, "ProjectUser", result);
                if (info.PasswordVariable is not null)
                {
                    var setPassword = RequireMethodByShape(user, "SetPassword", p => p.Length == 1 && IsSecureString(p[0]), "ProjectUser.SetPassword(SecureString)");
                    WithPassword(context, info.PasswordVariable, "ProjectUser", result, secure => Call(setPassword, user, secure));
                    result.Calls.Add("ProjectUser.SetPassword(SecureString)");
                }

                if (info.NewName is not null)
                {
                    var setName = RequireMethod(user, "SetName", new[] { typeof(string) }, "ProjectUser.SetName(String)");
                    Call(setName, user, info.NewName);
                    result.Calls.Add($"ProjectUser.SetName(\"{info.NewName}\")");
                    Exists(result, "newName", true, FindIn("ProjectUsers", info.NewName) is not null, $"Project user '{info.NewName}'", null);
                }

                return true;
            }

            case M.DeleteProjectUser:
            {
                var userName = Required(info.UserName, "userName");
                result.Target = $"project user '{userName}'";
                Delete(RequireIn("ProjectUsers", userName, "project user"), "ProjectUser.Delete()", result);
                Exists(result, "user", false, FindIn("ProjectUsers", userName) is not null, $"Project user '{userName}'", null);
                return true;
            }

            case M.AssignUserRole:
            case M.UnassignUserRole:
            {
                var userName = Required(info.UserName, "userName");
                var roleName = Required(info.RoleName, "roleName");
                var kind = info.UserKind ?? N.ProjectUser;
                var (composition, what) = kind switch
                {
                    N.UmcUser => ("UmcUsers", "UMC user"),
                    N.UmcUserGroup => ("UmcUserGroups", "UMC user group"),
                    _ => ("ProjectUsers", "project user"),
                };
                result.Target = $"{what} '{userName}'";
                var user = RequireIn(composition, userName, what);
                var role = FindIn("CustomRoles", roleName) ?? FindIn("SystemRoles", roleName)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The project has no custom or system role '{roleName}'. Nothing was changed.");
                var roles = Get(user, "Roles") ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "Roles returned null. Nothing was changed.");
                var add = method == M.AssignUserRole;
                var member = RequireMethodByShape(roles, add ? "Add" : "Remove", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(role), $"RoleAssociation.{(add ? "Add" : "Remove")}(Role)");
                Call(member, roles, role);
                result.Calls.Add($"Roles.{(add ? "Add" : "Remove")}('{roleName}')");
                Exists(result, roleName, add, Enumerate(Get(user, "Roles")).Any(r => Equals(r, role)), $"Role '{roleName}' of {what} '{userName}'", null);
                return true;
            }

            case M.SetAnonymousUser:
            {
                var active = info.Active ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "active is required. Nothing was changed.");
                result.Target = "anonymous user";
                Toggle(context, configurator, active ? "ActivateAnonymousUser" : "DeactivateAnonymousUser", null, active, "UmacConfigurator", result);
                if (Get(configurator, "AnonymousUser") is { } anonymous)
                    Compare(context, result, "IsActive", RuntimeServiceValues.Project(active).Value, null, ReadValue(context, anonymous, "IsActive"), "AnonymousUser");
                return true;
            }

            case M.UpdateUmcUser:
            case M.DeleteUmcUser:
            {
                var name = Required(info.UserName, "userName");
                var group = info.UserKind == N.UmcUserGroup;
                var composition = group ? "UmcUserGroups" : "UmcUsers";
                var what = group ? "UMC user group" : "UMC user";
                var type = group ? "UmcUserGroup" : "UmcUser";
                result.Target = $"{what} '{name}'";
                var user = RequireIn(composition, name, what);
                if (method == M.DeleteUmcUser)
                {
                    Delete(user, $"{type}.Delete()", result);
                    Exists(result, "user", false, FindIn(composition, name) is not null, $"{what} '{name}'", null);
                    return true;
                }

                if (info.Active is { } active) Toggle(context, user, active ? "Activate" : "Deactivate", "IsActive", active, type, result);
                if (info.NewName is not null)
                {
                    var setName = RequireMethod(user, "SetName", new[] { typeof(string) }, $"{type}.SetName(String)");
                    Call(setName, user, info.NewName);
                    result.Calls.Add($"{type}.SetName(\"{info.NewName}\")");
                    Exists(result, "newName", true, FindIn(composition, info.NewName) is not null, $"{what} '{info.NewName}'", null);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>Calls a parameterless toggle (Activate/Deactivate) and reads the state back when the type documents one.</summary>
    private static void Toggle(PlcSettingsContext context, object target, string method, string? state, bool expected, string type, PlcSettingsResultInfo result)
    {
        var member = RequireMethod(target, method, Type.EmptyTypes, $"{type}.{method}()");
        var before = state is null ? null : ReadValue(context, target, state);
        Call(member, target);
        result.Calls.Add($"{type}.{method}()");
        if (state is not null) Compare(context, result, state, RuntimeServiceValues.Project(expected).Value, before, ReadValue(context, target, state), type);
    }

    /// <summary>Calls a one-string setter method (SetAliasName) and reads the matching property back.</summary>
    private static void CallAndCompare(PlcSettingsContext context, object target, string method, string value, string property, string type, PlcSettingsResultInfo result)
    {
        var member = RequireMethod(target, method, new[] { typeof(string) }, $"{type}.{method}(String)");
        var before = ReadValue(context, target, property);
        Call(member, target, value);
        result.Calls.Add($"{type}.{method}(\"{value}\")");
        Compare(context, result, property, value, before, ReadValue(context, target, property), type);
    }
}
