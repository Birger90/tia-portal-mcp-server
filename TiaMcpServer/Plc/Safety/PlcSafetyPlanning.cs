using System.Text.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The fail-safe part of plc_write planning, one instance per planning pass. It reads <c>get_safety_administration</c>
/// at most once per PLC and only for items that need it (a fail-safe write, or an F-block change found in the content),
/// and applies earlier fail-safe items of the call to a planned access state so that, for example, a
/// <c>safety_login</c> followed by an F-block update in the same call plans as <see cref="PlcSafetyEditPermission.PendingEarlierLogin"/>.
/// The worker re-checks the real state right before every F-program change, so the planned state never authorizes
/// anything by itself. Every fail-safe item fires <see cref="PlcGuardDefinitions.SafetyAcceptance"/>.
/// </summary>
internal sealed class PlcSafetyPlanning(OpennessWorkerClient client, Dictionary<string, PlcMainSafetyLinkPlan>? links = null)
{
    public const string AdministrationKind = "SafetyAdministration";
    public const string RuntimeGroupKind = "SafetyRuntimeGroup";

    private sealed class Planned
    {
        public PlcSafetyCpuInfo? Cpu { get; init; }
        public WriteToolError? Error { get; init; }
        public bool? PasswordSet { get; set; }
        public bool? LoggedOn { get; set; }
        public bool LoggedOnByEarlierItem { get; set; }
        public HashSet<string> RuntimeGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The import routes that replace a block (worker method update_block_logic, or create_block_from_document with
    /// overwrite): their F-runtime group references are recorded for the item's verification (check safetyRuntimeGroup).
    /// </summary>
    internal static bool ImportsBlock(PlcOperationRequest item)
        => item.Operation is "update_block_logic" || item.Operation == SimaticSdMethods.CreateBlockFromDocument && item.Overwrite == true
            || PlcBlockEditMethods.PlannedEdits.Contains(item.Operation, StringComparer.Ordinal) && item.TypePath is null;

    /// <summary>
    /// The import routes that use SIMATIC SD <c>ImportFromDocuments</c> (live 2026-11-02, E7/L1: only this route dropped the
    /// main safety block reference; the SimaticML route kept it, E7b/L3): update_block_logic format sd (or a bundle without
    /// .xml, which takes that route), the network edits and create_block_from_document. Onto a main safety block the worker
    /// routes them through a temporary F-block and imports SimaticML instead (<see cref="PlcMainSafetyBlockSdRoute"/>).
    /// </summary>
    internal static bool ImportsThroughSimaticSd(PlcOperationRequest item)
    {
        if (item.Operation == SimaticSdMethods.CreateBlockFromDocument || PlcBlockEditMethods.NetworkEdits.Contains(item.Operation, StringComparer.Ordinal)) return true;
        if (item.Operation != "update_block_logic") return false;
        if (PlcFormatNames.Normalize(item.Operation, item.Format) == Contracts.Block.SourceFormatNames.Sd) return true;
        var documents = PlcBlockBundleText.Documents(item.Content ?? string.Empty);
        return PlcFormatNames.Normalize(item.Operation, item.Format) == Contracts.Block.SourceFormatNames.Xml && documents.Count > 0
            && !documents.Any(d => d.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            && documents.Any(d => d.Key.EndsWith(SimaticSdText.BundleExtension, StringComparison.OrdinalIgnoreCase));
    }

    private readonly Dictionary<string, Planned> _plcs = new(PlcNameRules.Comparer);

    public static bool Handles(PlcOperationRequest item) => PlcSafetyMethods.IsWrite(item.Operation);

    /// <summary>Plans one fail-safe write against the planned access state of its PLC, then applies it to that state.</summary>
    public async Task<(PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error)> ResolveAsync(PlcOperationRequest item, PlcWriteEffect effect)
    {
        var plcName = effect.Target.PlcName;
        var planned = await StateAsync(plcName, effect.Target.DeviceName).ConfigureAwait(false);
        if (planned.Error is { } readError) return (null, Array.Empty<FiredGuard>(), Prefixed(item, readError));
        var cpu = planned.Cpu!;
        if (!cpu.Available)
            return (null, Array.Empty<FiredGuard>(), new(WorkerFailureCategories.TargetKindUnsupported,
                $"Operation '{item.OperationId}': {item.Operation}: PLC '{plcName}' has no safety administration: {cpu.Reason}"));
        var guards = new List<FiredGuard> { Acceptance(item, plcName) };
        var changes = new List<PlcFieldChange>();
        string? Text(bool? value) => value switch { true => "true", false => "false", _ => null };
        WriteToolError? Invalid(string message) => new(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {message} Nothing was changed.");
        if (planned.PasswordSet is null || planned.LoggedOn is null)
            guards.Add(new FiredGuard(PlcGuardDefinitions.SafetyTiaDecides, item.OperationId,
                $"Operation '{item.OperationId}': the safety access state of PLC '{plcName}' (password set, logged on) could not be read ({string.Join("; ", cpu.Problems)}); "
                + $"{item.Operation} is passed to TIA Portal, which decides."));
        switch (item.Operation)
        {
            case PlcSafetyMethods.Login:
                if (planned.PasswordSet == false) guards.Add(TiaDecides(item, $"safety_login: PLC '{plcName}' has no safety offline program password in the planned state, so there may be nothing to log on to"));
                changes.Add(new("loggedOn", Text(planned.LoggedOn), "true"));
                if (planned.LoggedOn != true) planned.LoggedOnByEarlierItem = true;
                planned.LoggedOn = true;
                break;
            case PlcSafetyMethods.Logoff:
                changes.Add(new("loggedOn", Text(planned.LoggedOn), "false"));
                planned.LoggedOn = false;
                planned.LoggedOnByEarlierItem = false;
                break;
            case PlcSafetyMethods.SetPassword:
                if (planned.PasswordSet == true) guards.Add(TiaDecides(item, $"set_safety_password: PLC '{plcName}' already has a safety offline program password in the planned state"));
                changes.Add(new("passwordSet", Text(planned.PasswordSet), "true"));
                planned.PasswordSet = true;
                break;
            case PlcSafetyMethods.RevokePassword:
                if (planned.PasswordSet == false) guards.Add(TiaDecides(item, $"revoke_safety_password: PLC '{plcName}' has no safety offline program password in the planned state"));
                changes.Add(new("passwordSet", Text(planned.PasswordSet), "false"));
                planned.PasswordSet = false;
                break;
            case PlcSafetyMethods.SetRuntimeGroup:
            {
                var name = item.RuntimeGroupName!;
                if (!planned.RuntimeGroups.Contains(name))
                    return (null, guards, new(WorkerFailureCategories.TargetNotFound,
                        $"Operation '{item.OperationId}': set_safety_runtime_group: PLC '{plcName}' has no F-runtime group named '{name}' (nor does an earlier item of the call create one). Nothing was changed."));
                var permission = Permission(planned);
                if (PlcSafetyPolicy.Refusal(permission, plcName, $"Operation '{item.OperationId}' (set_safety_runtime_group)") is { } refusal)
                    guards.Add(new FiredGuard(PlcGuardDefinitions.SafetyLoginRequired, item.OperationId, refusal));
                var current = cpu.RuntimeGroups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                string? Attribute(string attribute) => current?.Attributes.FirstOrDefault(a => a.Name == attribute && a.State == PlcSafetyValueStates.Read)?.Value;
                changes.Add(new("runtimeGroup", name, name));
                changes.Add(new("mainSafetyBlock", Attribute(PlcSafetyRuntimeGroupBinding.MainSafetyBlockName), item.MainSafetyBlockPath));
                if (item.MainSafetyInstanceDbPath is not null)
                    changes.Add(new("mainSafetyInstanceDb", Attribute(PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdbName), item.MainSafetyInstanceDbPath));
                break;
            }
            case PlcSafetyMethods.CreateRuntimeGroup:
            {
                var name = item.RuntimeGroupName!;
                if (planned.RuntimeGroups.Contains(name)) return (null, guards, Invalid($"create_safety_runtime_group: PLC '{plcName}' already has an F-runtime group named '{name}'."));
                var permission = Permission(planned);
                if (PlcSafetyPolicy.Refusal(permission, plcName, $"Operation '{item.OperationId}' (create_safety_runtime_group)") is { } refusal)
                    guards.Add(new FiredGuard(PlcGuardDefinitions.SafetyLoginRequired, item.OperationId, refusal));
                // An unreadable state already fired the info guard above; no further note.
                changes.Add(new("runtimeGroup", null, name));
                changes.Add(new("overload", null, PlcSafetyPolicy.RuntimeGroupOverload(item.MainSafetyBlockPath is not null, item.MainSafetyInstanceDbPath is not null)));
                if (item.MainSafetyBlockPath is not null) changes.Add(new("mainSafetyBlock", null, item.MainSafetyBlockPath));
                if (item.MainSafetyInstanceDbPath is not null) changes.Add(new("mainSafetyInstanceDb", null, item.MainSafetyInstanceDbPath));
                planned.RuntimeGroups.Add(name);
                break;
            }
        }

        if (PlcSafetyMethods.TakesPassword(item.Operation)) changes.Add(new("passwordVariable", null, item.PasswordVariable));
        return (effect with { Changes = changes }, guards, null);
    }

    /// <summary>
    /// The guards of an F-block change on <paramref name="plcName"/>: always the acceptance guard; the login block guard
    /// only when a password is set and the planned state is not logged on (TIA Portal's own rule); the info guard
    /// <see cref="PlcGuardDefinitions.SafetyTiaDecides"/> when the state cannot be judged (TIA Portal decides then).
    /// </summary>
    public async Task<(IReadOnlyList<FiredGuard> Guards, string? Refusal)> FailSafeEditAsync(PlcOperationRequest item, PlcTargetIdentity target, string what)
    {
        var plcName = target.PlcName;
        var planned = await StateAsync(plcName, target.DeviceName).ConfigureAwait(false);
        var permission = planned.Error is not null ? PlcSafetyEditPermission.Unverifiable : Permission(planned);
        var refusal = PlcSafetyPolicy.Refusal(permission, plcName, what);
        var guards = new List<FiredGuard> { Acceptance(item, plcName) };
        if (refusal is not null) guards.Add(new FiredGuard(PlcGuardDefinitions.SafetyLoginRequired, item.OperationId, refusal));
        if (PlcSafetyPolicy.Undetermined(permission, plcName, what) is { } note)
            guards.Add(new FiredGuard(PlcGuardDefinitions.SafetyTiaDecides, item.OperationId, planned.Error is { } error ? $"{note} ({error.Message})" : note));
        if (ImportsBlock(item) && planned.Cpu is { } cpu && MainSafetyLinks(cpu, BlockName(item, target)) is { Count: > 0 } found)
        {
            // Recorded for the verification of every import route; the planning note only for SIMATIC SD imports (E7, E7b).
            if (links is not null) links[item.OperationId] = new PlcMainSafetyLinkPlan(target.PlcName, target.DeviceName, BlockName(item, target)!, found);
            if (ImportsThroughSimaticSd(item))
            {
                // A main safety block (MainSafetyBlockName) is routed through a temporary F-block and imported as SimaticML
                // (live 2026-11-02, L1-L4); an instance DB alone keeps the direct SD import and the re-link attempt.
                var path = target.BlockPath ?? item.BlockPath ?? BlockName(item, target)!;
                var mainGroups = PlcMainSafetyBlockSdRoute.MainBlockGroups(found, BlockName(item, target)!);
                guards.Add(new FiredGuard(PlcGuardDefinitions.ImportMayUnlinkMainSafetyBlock, item.OperationId, mainGroups.Count > 0
                    ? PlcMainSafetyBlockSdRoute.PlanningNote(item.OperationId, path, mainGroups)
                    : PlcMainSafetyLinks.PlanningNote(item.OperationId, path, found, simaticSdRoute: true)));
            }
        }

        return (guards, refusal);
    }

    /// <summary>
    /// Whether the PLC is an F-CPU by its planning read of <c>get_safety_administration</c> (read at most once per PLC): true
    /// when it offers a safety administration, false when it reads as having none, null when the read failed (unknown).
    /// </summary>
    public async Task<bool?> IsFailSafeCpuAsync(string plcName, string? deviceName)
    {
        var planned = await StateAsync(plcName, deviceName).ConfigureAwait(false);
        return planned.Error is not null || planned.Cpu is null ? null : planned.Cpu.Available;
    }

    /// <summary>The name of the block an import replaces: the target's, or the name the create_block_from_document document declares.</summary>
    private static string? BlockName(PlcOperationRequest item, PlcTargetIdentity target)
        => item.Operation == SimaticSdMethods.CreateBlockFromDocument
            ? SimaticSdBlockDocument.TryCheck(item.Content ?? string.Empty, out _)?.BlockName ?? target.Name
            : target.Name ?? (item.BlockPath is { } path ? path[(path.LastIndexOf('/') + 1)..] : null);

    /// <summary>The F-runtime groups of the planning read whose main safety block or its instance DB is <paramref name="blockName"/>.</summary>
    internal static IReadOnlyList<PlcMainSafetyLink> MainSafetyLinks(PlcSafetyCpuInfo cpu, string? blockName)
        => blockName is null ? Array.Empty<PlcMainSafetyLink>() : PlcMainSafetyLinks.Affected(cpu.RuntimeGroups.Select(Link), blockName);

    /// <summary>One runtime group of a get_safety_administration read as a link (unreadable names are null).</summary>
    internal static PlcMainSafetyLink Link(PlcSafetyRuntimeGroupInfo group)
    {
        string? Value(string name) => group.Attributes.FirstOrDefault(a => a.Name == name && a.State == PlcSafetyValueStates.Read)?.Value;
        return new PlcMainSafetyLink(group.Name ?? string.Empty, Value(PlcSafetyRuntimeGroupBinding.MainSafetyBlockName), Value(PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdbName));
    }

    /// <summary>A planned-state mismatch this server does not refuse: TIA Portal decides (owner decision, never stricter than TIA Portal).</summary>
    private static FiredGuard TiaDecides(PlcOperationRequest item, string what)
        => new(PlcGuardDefinitions.SafetyTiaDecides, item.OperationId, $"Operation '{item.OperationId}': {what}; the call is passed to TIA Portal, which decides.");

    public static FiredGuard Acceptance(PlcOperationRequest item, string plcName)
        => new(PlcGuardDefinitions.SafetyAcceptance, item.OperationId,
            $"Operation '{item.OperationId}' ({item.Operation}) changes the safety program or the safety access protection of PLC '{plcName}'. {PlcSafetyPolicy.AcceptanceNotice}");

    private static PlcSafetyEditPermission Permission(Planned planned)
        => PlcSafetyPolicy.Decide(planned.Cpu?.Available == true, planned.PasswordSet, planned.LoggedOn == true && planned.LoggedOnByEarlierItem ? false : planned.LoggedOn,
            planned.LoggedOnByEarlierItem);

    /// <summary>The planned state of the PLC with exactly this software and device name (a name alone can match a device of another PLC).</summary>
    private async Task<Planned> StateAsync(string plcName, string? deviceName)
    {
        var key = plcName + "\n" + deviceName;
        if (_plcs.TryGetValue(key, out var known)) return known;
        var call = await client.PlcSafetyAsync(PlcSafetyMethods.GetAdministration, new PlcSafetyRequestInfo { PlcName = plcName }).ConfigureAwait(false);
        Planned planned;
        if (!call.Success)
        {
            planned = new Planned { Error = new(call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, $"The safety administration of PLC '{plcName}' could not be read: {call.Error}") };
        }
        else
        {
            try
            {
                var matches = PlcSafetyContract.Administration(call.Payload).Plcs
                    .Where(p => PlcNameRules.Comparer.Equals(p.PlcName, plcName) && (deviceName is null || PlcNameRules.Comparer.Equals(p.DeviceName, deviceName))).ToList();
                planned = matches.Count == 1
                    ? new Planned { Cpu = matches[0], PasswordSet = matches[0].PasswordSet, LoggedOn = matches[0].LoggedOn }
                    : new Planned { Error = new(WorkerFailureCategories.StateChanged, $"The safety administration read does not describe PLC '{plcName}' exactly once.") };
                foreach (var group in matches.Count == 1 ? matches[0].RuntimeGroups : new List<PlcSafetyRuntimeGroupInfo>())
                    if (group.Name is not null) planned.RuntimeGroups.Add(group.Name);
            }
            catch (JsonException)
            {
                planned = new Planned { Error = new(WorkerFailureCategories.ProtocolError, "The worker's safety administration payload did not match its declared contract and was rejected.") };
            }
        }

        _plcs[key] = planned;
        return planned;
    }

    private static WriteToolError Prefixed(PlcOperationRequest item, WriteToolError error)
        => error with { Message = $"Operation '{item.OperationId}': {error.Message}" };
}

/// <summary>The F-runtime groups an import item's block was the main safety block (or IDB) of at planning, for the verification.</summary>
internal sealed record PlcMainSafetyLinkPlan(string PlcName, string? DeviceName, string BlockName, IReadOnlyList<PlcMainSafetyLink> Links);
