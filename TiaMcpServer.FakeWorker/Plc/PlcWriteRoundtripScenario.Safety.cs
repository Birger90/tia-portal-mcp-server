using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// The fail-safe part of <c>plc-write-roundtrip</c>: PLC_1 (Station_1) is an F-CPU whose safety offline program has a
/// password (the value of <see cref="Password"/>, compared with the environment variable the request names, like the
/// worker reads it) and is not logged on; it has the F-runtime group <c>RTG1</c>, whose main safety block is the SD fixture
/// <c>FB_Safety</c> (IDB name <c>FB_Safety_DB</c>), and the F-block <c>Safety_FB</c> (F_LAD) with one signature. A SIMATIC
/// SD import of <c>FB_Safety</c> drops RTG1's MainSafetyBlockName as TIA Portal V21 did live on 2026-11-02 (E7); the
/// SimaticML route keeps it (E7b). The fake then runs the worker's protection: it restores the reference (warning), or with
/// <c>noRelink</c> cannot (warning with the repair). PLC_2 is a standard CPU. Update of an F-block applies the worker's Siemens-free rule
/// (<see cref="PlcSafetyPolicy.Decide"/>; a login only when a password is set). This models the access state only; it is no evidence of Siemens behavior.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    public const string Password = "FakeSafetyPassword!1";

    private bool _safetyPasswordSet = true;
    private bool _safetyLoggedOn;
    private readonly Dictionary<string, (string? Main, string? Idb)> _runtimeGroups = new(StringComparer.OrdinalIgnoreCase) { ["RTG1"] = ("FB_Safety", "FB_Safety_DB") };

    private bool IsFailSafeCpu(Plc plc) => Names.Equals(plc.Software, "PLC_1") && Names.Equals(plc.Device, "Station_1");

    private string Safety(WorkerRequest request)
    {
        var info = request.PlcSafety ?? Throw<PlcSafetyRequestInfo>(WorkerFailureCategories.ValidationError, $"{request.Method} requires the fail-safe request.");
        switch (request.Method)
        {
            case PlcSafetyMethods.GetAdministration:
                return Ok(new PlcSafetyAdministrationInfo
                {
                    Plcs = SafetyPlcs(info.PlcName).Select(Cpu).ToList(),
                    GlobalSettings = new PlcSafetyGlobalSettingsInfo
                    {
                        Available = true,
                        Values = { new PlcSafetyValueInfo { Name = "SafetyModificationsPossible", Value = "true", ValueType = "System.Boolean" } },
                    },
                });
            case PlcSafetyMethods.ListBlocks:
                return Ok(new PlcSafetyBlockListInfo
                {
                    Plcs = SafetyPlcs(info.PlcName).Select(plc => new PlcSafetyBlockPlcInfo
                    {
                        PlcName = plc.Software, DeviceName = plc.Device, Available = IsFailSafeCpu(plc),
                        Reason = IsFailSafeCpu(plc) ? null : "not an F-CPU",
                        TotalBlocks = IsFailSafeCpu(plc) ? 2 : 0, Offset = info.Offset ?? 0,
                        Blocks = IsFailSafeCpu(plc) ? new List<PlcSafetyBlockInfo>
                        {
                            new()
                            {
                                BlockPath = "PLC_1/Blocks/Safety_FB", Name = "Safety_FB", Number = 5, BlockType = "FB", ProgrammingLanguage = "F_LAD",
                                IsKnowHowProtected = false, SignatureProvider = true, SignatureState = PlcSafetyValueStates.Read,
                                Signatures = new() { new PlcSafetySignatureInfo { Type = "Block", Value = "3735928559", Hex = "0xDEADBEEF" } },
                            },
                            // D13 (live, V21, 2026-11-02): the provider is listed, GetService returns null; the listing goes on.
                            new()
                            {
                                BlockPath = "PLC_1/Blocks/SD/FB_Safety", Name = "FB_Safety", Number = 900, BlockType = "FB", ProgrammingLanguage = "F_FBD",
                                IsKnowHowProtected = false, SignatureProvider = true, SignatureState = PlcSafetyValueStates.Unreadable,
                                Problem = TiaMcpServer.Contracts.Shared.ListedServices.UnavailableReason(PlcSafetyApiCatalog.SafetySignatureProviderType) + " The F-signatures of this block are unavailable.",
                            },
                        } : new List<PlcSafetyBlockInfo>(),
                    }).ToList(),
                });
        }

        var plc = SafetyPlcs(info.PlcName).ToList() is { Count: 1 } single ? single[0]
            : Throw<Plc>(WorkerFailureCategories.TargetAmbiguous, "Several PLC software instances match. Specify an unambiguous plcName.");
        if (!IsFailSafeCpu(plc)) Throw(WorkerFailureCategories.TargetKindUnsupported, $"{request.Method}: PLC '{plc.Software}': not an F-CPU.");
        var result = new PlcSafetyWriteResultInfo
        {
            Operation = request.Method, PlcName = plc.Software, DeviceName = plc.Device,
            PasswordVariable = PlcSafetyMethods.TakesPassword(request.Method) ? info.PasswordVariable : null,
            PasswordSetBefore = _safetyPasswordSet, LoggedOnBefore = _safetyLoggedOn,
        };
        switch (request.Method)
        {
            case PlcSafetyMethods.Login:
                RequirePassword(info);
                _safetyLoggedOn = true;
                break;
            case PlcSafetyMethods.Logoff:
                _safetyLoggedOn = false;
                break;
            case PlcSafetyMethods.SetPassword:
                // Modelled as TIA Portal's own rejection (the server no longer refuses it).
                if (_safetyPasswordSet) throw PortalRejection("A safety offline program password is already set.");
                _ = Environment.GetEnvironmentVariable(info.PasswordVariable!) ?? Throw<string>(WorkerFailureCategories.ValidationError, "passwordVariable is not set.");
                _safetyPasswordSet = true;
                break;
            case PlcSafetyMethods.RevokePassword:
                RequirePassword(info);
                _safetyPasswordSet = false;
                _safetyLoggedOn = false;
                break;
            case PlcSafetyMethods.CreateRuntimeGroup:
                if (PlcSafetyPolicy.Refusal(PlcSafetyPolicy.Decide(true, _safetyPasswordSet, _safetyLoggedOn, false), plc.Software, "create_safety_runtime_group") is { } refusal)
                    Throw(WorkerFailureCategories.TargetKindUnsupported, refusal);
                _runtimeGroups[info.RuntimeGroupName!] = (LastSegment(info.MainSafetyBlockPath), LastSegment(info.MainSafetyInstanceDbPath));
                result.Overload = PlcSafetyPolicy.RuntimeGroupOverload(info.MainSafetyBlockPath is not null, info.MainSafetyInstanceDbPath is not null);
                result.RuntimeGroup = SafetyGroup(info.RuntimeGroupName!);
                break;
        }

        if (request.Method == PlcSafetyMethods.SetRuntimeGroup) SetRuntimeGroup(plc, info, result);
        result.PasswordSetAfter = _safetyPasswordSet;
        result.LoggedOnAfter = _safetyLoggedOn;
        return Ok(result);
    }

    /// <summary>The worker's gate for every F-block change, with the same Siemens-free rule (a login only when a password is set).</summary>
    private void RequireFailSafeImportPermitted(Plc plc, string block, string? content, string operation = "update_block_logic")
    {
        var language = UserBlocks(plc.Root, new List<string>()).Select(found => found.Group.Blocks.First(b => Names.Equals(b.Name, found.Block)))
            .FirstOrDefault(found => Names.Equals(found.Name, block)).Language;
        if (!PlcSafetyPolicy.IsFailSafeLanguage(language) && !PlcSafetyPolicy.DeclaresFailSafe(content)) return;
        var decision = PlcSafetyPolicy.Decide(IsFailSafeCpu(plc), _safetyPasswordSet, _safetyLoggedOn, false);
        if (PlcSafetyPolicy.Refusal(decision, plc.Software, $"{operation} of '{block}'") is { } refusal)
            Throw(WorkerFailureCategories.TargetKindUnsupported, $"'{block}' is a fail-safe block. {refusal} Nothing was changed.");
    }

    private static void RequirePassword(PlcSafetyRequestInfo info)
    {
        // The worker's shared resolver: environment variables, or Windows generic credentials (unsupported off Windows).
        using var secure = TiaMcpServer.Contracts.Shared.PasswordSources.Resolve(info.PasswordVariable, Environment.GetEnvironmentVariable, null, out var reason);
        if (secure is null)
            Throw(reason == TiaMcpServer.Contracts.Shared.WindowsCredentialReader.UnsupportedReason ? WorkerFailureCategories.UnsupportedCapability : WorkerFailureCategories.ValidationError, reason ?? "No password.");
        var pointer = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secure!);
        string value;
        try { value = System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer)!; }
        finally { System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        // A wrong password is the Siemens call's own rejection.
        if (value != Password) Throw(WorkerFailureCategories.EngineeringRejected, "The password is not correct.");
    }

    private IEnumerable<Plc> SafetyPlcs(string? plcName)
    {
        var found = _plcs.Where(plc => plcName is null || Matches(plc, plcName)).ToList();
        return found.Count == 0 && plcName is not null ? Throw<List<Plc>>(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.") : found;
    }

    private PlcSafetyCpuInfo Cpu(Plc plc)
    {
        if (!IsFailSafeCpu(plc))
            return new PlcSafetyCpuInfo { PlcName = plc.Software, DeviceName = plc.Device, Available = false, Reason = "The CPU device item offers no SafetyAdministration (fake standard CPU)." };
        return new PlcSafetyCpuInfo
        {
            PlcName = plc.Software, DeviceName = plc.Device, Available = true,
            PasswordSet = _safetyPasswordSet, LoggedOn = _safetyLoggedOn,
            Access =
            {
                new PlcSafetyValueInfo { Name = PlcSafetyApiCatalog.IsPasswordSet, Value = _safetyPasswordSet ? "true" : "false", ValueType = "System.Boolean" },
                new PlcSafetyValueInfo { Name = PlcSafetyApiCatalog.IsLoggedOn, Value = _safetyLoggedOn ? "true" : "false", ValueType = "System.Boolean" },
            },
            Settings = { new PlcSafetyValueInfo { Name = PlcSafetyApiCatalog.SafetySystemVersion, Value = "V2.6", ValueType = "Siemens.Engineering.Safety.SafetySystemVersion" } },
            ApplicableSafetySystemVersions = new() { "V2.5", "V2.6" },
            RuntimeGroups = _runtimeGroups.Keys.Select(SafetyGroup).ToList(),
            ProgramSignatures = new() { new PlcSafetySignatureInfo { Type = "Collective", Value = "305419896", Hex = "0x12345678" } },
            SafetyServices = { PlcSafetyApiCatalog.SafetyAdministrationType },
        };
    }

    private PlcSafetyRuntimeGroupInfo SafetyGroup(string name) => new()
    {
        Name = name,
        Attributes =
        {
            new PlcSafetyValueInfo { Name = PlcSafetyRuntimeGroupBinding.MainSafetyBlockName, Value = _runtimeGroups[name].Main, ValueType = _runtimeGroups[name].Main is null ? null : "System.String" },
            new PlcSafetyValueInfo { Name = PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdbName, Value = _runtimeGroups[name].Idb, ValueType = _runtimeGroups[name].Idb is null ? null : "System.String" },
            new PlcSafetyValueInfo { Name = "MaximumCycleTime", Value = "200", ValueType = "System.Int32" },
        },
        // Modelled on the live L2 finding (V21, 2026-11-02): no attribute MainSafetyBlock is listed; which access modes TIA
        // Portal reports for these names is not known, so the model lists the attributes it reads as Read.
        AttributeInfos = new()
        {
            new PlcSafetyAttributeAccessInfo { Name = "Name", AccessMode = "Read" },
            new PlcSafetyAttributeAccessInfo { Name = PlcSafetyRuntimeGroupBinding.MainSafetyBlockName, AccessMode = "Read" },
            new PlcSafetyAttributeAccessInfo { Name = PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdbName, AccessMode = "Read" },
        },
    };

    private static string? LastSegment(string? path) => path is null ? null : path[(path.LastIndexOf('/') + 1)..];

    /// <summary>set_safety_runtime_group: the worker's rule (login, existing group, existing blocks, bound member) with names as the read-back.</summary>
    private void SetRuntimeGroup(Plc plc, PlcSafetyRequestInfo info, PlcSafetyWriteResultInfo result)
    {
        if (PlcSafetyPolicy.Refusal(PlcSafetyPolicy.Decide(true, _safetyPasswordSet, _safetyLoggedOn, false), plc.Software, "set_safety_runtime_group") is { } refusal)
            Throw(WorkerFailureCategories.TargetKindUnsupported, refusal + " Nothing was changed.");
        var name = info.RuntimeGroupName!;
        if (!_runtimeGroups.TryGetValue(name, out var current))
            Throw(WorkerFailureCategories.TargetNotFound, $"set_safety_runtime_group: PLC '{plc.Software}' has no F-runtime group named '{name}'. Nothing was changed.");
        var requested = new List<(string Member, string Path)> { (PlcSafetyRuntimeGroupBinding.MainSafetyBlock, info.MainSafetyBlockPath!) };
        if (info.MainSafetyInstanceDbPath is { } idb) requested.Add((PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb, idb));
        foreach (var (_, path) in requested)
            if (!AllBlocks(plc.Root).Any(b => Names.Equals(b.Name, LastSegment(path))))
                Throw(WorkerFailureCategories.TargetNotFound, $"set_safety_runtime_group: '{path}' was not found. Nothing was changed.");
        if (noRelink)
            Throw(WorkerFailureCategories.ApiMemberMissing, "api_member_missing: Siemens.Engineering.Safety.RuntimeGroup.MainSafetyBlock: no public property 'MainSafetyBlock' and GetAttributeInfos() lists no attribute 'MainSafetyBlock' "
                + $"and '{PlcSafetyRuntimeGroupBinding.MainSafetyBlockName}' as not writable (Read); the attributes GetAttributeInfos() reported: "
                + string.Join(", ", SafetyGroup(name).AttributeInfos!.Select(i => $"{i.Name} ({i.AccessMode})")) + " (fake, as live 2026-11-02). Nothing was changed.");
        result.Assignments = new List<PlcSafetyRuntimeGroupAssignmentInfo>();
        foreach (var (member, path) in requested)
        {
            var block = LastSegment(path)!;
            var before = member == PlcSafetyRuntimeGroupBinding.MainSafetyBlock ? current.Main : current.Idb;
            current = member == PlcSafetyRuntimeGroupBinding.MainSafetyBlock ? (block, current.Idb) : (current.Main, block);
            result.Assignments.Add(new PlcSafetyRuntimeGroupAssignmentInfo
            {
                Member = member, BlockPath = path, BlockName = block, Route = PlcSafetyRuntimeGroupBinding.Routes.SetAttribute,
                Signature = $"IEngineeringObject.SetAttribute(\"{member}\", PlcBlock)", Before = before, After = block,
            });
        }

        _runtimeGroups[name] = current;
        result.RuntimeGroup = SafetyGroup(name);
    }

    /// <summary>
    /// After a SIMATIC SD import of <paramref name="block"/>: drops the main safety block references that name it, as TIA
    /// Portal V21 did live (E7), then runs the worker's protection (restore, or the warning with the repair under noRelink).
    /// </summary>
    private List<string> DropAndRestoreMainSafetyReferences(Plc plc, string block, string blockPath)
    {
        if (!IsFailSafeCpu(plc)) return new List<string>();
        var before = _runtimeGroups.Select(g => new PlcMainSafetyLink(g.Key, g.Value.Main, g.Value.Idb)).ToList();
        var affected = PlcMainSafetyLinks.Affected(before, block);
        foreach (var link in affected)
            if (PlcMainSafetyLinks.Same(link.MainSafetyBlockName, block)) _runtimeGroups[link.RuntimeGroup] = (null, _runtimeGroups[link.RuntimeGroup].Idb);
        var outcomes = PlcMainSafetyLinks.Reconcile(affected, block,
            group => _runtimeGroups.TryGetValue(group, out var names) ? new PlcMainSafetyLink(group, names.Main, names.Idb) : null,
            (group, member, expected) =>
            {
                if (noRelink) throw new PlcSafetyApiMissingException($"Siemens.Engineering.Safety.RuntimeGroup.{member}", "no public property and no writable attribute (fake)");
                _runtimeGroups[group] = member == PlcSafetyRuntimeGroupBinding.MainSafetyBlock ? (expected, _runtimeGroups[group].Idb) : (_runtimeGroups[group].Main, expected);
                return ($"IEngineeringObject.SetAttribute(\"{member}\", PlcBlock)", expected);
            },
            _ => false);
        return outcomes.Where(o => o.Lost).Select(o => PlcMainSafetyLinks.Warning(o, plc.Software, blockPath)).ToList();
    }
}
