using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods;

namespace TiaMcpServer.Plc;

/// <summary>
/// The container part of the planner's working state: create/delete of PLC data type groups and tag table groups
/// and create/delete/rename of software units. Like every other create, a create is not checked against the
/// existing state (Portal rejects a duplicate or a missing parent at mutation); a delete or rename resolves its
/// target in the tree (missing or ambiguous is an error) and lists what it removes. A non-empty group or unit
/// fires the info guard <see cref="PlcGuardDefinitions.DeletesGroupContents"/>, as delete_block_group does; an
/// unreadable part of the tree inside the target fires <see cref="PlcGuardDefinitions.StateUnverifiable"/>,
/// because unreadable evidence is never treated as empty. Applied items update the groups, types, tag tables,
/// block tree and external sources later items of the call resolve against, and touch the keys
/// (<see cref="ContainerDependencyKeys"/>) that give those later items their <c>dependsOn</c>.
/// </summary>
internal sealed partial class PlcWorkingState
{
    private sealed record ContainerLeaf(string? Unit, string Folder, string Name);

    /// <summary>PLC data types of the PLC and its units (from the tree); null when the tree was not read.</summary>
    private List<ContainerLeaf>? _typeLeaves;

    /// <summary>Tag tables of the software units (from the tree); the PLC's own tables are <see cref="_tables"/>.</summary>
    private List<ContainerLeaf>? _tagLeaves;

    private const int ListedNames = 20;

    private PlcWriteEffect ResolveContainer(PlcOperationRequest item, List<FiredGuard> guards)
    {
        switch (item.Operation)
        {
            case CreateTypeGroup:
            {
                var path = GroupPathOf(item.TypePath);
                var canonical = Canonical(path);
                var leaf = path.Folders[^1];
                return Effect(item, new(PlcSourceFolderKinds.TypeGroup, _plc, _device, null, null, leaf, null, canonical), Change("name", null, leaf),
                    canonical[..canonical.LastIndexOf('/')]);
            }
            case DeleteTypeGroup:
            {
                var path = GroupPathOf(item.TypePath);
                var folder = PlcScopeInventory.FolderText(path.Folders);
                RequireFolder(item, _typeGroups, path.UnitName, folder, "PLC data type group", _typeTreeHidden);
                var canonical = Canonical(path);
                var types = (_typeLeaves ?? new()).Where(t => SameUnit(t.Unit, path.UnitName) && WithinFolder(t.Folder, folder))
                    .Select(t => $"{CanonicalScope(path.UnitName, PlcGroupPath.TypesKind)}{(t.Folder == "/" ? string.Empty : t.Folder)}/{t.Name}").ToList();
                var subgroups = Subfolders(_typeGroups, path.UnitName, folder).Select(f => CanonicalScope(path.UnitName, PlcGroupPath.TypesKind) + f).ToList();
                if (types.Count + subgroups.Count > 0)
                    guards.Add(Fire(PlcGuardDefinitions.DeletesGroupContents, item,
                        $"Deleting PLC data type group '{canonical}' removes {types.Count} PLC data types{NameList(types)} and {subgroups.Count} groups; blocks that use the types become inconsistent."));
                if (HiddenWithin(path.UnitName, ProjectTreeNodeTypes.TypeFolder, path.Folders)) guards.Add(SkippedGuard(item));
                return Effect(item, new(PlcSourceFolderKinds.TypeGroup, _plc, _device, null, null, path.Folders[^1], null, canonical), Array.Empty<PlcFieldChange>(), null,
                    new(0, 0, Array.Empty<string>(), subgroups, null, null));
            }
            case CreateTagTableGroup:
            {
                var folder = NormalizeFolder(item.FolderPath);
                var leaf = LeafName(folder);
                return Effect(item, new(PlcSourceFolderKinds.TagTableGroup, _plc, _device, UnitFolder(item.UnitName, folder), null, leaf, null, null),
                    Change("name", null, leaf), TagFolderPlacement(item.UnitName, ParentFolder(folder)));
            }
            case DeleteTagTableGroup:
            {
                var folder = NormalizeFolder(item.FolderPath);
                RequireFolder(item, _tagGroups, item.UnitName, folder, "tag table folder", _tagTreeHidden);
                var subgroups = Subfolders(_tagGroups, item.UnitName, folder).ToList();
                int tags = 0, constants = 0;
                List<string> tables;
                if (item.UnitName is null)
                {
                    var inside = _tables.Where(t => WithinFolder(t.Folder, folder)).ToList();
                    tags = inside.Sum(t => t.Tags.Count);
                    constants = inside.Sum(t => t.Constants.Count);
                    tables = inside.Select(TablePath).ToList();
                }
                else
                {
                    tables = (_tagLeaves ?? new()).Where(t => SameUnit(t.Unit, item.UnitName) && WithinFolder(t.Folder, folder))
                        .Select(t => (t.Folder == "/" ? string.Empty : t.Folder) + "/" + t.Name).ToList();
                }

                if (tables.Count + subgroups.Count > 0)
                    guards.Add(Fire(PlcGuardDefinitions.DeletesGroupContents, item,
                        $"Deleting tag table folder '{folder}'{UnitText(item.UnitName)} removes {tables.Count} tag tables{NameList(tables)}"
                        + (item.UnitName is null ? $" with {tags} tags and {constants} user constants" : string.Empty)
                        + $" and {subgroups.Count} folders."));
                if (HiddenWithin(item.UnitName, ProjectTreeNodeTypes.TagTableFolder, Segments(folder))) guards.Add(SkippedGuard(item));
                return Effect(item, new(PlcSourceFolderKinds.TagTableGroup, _plc, _device, UnitFolder(item.UnitName, folder), null, LeafName(folder), null, null),
                    Array.Empty<PlcFieldChange>(), null, new(tags, constants, Array.Empty<string>(), subgroups, null, null));
            }
            case CreateSoftwareUnit:
                return Effect(item, UnitTarget(item.UnitName!), Change("name", null, item.UnitName), $"{_plc}/Units");
            case DeleteSoftwareUnit:
            {
                var (name, root) = RequireUnit(item);
                var blocks = DescendantBlocks(root).Select(b => $"{b.Group.Path}/{b.Block.Name}").ToList();
                var groups = Descendants(root).Select(g => g.Path).ToList();
                var types = (_typeLeaves ?? new()).Count(t => SameUnit(t.Unit, name));
                var typeGroups = (_typeGroups ?? new()).Count(k => k.StartsWith(FolderKey(name, string.Empty), StringComparison.OrdinalIgnoreCase));
                var tables = (_tagLeaves ?? new()).Count(t => SameUnit(t.Unit, name));
                var tagGroups = (_tagGroups ?? new()).Count(k => k.StartsWith(FolderKey(name, string.Empty), StringComparison.OrdinalIgnoreCase));
                var sources = _sources?.Count(s => SameUnit(s.UnitName, name));
                if (blocks.Count + groups.Count + types + typeGroups + tables + tagGroups + (sources ?? 0) > 0)
                    guards.Add(Fire(PlcGuardDefinitions.DeletesGroupContents, item,
                        $"Deleting software unit '{name}' removes {blocks.Count} blocks and {groups.Count} block groups (listed in the effect's removes), {types} PLC data types, "
                        + $"{typeGroups} PLC data type groups, {tables} tag tables, {tagGroups} tag table folders"
                        + (sources is null ? string.Empty : $" and {sources} external sources")
                        + "; its alarm text lists (PlcAlarmTextlistGroup) are not listed (no read of them exists) and relations of other units to this unit are not checked."
                        + SourcesIncompleteNote));
                // What the delete removes is partly unreadable: like any delete consequence, that blocks.
                if (!_sourcesComplete)
                    guards.Add(Fire(PlcGuardDefinitions.StateUnverifiable, item, $"Deleting software unit '{name}' removes its external sources, which cannot all be verified.{SourcesIncompleteNote}"));
                if (_skipped.Any(s => s.ParentPath.Count > 2 && s.ParentPath[2].NodeType == ProjectTreeNodeTypes.SoftwareUnit && Names.Equals(s.ParentPath[2].Name, name)))
                    guards.Add(SkippedGuard(item));
                return Effect(item, UnitTarget(name), Array.Empty<PlcFieldChange>(), null, new(0, 0, blocks, groups, null, null));
            }
            case RenameSoftwareUnit:
            {
                var (name, root) = RequireUnit(item);
                guards.Add(Fire(PlcSourceFolderGuards.RenamesGroup, item,
                    $"Renaming software unit '{name}' to '{item.NewName}' changes the paths ({_plc}/Units/{name}/...) of its {DescendantBlocks(root).Count()} blocks, "
                    + $"{(_typeLeaves ?? new()).Count(t => SameUnit(t.Unit, name))} PLC data types and their groups, and the unitName of its "
                    + $"{(_sources ?? new()).Count(s => SameUnit(s.UnitName, name))} external sources; object names do not change. Relations of other units naming this unit are not checked."
                    + SourcesIncompleteNote));
                return Effect(item, UnitTarget(name), Change("name", name, item.NewName), null);
            }
            default:
                throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Unsupported PLC write operation '{item.Operation}'.");
        }
    }

    private void ApplyContainer(PlcOperationRequest item)
    {
        switch (item.Operation)
        {
            case CreateTypeGroup:
            {
                var path = GroupPathOf(item.TypePath);
                var folder = PlcScopeInventory.FolderText(path.Folders);
                _typeGroups?.Add(FolderKey(path.UnitName, folder));
                Touch(FolderTouchKey("typegroup", path.UnitName, folder), item);
                break;
            }
            case DeleteTypeGroup:
            {
                var path = GroupPathOf(item.TypePath);
                var folder = PlcScopeInventory.FolderText(path.Folders);
                foreach (var removed in RemoveFolders(_typeGroups, path.UnitName, folder)) Touch(FolderTouchKey("typegroup", path.UnitName, removed), item);
                foreach (var type in RemoveLeaves(_typeLeaves, path.UnitName, folder)) Touch(TypeKey(type.Name), item);
                break;
            }
            case CreateTagTableGroup:
            {
                var folder = NormalizeFolder(item.FolderPath);
                _tagGroups?.Add(FolderKey(item.UnitName, folder));
                Touch(FolderTouchKey("taggroup", item.UnitName, folder), item);
                break;
            }
            case DeleteTagTableGroup:
            {
                var folder = NormalizeFolder(item.FolderPath);
                foreach (var removed in RemoveFolders(_tagGroups, item.UnitName, folder)) Touch(FolderTouchKey("taggroup", item.UnitName, removed), item);
                RemoveLeaves(_tagLeaves, item.UnitName, folder);
                if (item.UnitName is null)
                    foreach (var table in _tables.Where(t => WithinFolder(t.Folder, folder)).ToList())
                    {
                        _tables.Remove(table);
                        Touch(TableKey(table.Name), item);
                        foreach (var name in table.Tags.Select(t => t.Name).Concat(table.Constants.Select(c => c.Name))) Touch(CpuKey(name), item);
                    }

                break;
            }
            case CreateSoftwareUnit:
            {
                var name = item.UnitName!;
                Touch(UnitKey(name), item);
                if (!_hasTree || _unitRoots.ContainsKey(name)) break;
                var selector = _basePath.Append(Segment(ProjectTreeNodeTypes.SoftwareUnit, name)).ToArray();
                _unitRoots[name] = new Group("Program blocks", $"{_plc}/Units/{name}/Blocks", null,
                    selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, "Program blocks")).ToArray(), false);
                break;
            }
            case DeleteSoftwareUnit:
            {
                var (name, root) = RequireUnit(item);
                Touch(UnitKey(name), item);
                foreach (var group in Descendants(root).Prepend(root)) Touch(GroupKey(group.Path), item);
                foreach (var block in DescendantBlocks(root)) Touch(CpuKey(block.Block.Name), item);
                _unitRoots.Remove(name);
                foreach (var removed in RemoveFolders(_typeGroups, name, null)) Touch(FolderTouchKey("typegroup", name, removed), item);
                foreach (var removed in RemoveFolders(_tagGroups, name, null)) Touch(FolderTouchKey("taggroup", name, removed), item);
                foreach (var type in RemoveLeaves(_typeLeaves, name, null)) Touch(TypeKey(type.Name), item);
                RemoveLeaves(_tagLeaves, name, null);
                foreach (var source in _sources?.Where(s => SameUnit(s.UnitName, name)).ToList() ?? new())
                {
                    _sources!.Remove(source);
                    Touch(SourceKey(source.UnitName, source.Name), item);
                }

                break;
            }
            case RenameSoftwareUnit:
            {
                var (name, root) = RequireUnit(item);
                var newName = item.NewName!;
                Touch(UnitKey(name), item);
                Touch(UnitKey(newName), item);
                foreach (var group in Descendants(root).Prepend(root)) Touch(GroupKey(group.Path), item);
                _unitRoots.Remove(name);
                var renamed = RebuildRoot(root, $"{_plc}/Units/{newName}/Blocks",
                    _basePath.Append(Segment(ProjectTreeNodeTypes.SoftwareUnit, newName)).Append(Segment(ProjectTreeNodeTypes.BlockFolder, root.Name)).ToArray());
                _unitRoots[newName] = renamed;
                foreach (var group in Descendants(renamed).Prepend(renamed)) Touch(GroupKey(group.Path), item);
                RenameUnitFolders(_typeGroups, name, newName);
                RenameUnitFolders(_tagGroups, name, newName);
                RenameUnitLeaves(_typeLeaves, name, newName);
                RenameUnitLeaves(_tagLeaves, name, newName);
                if (_sources is not null)
                    for (var index = 0; index < _sources.Count; index++)
                        if (SameUnit(_sources[index].UnitName, name))
                        {
                            Touch(SourceKey(_sources[index].UnitName, _sources[index].Name), item);
                            _sources[index] = new PlcExternalSourceInfo { Name = _sources[index].Name, UnitName = newName };
                            Touch(SourceKey(newName, _sources[index].Name), item);
                        }

                break;
            }
        }
    }

    /// <summary>
    /// The container keys a target depends on, added to every target's own keys (<see cref="LastTouchedBy"/>): the
    /// software unit an object path names (an earlier create, rename or delete of the unit), the tag table folders
    /// above a PLC tag table, tag or user constant, the PLC data type groups above a PLC data type, and for
    /// generate_blocks_from_source the target group.
    /// </summary>
    private static IEnumerable<string> ContainerDependencyKeys(PlcTargetIdentity target, PlcOperationRequest? item)
    {
        var keys = new List<string>();
        foreach (var path in new[] { target.BlockPath, target.TypePath, item?.Operation == GenerateBlocksFromSource ? item.TargetGroupPath : null })
            if (path is not null && UnitOfObjectPath(path) is { } unit) keys.Add(UnitKey(unit));
        if (target.Kind is PlcSourceFolderKinds.ExternalSource or PlcSourceFolderKinds.TagTableGroup && UnitOf(target.FolderPath) is { } folderUnit)
            keys.Add(UnitKey(folderUnit));
        if (target.Kind is PlcNameRules.TagTable or PlcNameRules.Tag or PlcNameRules.UserConstant && target.FolderPath is { } tableFolder)
            keys.AddRange(AncestorFolders(NormalizeFolder(tableFolder)).Select(folder => FolderTouchKey("taggroup", null, folder)));
        if (target.Kind == "Type" && target.TypePath is { } typePath && TypeFolders(typePath) is { } typeFolders)
            keys.AddRange(AncestorFolders(PlcScopeInventory.FolderText(typeFolders.Folders)).Select(folder => FolderTouchKey("typegroup", typeFolders.Unit, folder)));
        if (item?.Operation == GenerateBlocksFromSource && item.TargetGroupPath is { } groupPath && PlcGroupPath.TryParse(groupPath, out var group, out _))
            keys.AddRange(group!.Kind == PlcGroupPath.TypesKind
                ? AncestorFolders(PlcScopeInventory.FolderText(group.Folders)).Select(folder => FolderTouchKey("typegroup", group.UnitName, folder))
                : GroupKeys(group.ToDisplayPath()));
        return keys;
    }

    private (string Name, Group Root) RequireUnit(PlcOperationRequest item)
    {
        RequireTree(item);
        var name = item.UnitName!;
        if (_unitRoots.TryGetValue(name, out var root))
            return (_unitRoots.Keys.First(key => Names.Equals(key, name)), root);
        throw NotFound(item, $"software unit '{name}' was not found in PLC '{_plc}'.",
            _skipped.Any(s => s.NodeType == ProjectTreeNodeTypes.SoftwareUnit || s.ParentPath.Count == 2));
    }

    private PlcTargetIdentity UnitTarget(string name) => new(PlcSourceFolderKinds.SoftwareUnit, _plc, _device, UnitFolder(name, null), null, name, null, null);

    /// <summary>A skipped tree node inside the group <paramref name="folders"/> (below the scope's system folder) hides part of what a delete removes.</summary>
    private bool HiddenWithin(string? unit, string folderType, IReadOnlyList<string> folders)
        => _skipped.Any(s => InsideFolder(s.ParentPath, unit, folderType, folders));

    private static bool InsideFolder(IReadOnlyList<ProjectTreeSelectorSegment> parent, string? unit, string folderType, IReadOnlyList<string> folders)
    {
        var index = 2; // device, PLC software
        var inUnit = parent.Count > index && parent[index].NodeType == ProjectTreeNodeTypes.SoftwareUnit;
        if (inUnit != (unit is not null) || inUnit && !Names.Equals(parent[index].Name, unit)) return false;
        if (inUnit) index++;
        if (parent.Count <= index || parent[index].NodeType != folderType) return false;
        index++;
        if (parent.Count - index < folders.Count) return false;
        for (var level = 0; level < folders.Count; level++)
            if (parent[index + level].NodeType != folderType || !Names.Equals(parent[index + level].Name, folders[level])) return false;
        return true;
    }

    private static Group RebuildRoot(Group old, string path, IReadOnlyList<ProjectTreeSelectorSegment> selector)
    {
        var copy = new Group(old.Name, path, null, selector, old.IsSystem);
        copy.Blocks.AddRange(old.Blocks);
        foreach (var child in old.Groups)
            copy.Groups.Add(Rebuild(child, copy, child.Name, $"{path}/{child.Name}",
                selector.Append(Segment(child.IsSystem ? ProjectTreeNodeTypes.SystemBlockFolder : ProjectTreeNodeTypes.BlockFolder, child.Name)).ToArray()));
        return copy;
    }

    /// <summary>Removes <paramref name="folder"/> and every folder below it (all folders of the unit when null); returns the removed folder texts.</summary>
    private static List<string> RemoveFolders(HashSet<string>? folders, string? unit, string? folder)
    {
        if (folders is null) return new();
        var prefix = FolderKey(unit, folder ?? string.Empty);
        var removed = folders.Where(key => folder is null
                ? key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase) || key.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in removed) folders.Remove(key);
        return removed.Select(key => key[(key.IndexOf('\n') + 1)..]).ToList();
    }

    private static List<ContainerLeaf> RemoveLeaves(List<ContainerLeaf>? leaves, string? unit, string? folder)
    {
        if (leaves is null) return new();
        var removed = leaves.Where(leaf => SameUnit(leaf.Unit, unit) && (folder is null || WithinFolder(leaf.Folder, folder))).ToList();
        foreach (var leaf in removed) leaves.Remove(leaf);
        return removed;
    }

    private static void RenameLeafFolders(List<ContainerLeaf>? leaves, string? unit, string folder, string newName)
    {
        if (leaves is null) return;
        var renamed = Renamed(folder, newName);
        for (var index = 0; index < leaves.Count; index++)
            if (SameUnit(leaves[index].Unit, unit) && WithinFolder(leaves[index].Folder, folder))
                leaves[index] = leaves[index] with { Folder = renamed + leaves[index].Folder[folder.Length..] };
    }

    private static void RenameUnitFolders(HashSet<string>? folders, string unit, string newUnit)
    {
        if (folders is null) return;
        var prefix = FolderKey(unit, string.Empty);
        foreach (var key in folders.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            folders.Remove(key);
            folders.Add(FolderKey(newUnit, key[prefix.Length..]));
        }
    }

    private static void RenameUnitLeaves(List<ContainerLeaf>? leaves, string unit, string newUnit)
    {
        if (leaves is null) return;
        for (var index = 0; index < leaves.Count; index++)
            if (SameUnit(leaves[index].Unit, unit)) leaves[index] = leaves[index] with { Unit = newUnit };
    }

    /// <summary>The folders strictly below <paramref name="folder"/> of one scope, as folder texts.</summary>
    private static IEnumerable<string> Subfolders(HashSet<string>? folders, string? unit, string folder)
    {
        var prefix = FolderKey(unit, folder) + "/";
        return (folders ?? new()).Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[(key.IndexOf('\n') + 1)..]).OrderBy(key => key, StringComparer.Ordinal);
    }

    /// <summary><c>/A/B</c> → <c>/A</c>, <c>/A/B</c>; <c>/</c> → nothing.</summary>
    private static IEnumerable<string> AncestorFolders(string folder)
    {
        var segments = Segments(folder);
        for (var length = 1; length <= segments.Count; length++)
            yield return "/" + string.Join("/", segments.Take(length));
    }

    private static IReadOnlyList<string> Segments(string folder) => folder.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string LeafName(string folder) => folder[(folder.LastIndexOf('/') + 1)..];

    private static string ParentFolder(string folder) => folder.LastIndexOf('/') is var slash and > 0 ? folder[..slash] : "/";

    private static bool SameUnit(string? left, string? right) => Names.Equals(left ?? string.Empty, right ?? string.Empty);

    private string CanonicalScope(string? unit, string kind) => unit is null ? $"{_plc}/{kind}" : $"{_plc}/Units/{unit}/{kind}";

    private string TagFolderPlacement(string? unit, string folder) => $"{(unit is null ? _plc : $"{_plc}/Units/{unit}")} tag tables {folder}";

    private static string UnitText(string? unit) => unit is null ? string.Empty : $" of software unit '{unit}'";

    private static string NameList(IReadOnlyList<string> names)
        => names.Count == 0 ? string.Empty
            : $" ({string.Join(", ", names.Take(ListedNames))}{(names.Count > ListedNames ? $", and {names.Count - ListedNames} more" : string.Empty)})";

    /// <summary>The software unit a block, type or group path names (<c>PLC/Units/U/...</c>), else null.</summary>
    private static string? UnitOfObjectPath(string path)
    {
        var segments = path.Split('/');
        return segments.Length > 3 && string.Equals(segments[1].Trim(), "Units", StringComparison.OrdinalIgnoreCase) ? segments[2].Trim() : null;
    }

    private static (string? Unit, IReadOnlyList<string> Folders)? TypeFolders(string typePath)
    {
        try
        {
            var address = PlcTypeAddress.Parse(typePath);
            return address.IsDeterministic ? (address.UnitName, address.FolderPath) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string UnitKey(string unit) => "unit:" + unit.ToUpperInvariant();

    /// <summary>A tag table folder's verification key: the PLC's own folders prefix their tables' keys; a unit's folders have their own namespace.</summary>
    private static string TagFolderObjectKey(string scope, string? folderPath, string folder)
        => PlcObjectKeys.Container(UnitOf(folderPath) is { } unit ? $"{scope}unittable\n{unit}" : $"{scope}table", folder);

    /// <summary>
    /// A software unit operation's verification keys: it owns the unit's container-namespace keys (so a later unit
    /// operation supersedes it, and its block and type group operations); as a later item it also offers the prefixes of
    /// the unit's blocks, types, external sources and tag table folders.
    /// </summary>
    private static IEnumerable<string> UnitObjectKeys(string scope, string plc, string unit, bool asLater)
    {
        var own = new[]
        {
            PlcObjectKeys.ContainerOwn(scope, "unit", unit), PlcObjectKeys.ContainerOwn(scope, "block", $"{plc}/Units/{unit}"),
            PlcObjectKeys.ContainerOwn(scope, "type", $"{plc}/Units/{unit}"),
        };
        return asLater
            ? own.Concat(new[]
            {
                PlcObjectKeys.Container($"{scope}block", $"{plc}/Units/{unit}"), PlcObjectKeys.Container($"{scope}type", $"{plc}/Units/{unit}"),
                $"{scope}source\n{unit}", $"{scope}unittable\n{unit}",
            })
            : own;
    }
}
