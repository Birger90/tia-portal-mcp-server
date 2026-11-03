using System.Reflection;
using System.Security;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.Contracts.Plc.Safety.PlcSafetyApiCatalog;

namespace TiaMcpServer.OpennessWorker.Openness.Safety;

/// <summary>
/// The fail-safe (F-) PLC reads and writes. The worker has no compile reference to <c>Siemens.Engineering.Safety.dll</c>:
/// <c>SafetyAdministration</c> is found among the services the CPU device item lists (<c>GetServiceInfos()</c>, as the
/// technology objects do) or, when the listing does not name it, through the documented generic
/// <c>IEngineeringServiceProvider.GetService&lt;SafetyAdministration&gt;()</c> closed over the type loaded by name; every
/// member is bound by exact name and argument types (<see cref="PlcSafetyReflection"/>, <see cref="PlcSafetyApiCatalog"/>).
/// A missing member is <c>api_member_missing</c>. Passwords are read from the environment variable the request names,
/// handed to Siemens as a read-only <see cref="SecureString"/> and never returned or logged.
/// </summary>
internal static class PlcSafetyService
{
    private const string SafetyAssemblyName = "Siemens.Engineering.Safety";

    /// <summary>Exceptions that must escape a read: a lost or disposed TIA Portal object is never an "unreadable value".</summary>
    internal static bool IsFatal(Exception exception)
        => exception is NonRecoverableException or EngineeringObjectDisposedException or OutOfMemoryException or WorkerOperationException;

    /// <summary>The SafetyAdministration of a CPU device item: (service, reason it is absent, the Siemens.Engineering.Safety services it lists).</summary>
    internal static (object? Administration, string? Reason, List<string> Services) Administration(DeviceItem? host)
        => Service(host, SafetyAdministrationType,
            "The CPU device item offers no Siemens.Engineering.Safety.SafetyAdministration: it is not an F-CPU (or its F-capability is not activated), or STEP 7 Safety is not installed.");

    private static (object? Service, string? Reason, List<string> Services) Service(object? target, string fullName, string absent)
    {
        var (service, reason, services, _) = ServiceDetailed(target, fullName, absent);
        return (service, reason, services);
    }

    /// <summary>
    /// <see cref="Service(object?, string, string)"/> plus whether <c>GetServiceInfos()</c> listed the service although
    /// <c>GetService</c> returned null for it (live, V21, 2026-11-02, D13): that is reported as the reason, never thrown, so a
    /// read for which the service is optional goes on.
    /// </summary>
    private static (object? Service, string? Reason, List<string> Services, bool ListedButUnavailable) ServiceDetailed(object? target, string fullName, string absent)
    {
        if (target is null) return (null, "The device item that carries the PLC software is unknown.", new List<string>(), false);
        List<string> services;
        object? service = null;
        string? unavailable = null;
        try
        {
            var types = TechnologyObjectRuntime.ServiceTypes(target);
            services = types.Select(type => type.FullName ?? string.Empty)
                .Where(name => name.StartsWith(SafetyNamespace + ".", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal).ToList();
            service = TechnologyObjectRuntime.TryService(target, types, fullName, out unavailable);
        }
        catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.UnsupportedCapability)
        {
            services = new List<string>();
            service = GenericService(target, fullName, out var reason);
            if (service is null) return (null, $"{WorkerFailureCategories.ApiMemberMissing}: {exception.Message} {reason}".Trim(), services, false);
        }

        service ??= GenericService(target, fullName, out _);
        if (service is not null) return (service, null, services, false);
        return unavailable is not null ? (null, unavailable, services, true) : (null, absent, services, false);
    }

    /// <summary>
    /// The documented <c>GetService&lt;T&gt;()</c> (Siemens plc-safety-administration skill §2) closed over the Safety type
    /// loaded by its exact name; null when the assembly or the type is absent or the object does not offer the service.
    /// </summary>
    private static object? GenericService(object target, string fullName, out string? reason)
    {
        reason = null;
        Type? type;
        try
        {
            type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName, throwOnError: false)).FirstOrDefault(found => found is not null)
                ?? Assembly.Load(SafetyAssemblyName).GetType(fullName, throwOnError: false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            reason = $"{SafetyAssemblyName} could not be loaded ({exception.GetType().Name}); STEP 7 Safety may not be installed.";
            return null;
        }

        if (type is null)
        {
            reason = $"The installed {SafetyAssemblyName} has no type {fullName}.";
            return null;
        }

        if (target is not IEngineeringServiceProvider) return null;
        var generic = typeof(IEngineeringServiceProvider).GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (generic is null || !generic.IsGenericMethodDefinition) return null;
        try
        {
            return generic.MakeGenericMethod(type).Invoke(target, Array.Empty<object>());
        }
        catch (ArgumentException)
        {
            // The type violates GetService<T>'s constraint: it is not a service type here.
            return null;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is EngineeringException engineering && !IsFatal(engineering))
        {
            reason = engineering.Message;
            return null;
        }
    }

    // ---------------------------------------------------------------- reads

    public static PlcSafetyAdministrationInfo GetAdministration(ProjectBase project, TiaPortal? portal, PlcSafetyRequestInfo info)
    {
        var result = new PlcSafetyAdministrationInfo();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, info.PlcName, result.Messages).ToList();
        if (info.PlcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
        foreach (var plc in plcs) result.Plcs.Add(ReadCpu(plc));
        result.GlobalSettings = ReadGlobalSettings(portal);
        return result;
    }

    internal static PlcSafetyCpuInfo ReadCpu(PlcSoftwareLocator.DiscoveredPlcSoftware plc)
    {
        var cpu = new PlcSafetyCpuInfo { PlcName = plc.Software.Name, DeviceName = plc.DeviceName };
        var (administration, reason, services) = Administration(plc.HostDeviceItem);
        cpu.SafetyServices = services;
        cpu.Available = administration is not null;
        cpu.Reason = reason;
        cpu.SafetyUnits = SafetyUnits(plc.Software, cpu.Problems);
        if (administration is null) return cpu;

        cpu.Access.Add(PlcSafetyReflection.Read(administration, IsPasswordSet, IsFatal));
        cpu.Access.Add(PlcSafetyReflection.Read(administration, IsLoggedOn, IsFatal));
        cpu.PasswordSet = Bool(cpu.Access[0]);
        cpu.LoggedOn = Bool(cpu.Access[1]);

        if (TryGet(administration, Settings, cpu.Problems) is { } settings)
        {
            cpu.Settings = SettingsValues.Select(name => PlcSafetyReflection.Read(settings, name, IsFatal)).ToList();
            try
            {
                var versions = PlcSafetyReflection.Invoke(settings, GetApplicableSafetySystemVersions, Type.EmptyTypes, Array.Empty<object?>());
                cpu.ApplicableSafetySystemVersions = PlcSafetyReflection.Items(versions, GetApplicableSafetySystemVersions)
                    .Select(PlcSafetyReflection.VersionText).OfType<string>().ToList();
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                cpu.Problems.Add(Problem($"SafetySettings.{GetApplicableSafetySystemVersions}()", exception));
            }

            if (TryGet(settings, AssignmentOfBlockNumbers, cpu.Problems) is { } numbers)
                cpu.BlockNumbers = BlockNumberValues.Select(name => PlcSafetyReflection.Read(numbers, name, IsFatal)).ToList();
        }

        if (TryGet(administration, RuntimeGroups, cpu.Problems) is { } groups)
        {
            try
            {
                foreach (var group in PlcSafetyReflection.Items(groups, "SafetyAdministration.RuntimeGroups"))
                    cpu.RuntimeGroups.Add(ReadRuntimeGroup(group, cpu.Problems));
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                cpu.Problems.Add(Problem("SafetyAdministration.RuntimeGroups", exception));
            }
        }

        if (TryGet(administration, ProgramSignatures, cpu.Problems) is { } provider)
        {
            try
            {
                cpu.ProgramSignatures = PlcSafetyReflection.Signatures(provider);
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                cpu.Problems.Add(Problem("SafetyAdministration.ProgramSignatures.Signatures", exception));
            }
        }

        foreach (var value in cpu.Access.Concat(cpu.Settings).Concat(cpu.BlockNumbers).Concat(cpu.RuntimeGroups.SelectMany(g => g.Attributes)))
            if (value.State != PlcSafetyValueStates.Read) cpu.Problems.Add($"{value.Name}: {value.Error}");
        return cpu;
    }

    internal static PlcSafetyRuntimeGroupInfo ReadRuntimeGroup(object group, List<string>? problems = null)
    {
        Func<string, object?>? attribute = group is IEngineeringObject engineering ? name => engineering.GetAttribute(name) : null;
        var name = PlcSafetyReflection.Read(group, "Name", IsFatal, attribute);
        return new PlcSafetyRuntimeGroupInfo
        {
            Name = name.Value,
            Attributes = RuntimeGroupValues.Select(member => PlcSafetyReflection.Read(group, member, IsFatal, attribute)).ToList(),
            AttributeInfos = AttributeInfos(group, name.Value, problems),
        };
    }

    /// <summary>
    /// GetAttributeInfos() of an F-runtime group: every attribute's name and access mode, so a live read shows what TIA Portal
    /// offers to write (L2, live 2026-11-02: no attribute MainSafetyBlock). Null when the group is no IEngineeringObject or the call
    /// failed (the reason goes to <paramref name="problems"/>).
    /// </summary>
    internal static List<PlcSafetyAttributeAccessInfo>? AttributeInfos(object group, string? groupName, List<string>? problems)
    {
        if (group is not IEngineeringObject engineering) return null;
        try
        {
            return engineering.GetAttributeInfos().Select(i => new PlcSafetyAttributeAccessInfo { Name = i.Name, AccessMode = i.AccessMode.ToString() }).ToList();
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            problems?.Add($"RuntimeGroup '{groupName}'.GetAttributeInfos(): unreadable: {exception.Message}");
            return null;
        }
    }

    private static PlcSafetyGlobalSettingsInfo ReadGlobalSettings(TiaPortal? portal)
    {
        var (settings, reason, _) = Service(portal, GlobalSettingsType, "TIA Portal offers no Siemens.Engineering.Safety.GlobalSettings service (STEP 7 Safety may not be installed).");
        var info = new PlcSafetyGlobalSettingsInfo { Available = settings is not null, Reason = reason };
        if (settings is null) return info;
        info.Values = GlobalSettingsGetters.Select(name => PlcSafetyReflection.CallGetter(settings, name, IsFatal)).ToList();
        return info;
    }

    private static List<string>? SafetyUnits(PlcSoftware software, List<string> problems)
    {
        try
        {
            if (software.GetService<PlcUnitProvider>() is not { } provider) return null;
            var units = PlcSafetyReflection.Get(provider.UnitGroup, "SafetyUnits");
            return PlcSafetyReflection.Items(units, "PlcUnitSystemGroup.SafetyUnits")
                .Select(unit => PlcSafetyReflection.Get(unit, "Name")?.ToString() ?? string.Empty).ToList();
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            problems.Add(Problem("PlcUnitProvider.UnitGroup.SafetyUnits", exception));
            return null;
        }
    }

    public static PlcSafetyBlockListInfo ListBlocks(ProjectBase project, PlcSafetyRequestInfo info)
    {
        var result = new PlcSafetyBlockListInfo();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, info.PlcName, result.Messages).ToList();
        if (info.PlcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
        var offset = info.Offset ?? 0;
        var limit = info.Limit ?? PlcSafetyPolicy.DefaultBlockLimit;
        foreach (var plc in plcs)
        {
            var (administration, reason, _) = Administration(plc.HostDeviceItem);
            var entry = new PlcSafetyBlockPlcInfo { PlcName = plc.Software.Name, DeviceName = plc.DeviceName, Available = administration is not null, Reason = reason, Offset = offset };
            result.Plcs.Add(entry);
            if (administration is null) continue;
            var blocks = new List<PlcSafetyBlockInfo>();
            foreach (var (path, block) in Blocks(plc.Software, result.Messages))
            {
                var row = ReadBlock(path, block);
                if (row.SignatureProvider || PlcSafetyPolicy.IsFailSafeLanguage(row.ProgrammingLanguage)) blocks.Add(row);
            }

            blocks.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.BlockPath, right.BlockPath));
            entry.TotalBlocks = blocks.Count;
            entry.Blocks = blocks.Skip(offset).Take(limit).ToList();
            entry.NextOffset = offset + entry.Blocks.Count < blocks.Count ? offset + entry.Blocks.Count : null;
        }

        return result;
    }

    private static PlcSafetyBlockInfo ReadBlock(string path, PlcBlock block)
    {
        var row = new PlcSafetyBlockInfo { BlockPath = path, BlockType = block.GetType().Name };
        try
        {
            row.Name = block.Name;
            row.Number = block.Number;
            row.ProgrammingLanguage = block.ProgrammingLanguage.ToString();
            row.IsKnowHowProtected = block.IsKnowHowProtected;
            var (provider, reason, _, listedButUnavailable) = ServiceDetailed(block, SafetySignatureProviderType, string.Empty);
            row.SignatureProvider = provider is not null || listedButUnavailable;
            if (listedButUnavailable)
            {
                // D13: the block lists the provider, GetService returns null; the signatures are unavailable, the listing goes on.
                row.SignatureState = PlcSafetyValueStates.Unreadable;
                row.Problem = reason + " The F-signatures of this block are unavailable.";
            }
            else if (provider is not null)
            {
                // Unreadable until the read completes: a throwing read leaves it so, with the reason in Problem.
                row.SignatureState = PlcSafetyValueStates.Unreadable;
                row.Signatures = PlcSafetyReflection.Signatures(provider);
                row.SignatureState = PlcSafetyValueStates.Read;
            }
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            row.Problem = exception.Message;
        }

        return row;
    }

    /// <summary>Every block of the PLC: the program blocks with their user and system groups, the software units and the safety units.</summary>
    private static IEnumerable<(string Path, PlcBlock Block)> Blocks(PlcSoftware software, List<string> messages)
    {
        var roots = new List<(string, object)> { ($"{software.Name}/Blocks", software.BlockGroup) };
        try
        {
            if (software.GetService<PlcUnitProvider>() is { } provider)
            {
                foreach (PlcUnit unit in provider.UnitGroup.Units) roots.Add(($"{software.Name}/Units/{unit.Name}/Blocks", unit.BlockGroup));
                var safetyUnits = PlcSafetyReflection.Items(PlcSafetyReflection.Get(provider.UnitGroup, "SafetyUnits"), "PlcUnitSystemGroup.SafetyUnits");
                foreach (var unit in safetyUnits)
                    if (PlcSafetyReflection.Get(unit, "BlockGroup") is { } group)
                        roots.Add(($"{software.Name}/Units/{PlcSafetyReflection.Get(unit, "Name")}/Blocks", group));
            }
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            messages.Add($"The software units of PLC '{software.Name}' could not be read completely: {exception.Message}");
        }

        foreach (var (path, group) in roots)
            foreach (var found in Walk(path, group))
                yield return found;
    }

    private static IEnumerable<(string Path, PlcBlock Block)> Walk(string path, object group)
    {
        var blocks = PlcSafetyReflection.Get(group, "Blocks");
        foreach (var item in PlcSafetyReflection.Items(blocks, path + " Blocks"))
            if (item is PlcBlock block) yield return ($"{path}/{block.Name}", block);
        foreach (var name in new[] { "Groups", "SystemBlockGroups" })
        {
            if (Reflectable(group, name) is not { } children) continue;
            foreach (var child in PlcSafetyReflection.Items(children, path + " " + name))
            {
                var childName = PlcSafetyReflection.Get(child, "Name")?.ToString() ?? "?";
                foreach (var found in Walk($"{path}/{childName}", child)) yield return found;
            }
        }
    }

    private static object? Reflectable(object target, string name)
        => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { CanRead: true } property ? property.GetValue(target) : null;

    // ---------------------------------------------------------------- writes

    public static (PlcSafetyWriteResultInfo Result, List<string> Warnings) Write(ProjectBase project, string method, PlcSafetyRequestInfo info)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, info.PlcName);
        var (administration, reason, _) = Administration(plc.HostDeviceItem);
        if (administration is null)
            throw new WorkerOperationException(reason?.StartsWith(WorkerFailureCategories.ApiMemberMissing, StringComparison.Ordinal) == true
                ? WorkerFailureCategories.ApiMemberMissing : WorkerFailureCategories.TargetKindUnsupported,
                $"{method}: PLC '{plc.Software.Name}': {reason}");
        var warnings = new List<string>();
        var result = new PlcSafetyWriteResultInfo
        {
            Operation = method,
            PlcName = plc.Software.Name,
            DeviceName = plc.DeviceName,
            PasswordVariable = PlcSafetyMethods.TakesPassword(method) ? info.PasswordVariable : null,
        };
        Guarded(() =>
        {
            result.PasswordSetBefore = PlcSafetyReflection.GetBool(administration, IsPasswordSet);
            result.LoggedOnBefore = PlcSafetyReflection.GetBool(administration, IsLoggedOn);
        });

        switch (method)
        {
            case PlcSafetyMethods.Login:
                // Not stricter than TIA Portal: a state that makes the call look pointless is reported, and TIA Portal decides.
                if (result.PasswordSetBefore == false)
                    warnings.Add($"safety_login: the safety program of PLC '{plc.Software.Name}' reports no safety offline program password; {Login} is called anyway and TIA Portal decides.");
                if (result.LoggedOnBefore == true)
                {
                    warnings.Add($"safety_login: this session is already logged on to the safety program of PLC '{plc.Software.Name}'; {Login} was not called.");
                    break;
                }

                using (var password = Password(info.PasswordVariable, method))
                    Guarded(() => PlcSafetyReflection.Invoke(administration, Login, new[] { typeof(SecureString) }, new object?[] { password }));
                break;
            case PlcSafetyMethods.Logoff:
                if (result.LoggedOnBefore == false)
                {
                    warnings.Add($"safety_logoff: this session is not logged on to the safety program of PLC '{plc.Software.Name}'; {Logoff} was not called.");
                    break;
                }

                Guarded(() => PlcSafetyReflection.Invoke(administration, Logoff, Type.EmptyTypes, Array.Empty<object?>()));
                break;
            case PlcSafetyMethods.SetPassword:
                if (result.PasswordSetBefore == true)
                    warnings.Add($"set_safety_password: PLC '{plc.Software.Name}' already reports a safety offline program password; {SetPassword} is called anyway and TIA Portal decides (it may require revoke_safety_password first).");
                using (var password = Password(info.PasswordVariable, method))
                    Guarded(() => PlcSafetyReflection.Invoke(administration, SetPassword, new[] { typeof(SecureString) }, new object?[] { password }));
                break;
            case PlcSafetyMethods.RevokePassword:
                if (result.PasswordSetBefore == false)
                    warnings.Add($"revoke_safety_password: PLC '{plc.Software.Name}' reports no safety offline program password; {RevokePassword} is called anyway and TIA Portal decides.");
                using (var password = Password(info.PasswordVariable, method))
                    Guarded(() => PlcSafetyReflection.Invoke(administration, RevokePassword, new[] { typeof(SecureString) }, new object?[] { password }));
                break;
            case PlcSafetyMethods.CreateRuntimeGroup:
                CreateRuntimeGroup(project, plc, administration, info, result);
                break;
            case PlcSafetyMethods.SetRuntimeGroup:
                SetRuntimeGroup(project, plc, administration, info, result);
                break;
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported fail-safe write '{method}'.");
        }

        Guarded(() =>
        {
            result.PasswordSetAfter = PlcSafetyReflection.GetBool(administration, IsPasswordSet);
            result.LoggedOnAfter = PlcSafetyReflection.GetBool(administration, IsLoggedOn);
        });
        var expected = method switch
        {
            PlcSafetyMethods.Login => result.LoggedOnAfter == true,
            PlcSafetyMethods.Logoff => result.LoggedOnAfter == false,
            PlcSafetyMethods.SetPassword => result.PasswordSetAfter == true,
            PlcSafetyMethods.RevokePassword => result.PasswordSetAfter == false,
            // set_safety_runtime_group read every assignment back itself (postcondition_failed there).
            PlcSafetyMethods.SetRuntimeGroup => result.RuntimeGroup is not null,
            _ => result.RuntimeGroup is not null,
        };
        if (!expected)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{method} returned, but the read-back of PLC '{plc.Software.Name}' does not show the change "
                + $"(password set: {Text(result.PasswordSetAfter)}, logged on: {Text(result.LoggedOnAfter)}{(PlcSafetyMethods.IsRuntimeGroupWrite(method) ? ", runtime group not found" : string.Empty)}). Inspect plc_read get_safety_administration before retrying.");
        return (result, warnings);
    }

    private static void CreateRuntimeGroup(ProjectBase project, PlcSoftwareLocator.DiscoveredPlcSoftware plc, object administration,
        PlcSafetyRequestInfo info, PlcSafetyWriteResultInfo result)
    {
        // TIA Portal's rule: a login only when a safety password is set; an unreadable state is TIA Portal's to decide.
        var decision = PlcSafetyPolicy.Decide(true, result.PasswordSetBefore, result.LoggedOnBefore, earlierLoginInCall: false);
        if (PlcSafetyPolicy.Refusal(decision, plc.Software.Name, "create_safety_runtime_group") is { } refusal)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, refusal + " Nothing was changed.");
        var name = info.RuntimeGroupName ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "create_safety_runtime_group requires runtimeGroupName.");
        var groups = Guarded(() => PlcSafetyReflection.Get(administration, RuntimeGroups))
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "SafetyAdministration.RuntimeGroups returned null.");
        if (Guarded(() => PlcSafetyReflection.Invoke(groups, "Find", new[] { typeof(string) }, new object?[] { name })) is not null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"create_safety_runtime_group: PLC '{plc.Software.Name}' already has an F-runtime group named '{name}'. Nothing was changed.");
        var main = info.MainSafetyBlockPath is null ? null : Block(project, plc, info.MainSafetyBlockPath, "mainSafetyBlockPath");
        var instance = info.MainSafetyInstanceDbPath is null ? null : Block(project, plc, info.MainSafetyInstanceDbPath, "mainSafetyInstanceDbPath");
        result.Overload = PlcSafetyPolicy.RuntimeGroupOverload(main is not null, instance is not null);
        var types = main is null ? new[] { typeof(string) } : instance is null ? new[] { typeof(string), typeof(PlcBlock) } : new[] { typeof(string), typeof(PlcBlock), typeof(PlcBlock) };
        var arguments = main is null ? new object?[] { name } : instance is null ? new object?[] { name, main } : new object?[] { name, main, instance };
        Guarded(() => PlcSafetyReflection.Invoke(groups, "Create", types, arguments));
        var created = Guarded(() => PlcSafetyReflection.Invoke(groups, "Find", new[] { typeof(string) }, new object?[] { name }));
        result.RuntimeGroup = created is null ? null : ReadRuntimeGroup(created);
    }

    /// <summary>
    /// set_safety_runtime_group: assigns the main safety block (and the instance DB of an F-FB) of an existing F-runtime group.
    /// Both assignments are bound (<see cref="PlcSafetyRuntimeGroupBinding"/>) and both blocks resolved before the first call,
    /// so a missing member changes nothing (api_member_missing); each is read back with GetAttribute(MainSafetyBlockName /
    /// MainSafetyBlockIDbName) and must name the block, else postcondition_failed. Not live-tested.
    /// </summary>
    private static void SetRuntimeGroup(ProjectBase project, PlcSoftwareLocator.DiscoveredPlcSoftware plc, object administration,
        PlcSafetyRequestInfo info, PlcSafetyWriteResultInfo result)
    {
        const string operation = "set_safety_runtime_group";
        var decision = PlcSafetyPolicy.Decide(true, result.PasswordSetBefore, result.LoggedOnBefore, earlierLoginInCall: false);
        if (PlcSafetyPolicy.Refusal(decision, plc.Software.Name, operation) is { } refusal)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, refusal + " Nothing was changed.");
        var name = info.RuntimeGroupName ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{operation} requires runtimeGroupName.");
        var mainPath = info.MainSafetyBlockPath ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{operation} requires mainSafetyBlockPath.");
        var group = FindRuntimeGroup(administration, name)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"{operation}: PLC '{plc.Software.Name}' has no F-runtime group named '{name}'. Nothing was changed.");
        var requested = new List<(string Member, string Path, PlcBlock Block)>
        {
            (PlcSafetyRuntimeGroupBinding.MainSafetyBlock, mainPath, Block(project, plc, mainPath, "mainSafetyBlockPath", operation)),
        };
        if (info.MainSafetyInstanceDbPath is { } idbPath)
            requested.Add((PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb, idbPath, Block(project, plc, idbPath, "mainSafetyInstanceDbPath", operation)));
        var bound = requested.Select(r => (r.Member, r.Path, r.Block, Assignment: Guarded(() => BindAssignment(group, r.Member)))).ToList();
        result.Assignments = new List<PlcSafetyRuntimeGroupAssignmentInfo>();
        foreach (var (member, path, block, assignment) in bound)
        {
            var attribute = PlcSafetyRuntimeGroupBinding.ReadBackAttribute(member);
            var entry = new PlcSafetyRuntimeGroupAssignmentInfo
            {
                Member = member, BlockPath = path, BlockName = block.Name, Route = assignment.Route, Signature = assignment.Signature,
                Before = TryReadAttribute(group, attribute),
            };
            assignment.Apply(block, block.Name);
            entry.After = TryReadAttribute(group, attribute);
            result.Assignments.Add(entry);
            if (!PlcMainSafetyLinks.Same(entry.After, block.Name))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"{operation}: {assignment.Signature} returned for F-runtime group '{name}', but {attribute} reads '{entry.After ?? "null"}' instead of '{block.Name}' afterwards"
                    + (result.Assignments.Count > 1 ? " (the earlier assignment of this item was applied)" : string.Empty)
                    + ". Inspect plc_read get_safety_administration before retrying.");
        }

        result.RuntimeGroup = ReadRuntimeGroup(FindRuntimeGroup(administration, name) ?? group);
    }

    /// <summary>The F-runtime group of that exact name (RuntimeGroupComposition.Find); null when there is none.</summary>
    internal static object? FindRuntimeGroup(object administration, string name)
    {
        var groups = Guarded(() => PlcSafetyReflection.Get(administration, RuntimeGroups))
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "SafetyAdministration.RuntimeGroups returned null.");
        return Guarded(() => PlcSafetyReflection.Invoke(groups, "Find", new[] { typeof(string) }, new object?[] { name }));
    }

    /// <summary>Binds the assignment of a main safety block member on the live group (property first, then SetAttribute); nothing is called.</summary>
    internal static PlcSafetyRuntimeGroupBinding.Assignment BindAssignment(object group, string member)
        => PlcSafetyRuntimeGroupBinding.Bind(group, member, typeof(PlcBlock), group is IEngineeringObject engineering
            ? new PlcSafetyRuntimeGroupBinding.AttributeSurface(
                () => engineering.GetAttributeInfos().Select(i => new PlcSafetyRuntimeGroupBinding.AttributeAccess(i.Name, i.AccessMode.ToString())),
                (attribute, value) => engineering.SetAttribute(attribute, value))
            : null);

    /// <summary>An attribute of the group as text: GetAttribute when the group is an IEngineeringObject, else the property; null when unset or unreadable.</summary>
    internal static string? TryReadAttribute(object group, string name)
    {
        try
        {
            if (group is IEngineeringObject engineering) return PlcSafetyPolicy.Text(engineering.GetAttribute(name));
            return PlcSafetyPolicy.Text(PlcSafetyReflection.Get(group, name));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Unset, missing or unreadable: the comparison treats null as "not this block".
            return null;
        }
    }

    /// <summary>Every F-runtime group of the administration with its main safety block names (unreadable names are null).</summary>
    internal static List<PlcMainSafetyLink> ReadLinks(object administration)
    {
        var links = new List<PlcMainSafetyLink>();
        var groups = Guarded(() => PlcSafetyReflection.Get(administration, RuntimeGroups));
        foreach (var group in PlcSafetyReflection.Items(groups, "SafetyAdministration.RuntimeGroups"))
            links.Add(Link(group));
        return links;
    }

    /// <summary>One group's link as read now.</summary>
    internal static PlcMainSafetyLink Link(object group)
        => new(PlcSafetyPolicy.Text(Guarded(() => PlcSafetyReflection.Get(group, "Name"))) ?? string.Empty,
            TryReadAttribute(group, PlcSafetyRuntimeGroupBinding.MainSafetyBlockName),
            TryReadAttribute(group, PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdbName));

    private static PlcBlock Block(ProjectBase project, PlcSoftwareLocator.DiscoveredPlcSoftware plc, string path, string field, string operation = "create_safety_runtime_group")
    {
        var address = BlockAddress.Parse(path);
        var resolved = PlcSoftwareLocator.FindUnique(project, address.PlcName);
        if (!ReferenceEquals(resolved.Software, plc.Software) && !Equals(resolved.Software, plc.Software))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{operation}: {field} '{path}' names a block of another PLC than '{plc.Software.Name}'.");
        return BlockTargetResolver.ResolveForImport(project, address).Block
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{operation}: {field} '{path}' was not found. Nothing was changed.");
    }

    /// <summary>
    /// The password of the named source (an environment variable or a Windows generic credential, <see cref="PasswordSources"/>)
    /// as a read-only SecureString; the source was validated by the host. The reason of a failure never contains the secret.
    /// </summary>
    private static SecureString Password(string? source, string method)
    {
        if (!PasswordSources.IsSource(source))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{method}: passwordVariable must name an environment variable or a credential target starting with '{PasswordSources.CredentialTargetPrefix}'.");
        return PasswordSources.Resolve(source, Environment.GetEnvironmentVariable, null, out var reason)
            ?? throw new WorkerOperationException(
                PasswordSources.KindOf(source) == PasswordSourceKind.Credential && reason == WindowsCredentialReader.UnsupportedReason
                    ? WorkerFailureCategories.UnsupportedCapability : WorkerFailureCategories.ValidationError,
                $"{method}: {reason} Nothing was changed.");
    }

    /// <summary>Runs a Siemens call; a missing member becomes <c>api_member_missing</c>, everything else escapes for the shared classification.</summary>
    private static T Guarded<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (PlcSafetyApiMissingException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, exception.Message);
        }
    }

    private static void Guarded(Action call) => Guarded<object?>(() =>
    {
        call();
        return null;
    });

    private static object? TryGet(object target, string name, List<string> problems)
    {
        try
        {
            return PlcSafetyReflection.Get(target, name);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            problems.Add(Problem($"{target.GetType().Name}.{name}", exception));
            return null;
        }
    }

    private static string Problem(string member, Exception exception)
        => exception is PlcSafetyApiMissingException ? exception.Message : $"{member}: unreadable: {exception.Message}";

    private static bool? Bool(PlcSafetyValueInfo value)
        => value.State == PlcSafetyValueStates.Read && bool.TryParse(value.Value, out var flag) ? flag : null;

    private static string Text(bool? value) => value switch { true => "true", false => "false", _ => "unknown" };
}

/// <summary>The production lookup of <see cref="PlcSafetyGate"/>.</summary>
internal static partial class PlcSafetyGate
{
    static partial void FindAdministration(DeviceItem? host, ref object? administration)
        => administration = PlcSafetyService.Administration(host).Administration;
}
