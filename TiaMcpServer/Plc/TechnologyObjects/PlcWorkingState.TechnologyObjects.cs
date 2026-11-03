using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// The technology object part of the planner's working state. Objects and groups come from one
/// <c>list_technology_objects</c> read of the PLC, only when an item of the call needs it. Like tags, update and delete
/// targets are resolved (missing or ambiguous is an error). A create whose object name (unique per PLC: a technology
/// object is a DB) or group path is already in the inventory is invalid input and refused before anything runs; the
/// worker checks again before calling Create, so the object found afterwards is the created one. Whether TIA Portal
/// itself would reject such a collision is not verified. Objects are addressed by name across the whole tree. Earlier
/// items are applied so later ones see them and carry <c>dependsOn</c>; after an import (whose object names are unknown
/// before it runs) every later technology object item depends on it and is re-planned against the real state. A tag a
/// parameter or connect item names makes it depend on an earlier item of the call that created or changed that tag or table.
/// </summary>
internal sealed partial class PlcWorkingState
{
    public const string TechnologyObjectKind = "TechnologyObject";
    public const string TechnologyObjectGroupKind = "TechnologyObjectGroup";
    private const string TechnologyImportKey = "toimport";

    private sealed record TechnologyEntry(string Group, string Name);

    private List<TechnologyEntry>? _technologyObjects;
    private List<string>? _technologyGroups;
    private bool _technologyComplete = true;
    private bool _technologyImported;

    /// <summary>Every technology object write resolves against the PLC's technology object inventory.</summary>
    public static bool NeedsTechnologyObjects(PlcOperationRequest item) => TechnologyObjectMethods.IsWrite(item.Operation);

    /// <summary>Attaches the PLC's technology object inventory, or null when no item of the call needs it.</summary>
    public PlcWorkingState WithTechnologyObjects(TechnologyObjectPlcInfo? inventory)
    {
        if (inventory is null) return this;
        _technologyObjects = inventory.TechnologyObjects.Select(o => new TechnologyEntry(TechnologyObjectPolicy.NormalizeGroupPath(o.GroupPath), o.Name)).ToList();
        _technologyGroups = inventory.Groups.Select(g => TechnologyObjectPolicy.NormalizeGroupPath(g.Path)).ToList();
        _technologyComplete = inventory.Complete;
        return this;
    }

    private static bool IsTechnologyOperation(string operation) => TechnologyObjectMethods.IsWrite(operation);

    private PlcWriteEffect ResolveTechnology(PlcOperationRequest item, List<FiredGuard> guards)
    {
        if (_technologyObjects is null)
            throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the technology objects of PLC '{_plc}', which were not read.");
        if (!_technologyComplete)
            guards.Add(Fire(StateUnverifiable, item,
                $"The technology object inventory of PLC '{_plc}' is incomplete (more than {TechnologyObjectPolicy.MaxObjects} objects and groups), so the target's surroundings cannot be verified."));
        var group = TechnologyObjectPolicy.NormalizeGroupPath(item.FolderPath);
        switch (item.Operation)
        {
            case TechnologyObjectMethods.Create:
                RequireTechnologyGroup(item, group);
                if (_technologyObjects.FirstOrDefault(o => Names.Equals(o.Name, item.Name)) is { } existing)
                    throw new ResolutionException(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': a technology object named '{existing.Name}' already exists in group '{existing.Group}' of PLC '{_plc}' "
                        + "(names are unique per PLC); choose another name or delete it first.");
                return Effect(item, TechnologyTarget(TechnologyObjectKind, group, item.Name),
                    Change("technologyType", null, item.TechnologyType).Concat(Change("version", null, item.Version)).ToArray(), group);
            case TechnologyObjectMethods.Import:
                RequireTechnologyGroup(item, group);
                if (item.Overwrite == true)
                    guards.Add(Fire(TechnologyObjectImportReplaces, item,
                        $"Importing '{ShownFile(item.FilePath)}' with ImportOptions.Override is expected to replace any technology object of the same name in group '{group}' of PLC '{_plc}'."));
                return Effect(item, TechnologyTarget(TechnologyObjectGroupKind, group, null), Change("import", null, ShownFile(item.FilePath)), group);
            case TechnologyObjectMethods.CreateGroup:
                RequireTechnologyGroup(item, Parent(group));
                if (group == "/" || _technologyGroups!.Any(g => Names.Equals(g, group)))
                    throw new ResolutionException(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': technology object group '{group}' already exists in PLC '{_plc}'.");
                return Effect(item, TechnologyTarget(TechnologyObjectGroupKind, group, null), Change("name", null, Leaf(group)), Parent(group));
            case TechnologyObjectMethods.DeleteGroup:
            {
                RequireTechnologyGroup(item, group);
                var objects = _technologyObjects.Where(o => Within(o.Group, group)).Select(o => TechnologyObjectPolicy.Combine(o.Group, o.Name)).ToArray();
                var groups = _technologyGroups!.Where(g => Within(g, group) && !Names.Equals(g, group)).ToArray();
                if (objects.Length + groups.Length > 0)
                    guards.Add(Fire(DeletesTechnologyObjectGroupContents, item,
                        $"Deleting technology object group '{group}' removes {objects.Length} technology objects and {groups.Length} groups; the full list is in the effect's removes."));
                return Effect(item, TechnologyTarget(TechnologyObjectGroupKind, group, null), Array.Empty<PlcFieldChange>(), null,
                    new(0, 0, objects, groups, null, null));
            }

            case TechnologyObjectMethods.RenameGroup:
                RequireTechnologyGroup(item, group);
                return Effect(item, TechnologyTarget(TechnologyObjectGroupKind, group, null), Change("name", Leaf(group), item.NewName), null);
        }

        var entry = RequireTechnologyObject(item);
        if (entry is null)
            // Unknown before an earlier import of this call ran: dependsOn that import, re-planned against the real state.
            return Effect(item, TechnologyTarget(TechnologyObjectKind, null, item.Name), Array.Empty<PlcFieldChange>(), null);
        var target = TechnologyTarget(TechnologyObjectKind, entry.Group, entry.Name);
        switch (item.Operation)
        {
            case TechnologyObjectMethods.Delete:
            {
                var path = TechnologyObjectPolicy.Combine(entry.Group, entry.Name);
                guards.Add(Fire(DeletesTechnologyObject, item, $"Deleting technology object '{path}' of PLC '{_plc}' removes it; compile the PLC afterwards."));
                return Effect(item, target, Array.Empty<PlcFieldChange>(), null, new(0, 0, new[] { path }, Array.Empty<string>(), null, null));
            }

            case TechnologyObjectMethods.Rename:
                return Effect(item, target, Change("name", entry.Name, item.NewName), null);
            case TechnologyObjectMethods.SetParameter:
                // The planner reads the current value and fills in the change.
                return Effect(item, target, Change($"parameter {item.ParameterName}", null, Requested(item)), null);
            case TechnologyObjectMethods.ConnectHardware:
                return Effect(item, target, Change($"connection {Interface(item)}", null, $"{item.ConnectionKind}"), null);
            case TechnologyObjectMethods.DisconnectHardware:
                return Effect(item, target, Change($"connection {Interface(item)}", null, "disconnected"), null);
            case TechnologyObjectMethods.Export:
                return Effect(item, target, Change("exportFile", null, ShownFile(item.FilePath)), null);
            case TechnologyObjectMethods.UpdateContent:
                guards.Add(Fire(TechnologyObjectImportReplaces, item,
                    $"update_technology_object_content re-imports '{entry.Name}' into group '{entry.Group}' with ImportOptions.Override, which is expected to replace it."));
                return Effect(item, target, Array.Empty<PlcFieldChange>(), null);
            default:
                throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Unsupported PLC write operation '{item.Operation}'.");
        }
    }

    private void ApplyTechnology(PlcOperationRequest item)
    {
        if (_technologyObjects is null || !IsTechnologyOperation(item.Operation)) return;
        var group = TechnologyObjectPolicy.NormalizeGroupPath(item.FolderPath);
        switch (item.Operation)
        {
            case TechnologyObjectMethods.Create:
                _technologyObjects.Add(new TechnologyEntry(group, item.Name!));
                Touch(TechnologyKey(item.Name!), item);
                break;
            case TechnologyObjectMethods.Delete when RequireTechnologyObject(item) is { } deleted:
                _technologyObjects.Remove(deleted);
                Touch(TechnologyKey(deleted.Name), item);
                break;
            case TechnologyObjectMethods.Rename when RequireTechnologyObject(item) is { } renamed:
                _technologyObjects[_technologyObjects.IndexOf(renamed)] = renamed with { Name = item.NewName! };
                Touch(TechnologyKey(renamed.Name), item);
                Touch(TechnologyKey(item.NewName!), item);
                break;
            case TechnologyObjectMethods.UpdateContent when RequireTechnologyObject(item) is { } updated:
                Touch(TechnologyKey(updated.Name), item);
                break;
            case TechnologyObjectMethods.Import:
                _technologyImported = true;
                Touch(TechnologyImportKey, item);
                Touch(TechnologyGroupKey(group), item);
                break;
            case TechnologyObjectMethods.CreateGroup:
                _technologyGroups!.Add(group);
                Touch(TechnologyGroupKey(group), item);
                break;
            case TechnologyObjectMethods.DeleteGroup:
                foreach (var removed in _technologyObjects.Where(o => Within(o.Group, group)).ToArray())
                {
                    _technologyObjects.Remove(removed);
                    Touch(TechnologyKey(removed.Name), item);
                }

                foreach (var removed in _technologyGroups!.Where(g => Within(g, group)).ToArray())
                {
                    _technologyGroups!.Remove(removed);
                    Touch(TechnologyGroupKey(removed), item);
                }

                break;
            case TechnologyObjectMethods.RenameGroup:
            {
                var renamedPath = TechnologyObjectPolicy.Combine(Parent(group), item.NewName!);
                string Move(string path) => Names.Equals(path, group) ? renamedPath : renamedPath + path.Substring(group.Length);
                for (var i = 0; i < _technologyObjects.Count; i++)
                {
                    if (!Within(_technologyObjects[i].Group, group)) continue;
                    Touch(TechnologyKey(_technologyObjects[i].Name), item);
                    _technologyObjects[i] = _technologyObjects[i] with { Group = Move(_technologyObjects[i].Group) };
                }

                for (var i = 0; i < _technologyGroups!.Count; i++)
                {
                    if (!Within(_technologyGroups[i], group)) continue;
                    Touch(TechnologyGroupKey(_technologyGroups[i]), item);
                    _technologyGroups[i] = Move(_technologyGroups[i]);
                    Touch(TechnologyGroupKey(_technologyGroups[i]), item);
                }

                break;
            }
        }
    }

    /// <summary>
    /// The keys a technology object target depends on: its group and every ancestor group, the object, any earlier import,
    /// and the tag table and tag a parameter or connect item assigns (created or changed by an earlier tag item of the call).
    /// </summary>
    private static IEnumerable<string> TechnologyKeys(PlcTargetIdentity target, PlcOperationRequest? item)
    {
        if (target.Kind is not (TechnologyObjectKind or TechnologyObjectGroupKind)) return Array.Empty<string>();
        var keys = new List<string> { TechnologyImportKey };
        var segments = TechnologyObjectPolicy.Segments(target.FolderPath);
        for (var length = 1; length <= segments.Count; length++) keys.Add(TechnologyGroupKey("/" + string.Join("/", segments.Take(length))));
        if (target.Kind == TechnologyObjectKind && target.Name is not null) keys.Add(TechnologyKey(target.Name));
        if (item is not null && TechnologyObjectMethods.IsWrite(item.Operation))
        {
            if (item.TableName is { } table) keys.Add(TableKey(table));
            if (item.TagName is { } tag) keys.Add(CpuKey(tag));
        }

        return keys;
    }

    /// <summary>A caller file path as the worker will use it (backslash separators); unchanged when invalid (refused by validation).</summary>
    private static string? ShownFile(string? path)
        => path is not null && TechnologyObjectPolicy.ValidateLocalFilePath(path) is null ? TechnologyObjectPolicy.NormalizeLocalFilePath(path) : path;

    /// <summary>The single object named like the item (case-insensitive, whole tree); null only when an earlier import may have created it.</summary>
    private TechnologyEntry? RequireTechnologyObject(PlcOperationRequest item)
    {
        var matches = _technologyObjects!.Where(o => Names.Equals(o.Name, item.Name)).Take(2).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 when _technologyImported => null,
            0 => throw NotFound(item, $"technology object '{item.Name}' was not found in PLC '{_plc}'.", !_technologyComplete),
            _ => throw new ResolutionException(WorkerFailureCategories.TargetAmbiguous,
                $"Operation '{item.OperationId}': technology object '{item.Name}' is ambiguous in PLC '{_plc}'."),
        };
    }

    private void RequireTechnologyGroup(PlcOperationRequest item, string group)
    {
        if (group != "/" && !_technologyGroups!.Any(g => Names.Equals(g, group)))
            throw NotFound(item, $"technology object group '{group}' was not found in PLC '{_plc}'.", !_technologyComplete);
    }

    private PlcTargetIdentity TechnologyTarget(string kind, string? group, string? name) => new(kind, _plc, _device, group, null, name, null, null);

    private static string? Requested(PlcOperationRequest item)
        => item.TagName is not null ? $"PlcTag {item.TableName}/{item.TagName}" : item.Value;

    private static string Interface(PlcOperationRequest item)
        => item.InterfaceIndex is { } index ? $"{item.ConnectionInterface}[{index}]" : item.ConnectionInterface ?? string.Empty;

    private static string TechnologyKey(string name) => "to:" + name.ToUpperInvariant();

    private static string TechnologyGroupKey(string group) => "togroup:" + group.ToUpperInvariant();

    /// <summary>The requested identity of a technology object item whose PLC could not be verified.</summary>
    private static PlcTargetIdentity? UnresolvedTechnologyTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => item.Operation switch
        {
            TechnologyObjectMethods.CreateGroup or TechnologyObjectMethods.DeleteGroup or TechnologyObjectMethods.RenameGroup or TechnologyObjectMethods.Import
                => new(TechnologyObjectGroupKind, plcName, deviceName, TechnologyObjectPolicy.NormalizeGroupPath(item.FolderPath), null, null, null, null),
            TechnologyObjectMethods.Create
                => new(TechnologyObjectKind, plcName, deviceName, TechnologyObjectPolicy.NormalizeGroupPath(item.FolderPath), null, item.Name, null, null),
            _ when TechnologyObjectMethods.IsWrite(item.Operation) => new(TechnologyObjectKind, plcName, deviceName, null, null, item.Name, null, null),
            _ => null,
        };
}
