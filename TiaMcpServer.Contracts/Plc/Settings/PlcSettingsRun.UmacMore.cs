using System.Security;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcUmacMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcUmacNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Package G (V21 API completion; not live-tested): project protection and the UMC additions of the UMAC family. Evidence:
/// Siemens.Engineering.Base.xml (V21): <c>ProjectBase.ProtectProject(String administratorUsername, SecureString administratorUserPassword)</c>
/// ("Protects the project"), <c>UmcUserComposition.CreateOfflineUmcUser(String)</c>, <c>UmcUserGroupComposition.CreateOfflineUmcUserGroup()</c>,
/// <c>UmcUserComposition.Create(UmcUserInfo)</c>, <c>UmcUserGroupComposition.Create(UmcUserGroupInfo)</c>, <c>UmcServerConfigurator.UmcServer</c>,
/// <c>.CheckConsistency()</c> (Boolean), <c>UmcServer.GetUserByName(String)</c> (UmcUserInfo: <c>Name</c>), <c>.GetUserGroupByName(String)</c>
/// (UmcUserGroupInfo: <c>UserGroupName</c>, <c>Description</c>), event <c>UmcServer.Authentication</c> with <c>UmcAuthenticationEventArgs.UmcCredentials</c>
/// (<c>UmacCredentials.Name</c>, <c>.Type</c> (UmacUserType Global/Project), <c>.SetPassword(SecureString)</c>). How the UmcServerConfigurator is obtained is
/// not documented: it is searched among the project's, then the UmacConfigurator's <c>GetServiceInfos()</c>. Passwords only from sources.
/// </summary>
public static partial class PlcSettingsRun
{
    private static bool WriteUmacMore(string method, PlcSettingsContext context, PlcSettingsRequestInfo info, PlcSettingsResultInfo result)
    {
        string Required(string? value, string field) => value ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"{field} is required. Nothing was changed.");
        switch (method)
        {
            case M.ProtectProject:
            {
                var project = RequireProject(context);
                var user = Required(info.UserName, "userName");
                result.Owner = PlcSettingsNames.ProjectOwner;
                result.Target = $"project administrator '{user}'";
                var protect = RequireMethod(project, "ProtectProject", new[] { typeof(string), typeof(SecureString) }, "ProjectBase.ProtectProject(String, SecureString)");
                WithPassword(context, info.PasswordVariable, "administrator", result, secure => Call(protect, project, user, secure));
                result.Calls.Add("ProjectBase.ProtectProject(String, SecureString)");
                // Read back through UMAC (Siemens security skill: EngineeringFunctionRights not empty means protected; the administrator is a project user).
                var (configurator, _, unavailable) = FindService(context, new[] { project }, N.Configurator);
                if (configurator is null)
                {
                    result.Messages.Add((unavailable ?? $"The project lists no {N.Configurator} after ProtectProject") + "; the protection is not read back. Inspect it with plc_read get_umac.");
                    return true;
                }

                result.Service = N.Configurator;
                Exists(result, "protected", true, Enumerate(Get(configurator, "EngineeringFunctionRights")).Any(), "The project protection (EngineeringFunctionRights)", null);
                Exists(result, "administrator", true,
                    Find(Get(configurator, "ProjectUsers") ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "UmacConfigurator.ProjectUsers returned null."),
                        typeof(string), user, "ProjectUsers.Find(String)") is not null,
                    $"Project user '{user}'", null);
                return true;
            }
            case M.CreateOfflineUmcUser:
            case M.CreateOfflineUmcUserGroup:
            {
                var (configurator, owner) = Configurator(context);
                Describe(context, result, owner, N.Configurator);
                var group = method == M.CreateOfflineUmcUserGroup;
                var compositionName = group ? "UmcUserGroups" : "UmcUsers";
                var composition = Get(configurator, compositionName)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, $"UmacConfigurator.{compositionName} returned null. Nothing was changed.");
                if (!group)
                {
                    var name = Required(info.UserName, "userName");
                    result.Target = $"offline UMC user '{name}'";
                    var create = RequireMethod(composition, "CreateOfflineUmcUser", new[] { typeof(string) }, "UmcUserComposition.CreateOfflineUmcUser(String)");
                    Call(create, composition, name);
                    result.Calls.Add($"UmcUserComposition.CreateOfflineUmcUser(\"{name}\")");
                    Exists(result, "user", true, Find(composition, typeof(string), name, "UmcUsers.Find(String)") is not null, $"Offline UMC user '{name}'", null);
                    return true;
                }

                var createGroup = RequireMethod(composition, "CreateOfflineUmcUserGroup", Type.EmptyTypes, "UmcUserGroupComposition.CreateOfflineUmcUserGroup()");
                var groupName = info.Name;
                // K96 (live 2026-11-07, build 986928f): the call created the group with TIA's default name (Group_1), but a count
                // comparison of the composition enumerated before and after the call failed, so the item stopped before SetName.
                // The returned group is renamed first; the read-back then looks the group up by its actual name in a freshly read
                // UmacConfigurator.UmcUserGroups (Find(String)), never by counting.
                var created = Call(createGroup, composition)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, "UmcUserGroupComposition.CreateOfflineUmcUserGroup() returned null. Inspect it with plc_read get_umac.");
                result.Calls.Add("UmcUserGroupComposition.CreateOfflineUmcUserGroup()");
                var defaultName = Get(created, "Name") as string;
                if (groupName is not null)
                {
                    var rename = RequireMethod(created, "SetName", new[] { typeof(string) }, "UmcUserGroup.SetName(String)");
                    try
                    {
                        Call(rename, created, groupName);
                    }
                    catch (Exception exception) when (context.Recoverable(exception))
                    {
                        // The group exists under TIA's name; say which, so it can be renamed or deleted.
                        throw new RuntimeBindingException(WorkerFailureCategories.EngineeringRejected,
                            $"UmcUserGroup.SetName(\"{groupName}\") was rejected: {exception.Message} The group was created as '{defaultName ?? "(unreadable name)"}' and keeps that name. Inspect it with plc_read get_umac.");
                    }

                    result.Calls.Add($"UmcUserGroup.SetName(\"{groupName}\")");
                }

                var actualName = Get(created, "Name") as string;
                result.Target = $"offline UMC user group '{actualName ?? groupName ?? "(unnamed)"}'";
                if (actualName is not null) result.Items.Add(actualName);
                var fresh = Get(configurator, "UmcUserGroups")
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "UmacConfigurator.UmcUserGroups returned null after the call; the new group is not read back. Inspect it with plc_read get_umac.");
                if (actualName is null)
                    throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                        "UmcUserGroupComposition.CreateOfflineUmcUserGroup() returned a group whose Name is unreadable, so it cannot be read back; the group may exist. Inspect it with plc_read get_umac.");

                Exists(result, "group", true, Find(fresh, typeof(string), actualName, "UmcUserGroups.Find(String)") is not null, $"Offline UMC user group '{actualName}'", null);
                if (groupName is not null)
                {
                    result.Changes.Add(new PlcSettingChangeInfo
                    {
                        Name = "name", Requested = groupName, Before = RuntimeServiceValues.Project(defaultName), After = RuntimeServiceValues.Project(actualName),
                        Matched = string.Equals(actualName, groupName, StringComparison.Ordinal),
                    });
                    if (!string.Equals(actualName, groupName, StringComparison.Ordinal))
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                            $"UmcUserGroup.SetName(\"{groupName}\") returned, but the group reads '{actualName}' afterwards. Inspect it with plc_read get_umac.");
                }

                return true;
            }
            case M.ImportUmcUser:
            {
                var (configurator, owner) = Configurator(context);
                Describe(context, result, owner, N.Configurator);
                var name = Required(info.Name, "name");
                var group = info.UserKind == N.UmcUserGroup;
                var compositionName = group ? "UmcUserGroups" : "UmcUsers";
                var what = group ? "UMC user group" : "UMC user";
                result.Target = $"{what} '{name}'";
                var composition = Get(configurator, compositionName)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, $"UmacConfigurator.{compositionName} returned null. Nothing was changed.");
                var server = UmcServer(context, configurator);
                var fetch = RequireMethod(server, group ? "GetUserGroupByName" : "GetUserByName", new[] { typeof(string) },
                    group ? "UmcServer.GetUserGroupByName(String)" : "UmcServer.GetUserByName(String)");
                var serverInfo = WithUmcCredentials(context, server, info, result, () => Call(fetch, server, name))
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The UMC server has no {what} '{name}'. Nothing was changed.");
                result.Calls.Add($"UmcServer.{fetch.Name}(\"{name}\")");
                var create = RequireMethodByShape(composition, "Create", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(serverInfo),
                    group ? "Create(UmcUserGroupInfo)" : "Create(UmcUserInfo)");
                Call(create, composition, serverInfo);
                result.Calls.Add(group ? "UmcUserGroupComposition.Create(UmcUserGroupInfo)" : "UmcUserComposition.Create(UmcUserInfo)");
                Exists(result, "user", true, Find(composition, typeof(string), name, $"{compositionName}.Find(String)") is not null, $"{what} '{name}'", null);
                return true;
            }
            default:
                return false;
        }
    }

    private static PlcSettingsReadInfo ReadUmcServer(string method, PlcSettingsContext? project, PlcSettingsReadInfo result, PlcSettingsRequestInfo info)
    {
        if (project is null) return result;
        var section = new PlcSettingsSectionInfo { Name = method == M.CheckUmcConsistency ? "umcConsistency" : "umcServerUser", Service = N.UmcServerConfigurator };
        result.Project.Add(section);
        var scratch = new PlcSettingsResultInfo { Operation = method };
        try
        {
            var (configurator, _) = Configurator(project);
            var server = UmcServer(project, configurator);
            section.Offered = true;
            section.Owner = PlcSettingsNames.ProjectOwner;
            if (method == M.CheckUmcConsistency)
            {
                var serverConfigurator = UmcServerConfigurator(project, configurator);
                var check = RequireMethod(serverConfigurator, "CheckConsistency", Type.EmptyTypes, "UmcServerConfigurator.CheckConsistency()");
                var consistent = WithUmcCredentials(project, server, info, scratch, () => Call(check, serverConfigurator));
                section.Properties.Add(new PlcSettingPropertyInfo { Name = "Consistent", Value = RuntimeServiceValues.Project(consistent) });
                section.Note = "CheckConsistency compares the project's UMAC copy of the UMC data with the UMC server; synchronizing (Synchronize) is not offered by this server.";
            }
            else
            {
                var name = info.Name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "name is required.");
                var group = info.UserKind == N.UmcUserGroup;
                var fetch = RequireMethod(server, group ? "GetUserGroupByName" : "GetUserByName", new[] { typeof(string) }, group ? "UmcServer.GetUserGroupByName(String)" : "UmcServer.GetUserByName(String)");
                var found = WithUmcCredentials(project, server, info, scratch, () => Call(fetch, server, name));
                section.Properties.Add(new PlcSettingPropertyInfo { Name = "Found", Value = RuntimeServiceValues.Project(found is not null) });
                if (found is not null)
                    foreach (var property in group ? new[] { "UserGroupName", "Description" } : new[] { "Name" })
                        section.Properties.Add(new PlcSettingPropertyInfo { Name = property, Value = ReadValue(project, found, property) });
            }

            if (scratch.Messages.Count > 0) section.Note = string.Join(" ", new[] { section.Note }.Concat(scratch.Messages).Where(m => m is not null));
        }
        catch (RuntimeBindingException exception)
        {
            section.Unavailable = exception.Message;
        }
        catch (Exception exception) when (project.Recoverable(exception))
        {
            section.Unavailable = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}");
        }

        return result;
    }

    private static object UmcServerConfigurator(PlcSettingsContext context, object configurator)
        => RequireService(context, new[] { RequireProject(context), configurator }, N.UmcServerConfigurator, "The project (and its UmacConfigurator)").Service;

    private static object UmcServer(PlcSettingsContext context, object configurator)
        => Get(UmcServerConfigurator(context, configurator), "UmcServer")
            ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, "UmcServerConfigurator.UmcServer returned null: the project has no UMC server configured. Nothing was changed.");

    /// <summary>
    /// Runs a call that contacts the UMC server. With userName and passwordVariable, the server's <c>Authentication</c> event is
    /// answered with those credentials (UmacCredentials.Name, Type Global, SetPassword); the password is resolved before the call and
    /// disposed after it. The handler never throws into TIA Portal; what it could not set is reported in the messages.
    /// </summary>
    private static object? WithUmcCredentials(PlcSettingsContext context, object server, PlcSettingsRequestInfo info, PlcSettingsResultInfo result, Func<object?> call)
    {
        if (info.UserName is null && info.PasswordVariable is null)
        {
            result.Messages.Add("No UMC credentials were given (userName, passwordVariable); TIA Portal authenticates on its own (for example single sign-on) or refuses.");
            return call();
        }

        if (info.UserName is null || string.IsNullOrWhiteSpace(info.PasswordVariable))
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "UMC credentials need both userName and passwordVariable. Nothing was changed.");
        using var secure = context.Resolve(info.PasswordVariable!);
        var problems = new List<string>();
        var answered = 0;
        using (RuntimeEvents.Subscribe(server, "Authentication", args => AnswerUmc(args, info.UserName!, secure, problems, ref answered), "UmcServer.Authentication"))
        {
            var value = call();
            result.PasswordSources.Add(new PlcSettingsPasswordUseInfo { Purpose = "umcServer", Source = info.PasswordVariable!, Kind = PasswordSources.KindName(info.PasswordVariable) ?? string.Empty });
            result.Messages.Add($"UMC server authentication requests answered: {answered}." + (problems.Count == 0 ? string.Empty : " " + string.Join(" ", problems.Distinct())));
            return value;
        }
    }

    private static void AnswerUmc(object? args, string user, SecureString password, List<string> problems, ref int answered)
    {
        try
        {
            var credentials = args is null ? null : args.GetType().GetProperty("UmcCredentials")?.GetValue(args, null);
            if (credentials is null)
            {
                problems.Add("An authentication request carried no UmcCredentials.");
                return;
            }

            var name = credentials.GetType().GetProperty("Name");
            if (name?.GetSetMethod() is not null) name.SetValue(credentials, user, null);
            else if (!string.Equals(name?.GetValue(credentials, null) as string, user, StringComparison.Ordinal))
                problems.Add("UmacCredentials.Name has no setter on this installation; the user name could not be passed.");
            var type = credentials.GetType().GetProperty("Type");
            if (type?.GetSetMethod() is not null && type.PropertyType.IsEnum && Library.LibraryRuntimeBinding.EnumValue(type.PropertyType, "Global") is { } global)
                type.SetValue(credentials, global, null);
            var setPassword = credentials.GetType().GetMethod("SetPassword", new[] { typeof(SecureString) });
            if (setPassword is null)
            {
                problems.Add("UmacCredentials.SetPassword(SecureString) is missing on this installation.");
                return;
            }

            setPassword.Invoke(credentials, new object[] { password });
            answered++;
        }
        catch (Exception exception)
        {
            var inner = exception is System.Reflection.TargetInvocationException { InnerException: { } cause } ? cause : exception;
            problems.Add($"Answering the UMC authentication failed ({inner.GetType().Name}).");
        }
    }
}
