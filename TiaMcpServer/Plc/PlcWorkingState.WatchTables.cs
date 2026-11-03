using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The watch/force table and update_program part of the planner's working state. Watch tables come from
/// one <c>list_watch_and_force_tables</c> read of the PLC, only when an item needs it
/// (<see cref="NeedsWatchTables"/>). Like tags, update and delete targets are resolved (missing or
/// ambiguous is an error) and creates are never checked against existing state: Portal rejects them.
/// Earlier items of the call are applied so later ones see them and carry <c>dependsOn</c>.
/// </summary>
internal sealed partial class PlcWorkingState
{
    public const string WatchTableKind = "WatchTable";
    public const string ForceTableKind = "ForceTable";
    public const string WatchTableGroupKind = "WatchTableGroup";
    public const string PlcSoftwareKind = "PlcSoftware";

    private sealed record WatchTable(string Group, string Name, bool Force);

    private List<WatchTable>? _watchTables;
    private List<string>? _watchGroups;
    private bool _watchComplete = true;

    /// <summary>Watch/force table items resolve against the PLC's watch-table inventory; update_program needs nothing.</summary>
    public static bool NeedsWatchTables(PlcOperationRequest item)
        => PlcOnlineMethods.ContentWriteMethods.Contains(item.Operation) && item.Operation != PlcOnlineMethods.UpdateProgram
            || item.Operation == TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods.RenameWatchTableGroup;

    public static bool IsWatchContentUpdate(string operation)
        => operation is PlcOnlineMethods.UpdateWatchTableContent or PlcOnlineMethods.UpdateForceTableContent;

    /// <summary>Attaches the PLC's watch-table inventory, or null when no item of the call needs it.</summary>
    public PlcWorkingState WithWatchTables(PlcWatchTablePlcInfo? inventory)
    {
        if (inventory is null) return this;
        _watchTables = inventory.WatchTables.Select(t => new WatchTable(WatchGroupPath(t.GroupPath), t.Name, false))
            .Concat(inventory.ForceTables.Select(t => new WatchTable(WatchGroupPath(t.GroupPath), t.Name, true))).ToList();
        _watchGroups = inventory.Groups.Select(WatchGroupPath).ToList();
        _watchComplete = inventory.Complete;
        return this;
    }

    private bool IsWatchOperation(string operation) => PlcOnlineMethods.ContentWriteMethods.Contains(operation);

    private PlcWriteEffect ResolveWatch(PlcOperationRequest item, List<FiredGuard> guards)
    {
        if (item.Operation == PlcOnlineMethods.UpdateProgram)
            return Effect(item, new(PlcSoftwareKind, _plc, _device, null, null, _plc, null, null), Array.Empty<PlcFieldChange>(), null);
        if (_watchTables is null)
            throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the watch tables of PLC '{_plc}', which were not read.");
        if (!_watchComplete)
            guards.Add(Fire(PlcGuardDefinitions.StateUnverifiable, item,
                $"The watch-and-force table inventory of PLC '{_plc}' is incomplete, so the target's surroundings cannot be verified."));
        var group = WatchGroupPath(item.FolderPath);
        switch (item.Operation)
        {
            case PlcOnlineMethods.CreateWatchTable:
                RequireWatchGroup(item, group);
                return Effect(item, WatchTarget(WatchTableKind, group, item.TableName!), Change("tableName", null, item.TableName), group);
            case PlcOnlineMethods.DeleteWatchTable:
            {
                var table = RequireWatchTable(item, group, force: false);
                guards.Add(Fire(PlcGuardDefinitions.DeletesWatchTable, item, $"Deleting watch table '{table.Name}' in '{table.Group}' removes it and its entries."));
                return Effect(item, WatchTarget(WatchTableKind, table.Group, table.Name), Array.Empty<PlcFieldChange>(), null);
            }
            case PlcOnlineMethods.UpdateWatchTableContent:
            case PlcOnlineMethods.UpdateForceTableContent:
            {
                var force = item.Operation == PlcOnlineMethods.UpdateForceTableContent;
                var table = RequireWatchTable(item, group, force);
                return Effect(item, WatchTarget(force ? ForceTableKind : WatchTableKind, table.Group, table.Name), Array.Empty<PlcFieldChange>(), null);
            }
            case PlcOnlineMethods.CreateWatchTableGroup:
            {
                RequireWatchGroup(item, Parent(group));
                return Effect(item, WatchTarget(WatchTableGroupKind, group, null), Change("name", null, Leaf(group)), Parent(group));
            }
            case PlcOnlineMethods.DeleteWatchTableGroup:
            {
                RequireWatchGroup(item, group);
                var tables = _watchTables.Where(t => Within(t.Group, group)).ToArray();
                var groups = _watchGroups!.Where(g => Within(g, group) && !Names.Equals(g, group)).ToArray();
                if (tables.Length + groups.Length > 0)
                    guards.Add(Fire(PlcGuardDefinitions.DeletesWatchTable, item,
                        $"Deleting watch-and-force table group '{group}' removes {tables.Count(t => !t.Force)} watch tables, {tables.Count(t => t.Force)} force tables and {groups.Length} groups."));
                return Effect(item, WatchTarget(WatchTableGroupKind, group, null), Array.Empty<PlcFieldChange>(), null,
                    new(0, 0, Array.Empty<string>(), groups, null, null));
            }
            default:
                throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Unsupported PLC write operation '{item.Operation}'.");
        }
    }

    private void ApplyWatch(PlcOperationRequest item)
    {
        if (_watchTables is null) return;
        var group = WatchGroupPath(item.FolderPath);
        switch (item.Operation)
        {
            case PlcOnlineMethods.CreateWatchTable:
                _watchTables.Add(new WatchTable(group, item.TableName!, false));
                Touch(WatchKey(group, item.TableName!), item);
                break;
            case PlcOnlineMethods.DeleteWatchTable:
                var table = RequireWatchTable(item, group, force: false);
                _watchTables.Remove(table);
                Touch(WatchKey(table.Group, table.Name), item);
                break;
            case PlcOnlineMethods.CreateWatchTableGroup:
                _watchGroups!.Add(group);
                Touch(WatchGroupKey(group), item);
                break;
            case PlcOnlineMethods.DeleteWatchTableGroup:
                foreach (var removed in _watchTables.Where(t => Within(t.Group, group)).ToArray())
                {
                    _watchTables.Remove(removed);
                    Touch(WatchKey(removed.Group, removed.Name), item);
                }

                foreach (var removed in _watchGroups!.Where(g => Within(g, group)).ToArray())
                {
                    _watchGroups!.Remove(removed);
                    Touch(WatchGroupKey(removed), item);
                }

                break;
        }
    }

    /// <summary>The keys a watch-table target depends on: its group and every ancestor group, and the table itself.</summary>
    private static IEnumerable<string> WatchKeys(PlcTargetIdentity target)
    {
        if (target.Kind is not (WatchTableKind or ForceTableKind or WatchTableGroupKind) || target.FolderPath is null) return Array.Empty<string>();
        var keys = new List<string>();
        var segments = target.FolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var length = 1; length <= segments.Length; length++) keys.Add(WatchGroupKey("/" + string.Join("/", segments.Take(length))));
        if (target.TableName is not null) keys.Add(WatchKey(target.FolderPath, target.TableName));
        return keys;
    }

    private WatchTable RequireWatchTable(PlcOperationRequest item, string group, bool force)
    {
        var matches = _watchTables!.Where(t => t.Force == force && Names.Equals(t.Group, group) && Names.Equals(t.Name, item.TableName)).Take(2).ToList();
        var kind = force ? "force table" : "watch table";
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw NotFound(item, $"{kind} '{item.TableName}' was not found in watch-and-force table group '{group}' of PLC '{_plc}'.", !_watchComplete),
            _ => throw new ResolutionException(WorkerFailureCategories.TargetAmbiguous, $"Operation '{item.OperationId}': {kind} '{item.TableName}' is ambiguous in '{group}'."),
        };
    }

    private void RequireWatchGroup(PlcOperationRequest item, string group)
    {
        if (group != "/" && !_watchGroups!.Any(g => Names.Equals(g, group)))
            throw NotFound(item, $"watch-and-force table group '{group}' was not found in PLC '{_plc}'.", !_watchComplete);
    }

    private PlcTargetIdentity WatchTarget(string kind, string group, string? table) => new(kind, _plc, _device, group, table, null, null, null);

    private static string WatchGroupPath(string? path) => PlcWatchTablePolicy.NormalizeGroupPath(path);

    private static string Parent(string group) => group.LastIndexOf('/') is var slash and > 0 ? group[..slash] : "/";

    private static string Leaf(string group) => group[(group.LastIndexOf('/') + 1)..];

    private static bool Within(string candidate, string group)
        => Names.Equals(candidate, group) || candidate.StartsWith(group + "/", StringComparison.OrdinalIgnoreCase);

    private static string WatchKey(string group, string name) => $"watch:{group.ToUpperInvariant()}/{name.ToUpperInvariant()}";

    private static string WatchGroupKey(string group) => "watchgroup:" + group.ToUpperInvariant();

    /// <summary>The requested identity of a watch-table item whose PLC could not be verified.</summary>
    private static PlcTargetIdentity? UnresolvedWatchTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => item.Operation switch
        {
            PlcOnlineMethods.UpdateProgram => new(PlcSoftwareKind, plcName, deviceName, null, null, plcName, null, null),
            PlcOnlineMethods.CreateWatchTableGroup or PlcOnlineMethods.DeleteWatchTableGroup
                => new(WatchTableGroupKind, plcName, deviceName, WatchGroupPath(item.FolderPath), null, null, null, null),
            PlcOnlineMethods.UpdateForceTableContent => new(ForceTableKind, plcName, deviceName, WatchGroupPath(item.FolderPath), item.TableName, null, null, null),
            _ when PlcOnlineMethods.ContentWriteMethods.Contains(item.Operation)
                => new(WatchTableKind, plcName, deviceName, WatchGroupPath(item.FolderPath), item.TableName, null, null, null),
            _ => null,
        };
}
