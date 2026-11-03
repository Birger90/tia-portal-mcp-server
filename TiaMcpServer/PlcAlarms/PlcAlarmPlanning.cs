using System.Text.Json;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcAlarms;

/// <summary>
/// plc_write planning evidence of the alarm, text list, supervision and ProDiag items that the working state cannot know.
/// Every item first runs the worker's non-mutating <c>plc_alarm_plan</c>: it resolves the PLC, FB or tag, binds every
/// Siemens member the item will call (fail closed before anything runs, dryRun included) and reports the caller path as
/// the file system sees it. The host's own path check is lexical (the catalog); existence, overwrite, network drives and
/// links come from that evidence. A cell update then reads the same workbook once to check the workbook hash and every
/// edited cell's expected value. The worker repeats every check at mutation, so a change between planning and mutation
/// is still refused.
/// </summary>
internal static class PlcAlarmPlanning
{
    public sealed record Outcome(PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, IReadOnlyList<CheckedPrecondition> Preconditions, WriteToolError? Error)
    {
        public static Outcome Fail(string category, string message) => new(null, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), new WriteToolError(category, message));
    }

    /// <summary>
    /// The project-wide alarm-class items: no PLC is resolved; plcName is empty in the effect. Their capability rows are
    /// standalone-only, so a bound local session is refused here already (the worker gate would refuse the mutation with
    /// the same unsupported_capability), and a dryRun says so.
    /// </summary>
    public static async Task<Outcome> ResolveProjectAsync(OpennessWorkerClient client, PlcOperationRequest item)
    {
        var binding = client.BindingSnapshot;
        var diagnostics = PlcAlarmMethods.IsSystemDiagnostics(item.Operation);
        if (binding.IsVerified && binding.Context is { } context && !ProjectCapabilityCatalog.Supports(context.ContainerKind, item.Operation))
            return Outcome.Fail(WorkerFailureCategories.UnsupportedCapability,
                $"Operation '{item.OperationId}': {item.Operation} is unavailable for this project container; "
                + (diagnostics
                    ? "the system diagnostics settings are reached through the services of a standalone project."
                    : "the common alarm classes are reached through Project.GetService<AlarmClassDataProvider>() of a standalone project."));
        var target = new PlcTargetIdentity(diagnostics ? PlcWorkingState.SystemDiagnosticsSettingsKind : PlcWorkingState.AlarmClassesKind,
            string.Empty, null, null, null, null, null, null);
        var guards = new List<FiredGuard>();
        var imports = PlcAlarmMethods.FileImports.Contains(item.Operation);
        var effect = imports
            ? new PlcWriteEffect(item.Operation, target, new[] { new PlcFieldChange("filePath", null, item.FilePath) }, null, null, null, null)
            : new PlcWriteEffect(item.Operation, target, Array.Empty<PlcFieldChange>(), item.FilePath, null, null, null);
        if (imports)
            guards.Add(Fire(PlcGuardDefinitions.ImportsAlarmData, item, $"{item.Operation} imports a private copy of '{item.FilePath}' into the project's "
                + (diagnostics ? "system diagnostics settings" : "common alarm classes")
                + "; what TIA Portal's import replaces, adds or keeps is decided by TIA Portal; a re-export right after it is compared with the file byte for byte."));
        return await PlanAsync(client, item, effect, guards).ConfigureAwait(false);
    }

    /// <summary>
    /// The worker plan of every PLC-scoped alarm item and, for cell updates, the workbook probe. When an earlier item of the
    /// call already writes to the same PLC (<paramref name="afterEarlierWrites"/>), a plan refusal that such an item can cause
    /// (a list it creates, renames or deletes, a block it creates) does not fail planning: the working state already planned
    /// the item against the call's effects, and the worker repeats every check at mutation (and re-plans dependent items).
    /// </summary>
    public static async Task<Outcome> ResolveAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, IReadOnlyList<FiredGuard> guards,
        bool afterEarlierWrites = false)
    {
        var planned = await PlanAsync(client, item, effect, guards.ToList(), afterEarlierWrites).ConfigureAwait(false);
        if (planned.Error is not null || !PlcAlarmMethods.CellUpdates.Contains(item.Operation)) return planned;
        return await ResolveCellsAsync(client, item, planned.Effect!, planned.Guards).ConfigureAwait(false);
    }

    private static async Task<Outcome> PlanAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, List<FiredGuard> guards,
        bool afterEarlierWrites = false)
    {
        var call = await client.PlcAlarmAsync(PlcAlarmMethods.Plan, PlcAlarmRequests.Plan(item)).ConfigureAwait(false);
        if (!call.Success && afterEarlierWrites && call.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous
                or WorkerFailureCategories.ValidationError or WorkerFailureCategories.TargetKindUnsupported)
            return new Outcome(effect, guards, Array.Empty<CheckedPrecondition>(), null);
        if (!call.Success)
            return Outcome.Fail(call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, $"Operation '{item.OperationId}': {call.Error}");
        PlcAlarmPlanInfo plan;
        try
        {
            plan = PlcAlarmPayloadContract.DecodePlan(item.Operation, call.Payload);
        }
        catch (JsonException)
        {
            return Outcome.Fail(WorkerFailureCategories.ProtocolError, $"Operation '{item.OperationId}': the planning evidence did not match its declared result contract and was rejected.");
        }

        if (item.Operation == PlcAlarmMethods.SetProDiagAssignment)
            effect = effect with { Changes = new[] { new PlcFieldChange("assignedProDiagFb", plan.AssignedProDiagFb, item.ProDiagFb) } };
        if (plan.Path is { } path && CheckPath(item, path, guards) is { } refusal) return refusal;
        return new Outcome(effect, guards, Array.Empty<CheckedPrecondition>(), null);
    }

    /// <summary>The caller path as the worker's file system sees it: existence and overwrite rules, then network drives and links (info).</summary>
    private static Outcome? CheckPath(PlcOperationRequest item, PlcAlarmPathInfo path, List<FiredGuard> guards)
    {
        var shown = item.FilePath ?? item.DirectoryPath;
        if (PlcAlarmMethods.FileImports.Contains(item.Operation))
        {
            if (path.IsDirectory) return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': '{shown}' is a directory.");
            if (!path.Exists) return Outcome.Fail(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': the file '{shown}' does not exist.");
        }
        else if (item.Operation == PlcAlarmMethods.ExportProDiagInfo)
        {
            if (!path.ParentExists)
                return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': the parent directory of '{shown}' does not exist. Nothing was written.");
            if (path.Exists)
                return Outcome.Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': '{shown}' already exists; export_prodiag_info creates a new directory and never merges into or replaces one. Nothing was written.");
        }
        else
        {
            if (!path.ParentExists)
                return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': the directory of '{shown}' does not exist. Nothing was written.");
            if (path.IsDirectory) return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': '{shown}' is a directory.");
            if (path.Exists)
            {
                if (item.Overwrite != true)
                    return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': '{shown}' already exists; pass overwrite:true to replace it. Nothing was written.");
                if (path.ReparsePoint)
                    return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': '{shown}' is a link (reparse point); it is not replaced. Nothing was written.");
                guards.Add(Fire(PlcGuardDefinitions.ExportOverwritesFile, item, $"{item.Operation} replaces the existing file '{shown}' (overwrite:true)."));
            }
        }

        var notes = PlcAlarmFiles.Notes(path);
        if (notes.Count > 0) guards.Add(Fire(PlcGuardDefinitions.FileOnNetworkOrLink, item, string.Join(" ", notes)));
        return null;
    }

    private static async Task<Outcome> ResolveCellsAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, IReadOnlyList<FiredGuard> guards)
    {
        var read = PlcAlarmMethods.WorkbookReadFor(item.Operation)!;
        var call = await client.PlcAlarmAsync(read, PlcAlarmRequests.WorkbookProbe(item)).ConfigureAwait(false);
        if (!call.Success && call.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous
                or WorkerFailureCategories.ValidationError or WorkerFailureCategories.TargetKindUnsupported or WorkerFailureCategories.UnsupportedCapability)
            return Outcome.Fail(call.FailureCategory!, $"Operation '{item.OperationId}': {call.Error}");
        if (!call.Success)
        {
            // An unreadable workbook is never treated as matching: the call is blocked.
            var unverifiable = Fire(PlcGuardDefinitions.StateUnverifiable, item,
                $"the workbook could not be exported with {read} ({call.Error}), so expectedWorkbookHash and the expected cell values cannot be checked.");
            return new Outcome(effect, guards.Append(unverifiable).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        PlcAlarmWorkbookInfo workbook;
        try
        {
            workbook = PlcAlarmPayloadContract.DecodeWorkbook(read, call.Payload);
        }
        catch (JsonException)
        {
            return Outcome.Fail(WorkerFailureCategories.ProtocolError, $"Operation '{item.OperationId}': the workbook read did not match its declared result contract and was rejected.");
        }

        var precondition = new CheckedPrecondition("workbookHash", item.ExpectedWorkbookHash!, workbook.WorkbookHash,
            string.Equals(item.ExpectedWorkbookHash, workbook.WorkbookHash, StringComparison.Ordinal));
        if (!precondition.Satisfied)
            return Outcome.Fail(WorkerFailureCategories.StateChanged,
                $"Operation '{item.OperationId}': the workbook changed since it was read (now {workbook.WorkbookHash}). Read it again with {read}.");

        var changes = new List<PlcFieldChange>();
        foreach (var edit in item.CellEdits!)
        {
            var probe = workbook.Probes.FirstOrDefault(p => p.Sheet == edit.Sheet && p.Cell == edit.Cell);
            var where = $"{edit.Sheet}!{edit.Cell}";
            if (probe is null || !probe.SheetExists)
                return Outcome.Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': the workbook has no worksheet '{edit.Sheet}' (worksheets: {string.Join(", ", workbook.Sheets.Select(s => $"'{s.Name}'"))}).");
            if (probe.Kind is PlcAlarmPolicy.CellKinds.Formula or PlcAlarmPolicy.CellKinds.Error or PlcAlarmPolicy.CellKinds.Boolean or "date")
                return Outcome.Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': cell {where} is a {probe.Kind} cell; only text and number cells are edited.");
            if (!string.Equals(probe.Value, edit.Expected, StringComparison.Ordinal))
                return Outcome.Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': cell {where} holds {Quote(probe.Value)}, not the expected {Quote(edit.Expected!)}.");
            if (edit.Kind is null && probe.Kind == PlcAlarmPolicy.CellKinds.Number && !PlcAlarmPolicy.IsNumber(edit.Value))
                return Outcome.Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': cell {where} is a number cell and '{edit.Value}' is not an invariant number; pass kind string to write text.");
            changes.Add(new PlcFieldChange(where, PlcAlarmPolicy.Bound(probe.Exists ? probe.Value : null, 1000), PlcAlarmPolicy.Bound(edit.Value, 1000)));
        }

        return new Outcome(effect with { Changes = changes }, guards, new[] { precondition }, null);
    }

    private static string Quote(string value) => value.Length == 0 ? "nothing (empty)" : "'" + PlcAlarmPolicy.Bound(value, 200) + "'";

    private static FiredGuard Fire(string id, PlcOperationRequest item, string message) => new(id, item.OperationId, $"Operation '{item.OperationId}': {message}");

    /// <summary>The PLC's text list inventory, read once per call for the PLCs whose items need it.</summary>
    public static async Task<(PlcTextListPlcInfo? Lists, WriteToolError? Error)> ReadTextListsAsync(OpennessWorkerClient client, PlcTagInventoryPlcInfo plc)
    {
        var call = await client.PlcAlarmAsync(PlcAlarmMethods.ListTextLists, new PlcAlarmRequestInfo { PlcName = plc.PlcName }).ConfigureAwait(false);
        if (!call.Success)
            return (null, new(call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, $"The text lists of PLC '{plc.PlcName}' could not be read: {call.Error}"));
        try
        {
            var inventory = PlcAlarmPayloadContract.DecodeInventory(call.Payload);
            var match = inventory.Plcs.Where(p => p.PlcName == plc.PlcName && p.DeviceName == plc.DeviceName).ToList();
            return match.Count == 1 ? (match[0], null)
                : (null, new(WorkerFailureCategories.StateChanged, $"The text list inventory does not describe PLC '{plc.PlcName}' on device '{plc.DeviceName}' exactly once."));
        }
        catch (JsonException)
        {
            return (null, new(WorkerFailureCategories.ProtocolError, "The text list inventory did not match its declared result contract and was rejected."));
        }
    }
}
