using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.ProjectTree;
using static TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods;

namespace TiaMcpServer.Plc;

/// <summary>
/// The external-source, group-rename and file-export part of the planner's working state. External sources come
/// from one <c>list_external_sources</c> read of the PLC (<see cref="NeedsExternalSources"/>), block, PLC data type
/// and tag-table groups from the PLC's project tree (<see cref="NeedsSourceFolderTree"/>), watch-table groups from
/// the watch-table inventory. Like the other writes, update/delete targets (a source to generate from or delete, a
/// group to rename) are resolved — missing or ambiguous is an error, unreadable evidence blocks — and creates are
/// not checked against existing state. File exports pre-check their targets: an existing file without
/// overwrite:true and a missing directory are refused before anything runs. Earlier items of the call are applied
/// so later ones see them and carry <c>dependsOn</c>.
/// </summary>
internal sealed partial class PlcWorkingState
{
    private List<PlcExternalSourceInfo>? _sources;
    private bool _sourcesComplete = true;
    private string? _sourcesProblem;
    private HashSet<string>? _typeGroups;
    private HashSet<string>? _tagGroups;
    private bool _typeTreeHidden;
    private bool _tagTreeHidden;
    private PlcScopeInventory? _scope;

    public static bool HandlesSourceFolder(string? operation) => IsWrite(operation);

    /// <summary>
    /// The items that read the PLC's external sources: generate and delete resolve their source; the software unit
    /// delete lists the unit's sources in its content guard and the unit rename moves them to the new unit name.
    /// </summary>
    public static bool NeedsExternalSources(PlcOperationRequest item)
        => item.Operation is GenerateBlocksFromSource or DeleteExternalSource or DeleteSoftwareUnit or RenameSoftwareUnit;

    /// <summary>
    /// Renames of block, type and tag-table groups, the group deletes and the software unit deletes and renames
    /// resolve their target in the tree; export_blocks plans its files from it.
    /// </summary>
    public static bool NeedsSourceFolderTree(PlcOperationRequest item)
        => item.Operation is RenameBlockGroup or RenameTypeGroup or RenameTagTableGroup or ExportBlocks
            or DeleteTypeGroup or DeleteTagTableGroup or DeleteSoftwareUnit or RenameSoftwareUnit;

    /// <summary>The PLC selector of an item: the PLC of its group or object paths, else plcName.</summary>
    /// <exception cref="ArgumentException">The paths name different PLCs or are malformed.</exception>
    public static string? SourceFolderPlcSelector(PlcOperationRequest item)
    {
        switch (item.Operation)
        {
            case RenameBlockGroup:
                return GroupPathOf(item.BlockPath).PlcName;
            case RenameTypeGroup or CreateTypeGroup or DeleteTypeGroup:
                return GroupPathOf(item.TypePath).PlcName;
            case ExportBlocks when (item.BlockPath ?? item.TypePath) is { } scope:
                return GroupPathOf(scope).PlcName;
            case ExportSourceFile:
            {
                var names = (item.BlockPaths ?? Array.Empty<string>()).Select(path => BlockAddress.Parse(path).PlcName)
                    .Concat((item.TypePaths ?? Array.Empty<string>()).Select(path => PlcTypeAddress.Parse(path).PlcName))
                    .Distinct(Names).ToList();
                if (names.Count > 1)
                    throw new ArgumentException("blockPaths and typePaths name more than one PLC (or mix paths with and without a PLC); one export_source_file item writes the source of one PLC scope.");
                return names.FirstOrDefault();
            }
            default:
                return item.PlcName;
        }
    }

    /// <summary>Attaches the tree-derived groups and the PLC's external sources; both are null when no item of the call needs them.</summary>
    /// <param name="sourcesProblem">Why the external sources could not be read, when the read degraded to an empty, incomplete inventory.</param>
    public PlcWorkingState WithSourceFolders(ProjectTreeObservation? tree, PlcExternalSourcePlcInfo? sources, bool sourcesComplete, string? sourcesProblem = null)
    {
        if (sources is not null)
        {
            _sources = sources.Sources.Select(s => new PlcExternalSourceInfo { Name = s.Name, UnitName = s.UnitName }).ToList();
            _sourcesComplete = sourcesComplete;
            _sourcesProblem = sourcesProblem;
        }

        var software = tree?.Roots.FirstOrDefault(r => r.NodeType == ProjectTreeNodeTypes.PlcSoftware && Names.Equals(r.Name, _plc));
        if (software is null) return this;
        _scope = PlcScopeInventory.FromSoftware(software, _device ?? string.Empty, _skipped.Count);
        _typeGroups = new HashSet<string>(_scope.Items.Where(i => i.Kind == "type_group").Select(i => FolderKey(i.Unit, PlcScopeInventory.FolderText(i.Folders))), StringComparer.OrdinalIgnoreCase);
        _tagGroups = new HashSet<string>(_scope.Items.Where(i => i.Kind == "tag_table_group").Select(i => FolderKey(i.Unit, PlcScopeInventory.FolderText(i.Folders))), StringComparer.OrdinalIgnoreCase);
        _typeLeaves = _scope.Items.Where(i => i.Kind == "type").Select(i => new ContainerLeaf(i.Unit, PlcScopeInventory.FolderText(i.Folders), i.Name)).ToList();
        _tagLeaves = _scope.Items.Where(i => i.Kind == "tag_table" && i.Unit is not null).Select(i => new ContainerLeaf(i.Unit, PlcScopeInventory.FolderText(i.Folders), i.Name)).ToList();
        _typeTreeHidden = _skipped.Any(s => s.NodeType is ProjectTreeNodeTypes.Type or ProjectTreeNodeTypes.TypeFolder or ProjectTreeNodeTypes.SoftwareUnit);
        _tagTreeHidden = _skipped.Any(s => s.NodeType is ProjectTreeNodeTypes.TagTable or ProjectTreeNodeTypes.TagTableFolder or ProjectTreeNodeTypes.SoftwareUnit);
        return this;
    }

    private PlcWriteEffect ResolveSourceFolder(PlcOperationRequest item, List<FiredGuard> guards)
    {
        switch (item.Operation)
        {
            case ImportExternalSource:
            {
                if (!File.Exists(item.FilePath))
                    throw new ResolutionException(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': the source file '{item.FilePath}' does not exist{(Directory.Exists(item.FilePath) ? " (it is a directory)" : string.Empty)}. Nothing was imported.");
                var name = SourceName(item);
                return Effect(item, SourceTarget(item.UnitName, name), Change("sourceName", null, name).Concat(Change("filePath", null, item.FilePath)).ToArray(), SourcePlacement(item.UnitName));
            }
            case GenerateBlocksFromSource:
            {
                var source = RequireSource(item);
                var option = item.GenerateOption ?? "None";
                guards.Add(Fire(PlcSourceFolderGuards.GeneratesFromSource, item, option == "None"
                    ? $"generate_blocks_from_source creates or overwrites every block and PLC data type that external source '{source.Name}' declares (existing objects of the same names are replaced); with GenerateBlockOption.None an error makes TIA Portal delete what it generated."
                    : $"generate_blocks_from_source creates or overwrites every block and PLC data type that external source '{source.Name}' declares (existing objects of the same names are replaced); with GenerateBlockOption.KeepOnError objects that contain errors are kept. Compile them with compile_check."));
                return Effect(item, SourceTarget(source.UnitName, source.Name),
                    Change("generateOption", null, option).Concat(Change("targetGroupPath", null, item.TargetGroupPath ?? "(scope root)")).ToArray(),
                    item.TargetGroupPath ?? SourceRootPlacement(source.UnitName));
            }
            case DeleteExternalSource:
            {
                var source = RequireSource(item);
                guards.Add(Fire(PlcSourceFolderGuards.DeletesExternalSource, item,
                    $"Deleting external source '{source.Name}' removes the source node only; blocks and PLC data types generated from it stay."));
                return Effect(item, SourceTarget(source.UnitName, source.Name), Array.Empty<PlcFieldChange>(), null);
            }
            case ExportSourceFile:
            case ExportTagTablesCsv:
            case ExportWatchTablesCsv:
            case ExportForceTablesCsv:
            {
                if (PlcFileExports.FilePrecheck(item) is { } refusal)
                    throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {refusal}");
                var exists = File.Exists(item.FilePath);
                if (exists)
                    guards.Add(Fire(PlcSourceFolderGuards.ExportOverwritesFiles, item, $"{item.Operation} replaces the existing file '{item.FilePath}' (overwrite:true)."));
                var changes = Change("file", exists ? "exists" : null, item.FilePath).ToList();
                if (item.Operation == ExportSourceFile)
                    changes.AddRange(Change("objects", null, string.Join(", ", (item.BlockPaths ?? Array.Empty<string>()).Concat(item.TypePaths ?? Array.Empty<string>()))));
                // The file target's name is its full path: two items writing the same file depend on each other.
                return Effect(item, new(PlcSourceFolderKinds.File, _plc, _device, item.FolderPath, item.TableName, item.FilePath, null, null),
                    changes, item.FilePath);
            }
            case ExportBlocks:
                return ResolveExportBlocks(item, guards);
            case RenameBlockGroup:
            {
                RequireTree(item);
                var path = GroupPathOf(item.BlockPath);
                var group = ResolveGroup(item, OwnerOf(item, path.UnitName), path.Folders);
                var removedBlocks = DescendantBlocks(group).Count();
                var subgroups = Descendants(group).Count();
                guards.Add(Fire(PlcSourceFolderGuards.RenamesGroup, item,
                    $"Renaming block group '{group.Path}' to '{item.NewName}' changes the deterministic paths of its {removedBlocks} blocks and {subgroups} subgroups; block names and references between blocks do not change."));
                return Effect(item, new(PlcNameRules.BlockGroup, _plc, _device, null, null, group.Name, group.Path, null), Change("name", group.Name, item.NewName), null);
            }
            case RenameTypeGroup:
            {
                var path = GroupPathOf(item.TypePath);
                var folder = PlcScopeInventory.FolderText(path.Folders);
                RequireFolder(item, _typeGroups, path.UnitName, folder, "PLC data type group", _typeTreeHidden);
                var canonical = Canonical(path);
                guards.Add(Fire(PlcSourceFolderGuards.RenamesGroup, item,
                    $"Renaming PLC data type group '{canonical}' to '{item.NewName}' changes the deterministic paths of the PLC data types and groups inside it; type names do not change."));
                return Effect(item, new(PlcSourceFolderKinds.TypeGroup, _plc, _device, null, null, path.Folders[^1], null, canonical), Change("name", path.Folders[^1], item.NewName), null);
            }
            case RenameTagTableGroup:
            {
                var folder = NormalizeFolder(item.FolderPath);
                RequireFolder(item, _tagGroups, item.UnitName, folder, "tag table folder", _tagTreeHidden);
                var leaf = folder[(folder.LastIndexOf('/') + 1)..];
                guards.Add(Fire(PlcSourceFolderGuards.RenamesGroup, item,
                    $"Renaming tag table folder '{folder}'{(item.UnitName is null ? string.Empty : $" of software unit '{item.UnitName}'")} to '{item.NewName}' changes the folderPath of the tag tables and folders inside it; tag names do not change."));
                return Effect(item, new(PlcSourceFolderKinds.TagTableGroup, _plc, _device, UnitFolder(item.UnitName, folder), null, leaf, null, null), Change("name", leaf, item.NewName), null);
            }
            case RenameWatchTableGroup:
            {
                if (_watchGroups is null)
                    throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the watch tables of PLC '{_plc}', which were not read.");
                if (!_watchComplete)
                    guards.Add(Fire(PlcGuardDefinitions.StateUnverifiable, item,
                        $"The watch-and-force table inventory of PLC '{_plc}' is incomplete, so the target's surroundings cannot be verified."));
                var group = WatchGroupPath(item.FolderPath);
                RequireWatchGroup(item, group);
                guards.Add(Fire(PlcSourceFolderGuards.RenamesGroup, item,
                    $"Renaming watch-and-force table group '{group}' to '{item.NewName}' changes the folderPath of the watch tables, force tables and groups inside it."));
                return Effect(item, WatchTarget(WatchTableGroupKind, group, null), Change("name", Leaf(group), item.NewName), null);
            }
            default:
                return ResolveContainer(item, guards);
        }
    }

    private PlcWriteEffect ResolveExportBlocks(PlcOperationRequest item, List<FiredGuard> guards)
    {
        RequireTree(item);
        if (PlcFileExports.DirectoryPrecheck(item) is { } refusal)
            throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {refusal}");
        var (plan, category, error) = PlcBlockExportPlan.Build(_scope ?? throw new InvalidOperationException($"The tree inventory of PLC '{_plc}' was not attached."), item);
        if (plan is null) throw new ResolutionException(category!, $"Operation '{item.OperationId}': {error}", hidden: category == WorkerFailureCategories.TargetNotFound && _skipped.Count > 0);
        var existing = plan.ExistingPrimaryFiles(item.DirectoryPath!);
        if (existing.Count > 0 && item.Overwrite != true)
            throw new ResolutionException(WorkerFailureCategories.ValidationError,
                $"Operation '{item.OperationId}': {existing.Count} target files exist already ({string.Join(", ", existing.Take(10))}{(existing.Count > 10 ? ", ..." : string.Empty)}); pass overwrite:true to replace them. Nothing was written.");
        if (existing.Count > 0)
            guards.Add(Fire(PlcSourceFolderGuards.ExportOverwritesFiles, item, $"export_blocks replaces {existing.Count} existing files below '{item.DirectoryPath}' (overwrite:true)."));
        // An unreadable tree part is reported (treeComplete, and complete:false in the result), not blocked: the
        // export writes files outside TIA Portal and lists what it could not see.
        var exported = plan.Objects.Count(o => o.SkipReason is null);
        var changes = new List<PlcFieldChange>
        {
            new("objects", null, plan.Objects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("exportable", null, exported.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("format", null, plan.Format),
            new("treeComplete", null, plan.TreeComplete ? "true" : "false"),
        };
        return Effect(item, new(PlcSourceFolderKinds.Directory, _plc, _device, null, null, plan.Scope ?? _plc,
            item.BlockPath is null ? null : plan.Scope, item.TypePath is null ? null : plan.Scope), changes, item.DirectoryPath);
    }

    private void ApplySourceFolder(PlcOperationRequest item)
    {
        switch (item.Operation)
        {
            case ImportExternalSource:
            {
                var name = SourceName(item);
                _sources?.Add(new PlcExternalSourceInfo { Name = name, UnitName = item.UnitName });
                Touch(SourceKey(item.UnitName, name), item);
                break;
            }
            case GenerateBlocksFromSource:
                Touch(SourceKey(item.UnitName, item.SourceName!), item);
                break;
            case DeleteExternalSource:
            {
                var source = RequireSource(item);
                _sources?.Remove(source);
                Touch(SourceKey(source.UnitName, source.Name), item);
                break;
            }
            case ExportSourceFile:
            case ExportTagTablesCsv:
            case ExportWatchTablesCsv:
            case ExportForceTablesCsv:
                Touch(FileKey(item.FilePath!), item);
                break;
            case RenameBlockGroup:
            {
                var path = GroupPathOf(item.BlockPath);
                var group = ResolveGroup(item, OwnerOf(item, path.UnitName), path.Folders);
                foreach (var g in Descendants(group).Prepend(group)) Touch(GroupKey(g.Path), item);
                var renamed = RenameGroup(group, item.NewName!);
                foreach (var g in Descendants(renamed).Prepend(renamed)) Touch(GroupKey(g.Path), item);
                break;
            }
            case RenameTypeGroup:
            {
                var path = GroupPathOf(item.TypePath);
                RenameFolder(_typeGroups, path.UnitName, PlcScopeInventory.FolderText(path.Folders), item.NewName!);
                RenameLeafFolders(_typeLeaves, path.UnitName, PlcScopeInventory.FolderText(path.Folders), item.NewName!);
                Touch(FolderTouchKey("typegroup", path.UnitName, PlcScopeInventory.FolderText(path.Folders)), item);
                Touch(FolderTouchKey("typegroup", path.UnitName, Renamed(PlcScopeInventory.FolderText(path.Folders), item.NewName!)), item);
                break;
            }
            case RenameTagTableGroup:
            {
                var folder = NormalizeFolder(item.FolderPath);
                var renamed = Renamed(folder, item.NewName!);
                RenameFolder(_tagGroups, item.UnitName, folder, item.NewName!);
                RenameLeafFolders(_tagLeaves, item.UnitName, folder, item.NewName!);
                if (item.UnitName is null)
                {
                    // The tag inventory addresses the PLC's own tables only; move them to the renamed folder.
                    for (var index = 0; index < _tables.Count; index++)
                        if (WithinFolder(_tables[index].Folder, folder))
                        {
                            Touch(TableKey(_tables[index].Name), item);
                            _tables[index] = _tables[index] with { Folder = renamed + _tables[index].Folder[folder.Length..] };
                        }
                }

                Touch(FolderTouchKey("taggroup", item.UnitName, folder), item);
                Touch(FolderTouchKey("taggroup", item.UnitName, renamed), item);
                break;
            }
            case RenameWatchTableGroup:
            {
                if (_watchTables is null) break;
                var group = WatchGroupPath(item.FolderPath);
                var renamed = Renamed(group, item.NewName!);
                for (var index = 0; index < _watchTables.Count; index++)
                    if (Within(_watchTables[index].Group, group))
                    {
                        Touch(WatchKey(_watchTables[index].Group, _watchTables[index].Name), item);
                        _watchTables[index] = _watchTables[index] with { Group = renamed + _watchTables[index].Group[group.Length..] };
                    }

                for (var index = 0; index < _watchGroups!.Count; index++)
                    if (Within(_watchGroups[index], group))
                    {
                        Touch(WatchGroupKey(_watchGroups[index]), item);
                        _watchGroups[index] = renamed + _watchGroups[index][group.Length..];
                        Touch(WatchGroupKey(_watchGroups[index]), item);
                    }

                break;
            }
            default:
                ApplyContainer(item);
                break;
        }
    }

    /// <summary>The keys a source/folder/export target depends on.</summary>
    private static IEnumerable<string> SourceFolderKeys(PlcTargetIdentity target)
        => target.Kind switch
        {
            PlcSourceFolderKinds.ExternalSource => new[] { SourceKey(UnitOf(target.FolderPath), target.Name!) },
            PlcSourceFolderKinds.File => new[] { FileKey(target.Name!) },
            PlcSourceFolderKinds.TypeGroup when target.TypePath is not null && PlcGroupPath.TryParse(target.TypePath, out var path, out _)
                => AncestorFolders(PlcScopeInventory.FolderText(path!.Folders)).Select(folder => FolderTouchKey("typegroup", path.UnitName, folder)),
            PlcSourceFolderKinds.TagTableGroup => AncestorFolders(FolderOf(target.FolderPath)).Select(folder => FolderTouchKey("taggroup", UnitOf(target.FolderPath), folder)),
            PlcSourceFolderKinds.SoftwareUnit => new[] { UnitKey(target.Name!) },
            _ => Array.Empty<string>(),
        };

    /// <summary>
    /// Verification keys (<see cref="PlcVerificationPolicy.ObjectKeys"/>, shapes from <see cref="PlcObjectKeys"/>). A block
    /// group, PLC data type group or software unit operation owns its container key in the container namespace
    /// (<see cref="PlcObjectKeys.ContainerOwn"/>; a rename its old and new path) and, as a later item, also offers the
    /// prefix of everything inside it at any depth; tag-table and watch-table group operations own their folder key, which
    /// prefixes the tables inside. A software unit's later keys cover its blocks, PLC data types, external sources and tag
    /// table folders. A file export owns its file; a source item its source. generate_blocks_from_source owns only its own
    /// item key (it changes neither the source nor a container) and as a later item offers the prefix of every block and
    /// PLC data type of its PLC, whatever path form an earlier item used: earlier block and type content items of the PLC
    /// are conservatively superseded instead of being reported with a content hash the generation may have made stale;
    /// container items and source imports are not.
    /// </summary>
    public static IEnumerable<string> SourceFolderObjectKeys(PlcOperationRequest item, PlcTargetIdentity target, string scope, bool asLater)
    {
        switch (item.Operation)
        {
            case RenameBlockGroup when target.BlockPath is { } path:
                return ContainerKeys(scope, "block", asLater, path, RenamedPath(path, item.NewName!));
            case RenameTypeGroup when target.TypePath is { } path:
                return ContainerKeys(scope, "type", asLater, path, RenamedPath(path, item.NewName!));
            case CreateTypeGroup or DeleteTypeGroup when target.TypePath is { } path:
                return ContainerKeys(scope, "type", asLater, path);
            case RenameTagTableGroup:
                return new[] { TagFolderObjectKey(scope, target.FolderPath, FolderOf(target.FolderPath)), TagFolderObjectKey(scope, target.FolderPath, Renamed(FolderOf(target.FolderPath), item.NewName!)) };
            case RenameWatchTableGroup:
                return new[] { PlcObjectKeys.Container($"{scope}watch", target.FolderPath), PlcObjectKeys.Container($"{scope}watch", Renamed(target.FolderPath ?? "/", item.NewName!)) };
            case CreateTagTableGroup or DeleteTagTableGroup:
                return new[] { TagFolderObjectKey(scope, target.FolderPath, FolderOf(target.FolderPath)) };
            case CreateSoftwareUnit or DeleteSoftwareUnit:
                return UnitObjectKeys(scope, target.PlcName, target.Name!, asLater);
            case RenameSoftwareUnit:
                return UnitObjectKeys(scope, target.PlcName, target.Name!, asLater).Concat(UnitObjectKeys(scope, target.PlcName, item.NewName!, asLater));
            case GenerateBlocksFromSource:
                // The scope (device and PLC) is part of every key, so these prefixes cover every block and type of the PLC.
                return asLater ? new[] { $"{scope}block", $"{scope}type" } : new[] { $"{scope}generation\n{item.OperationId}" };
            case ImportExternalSource or DeleteExternalSource:
                return new[] { $"{scope}source\n{UnitOf(target.FolderPath)}\n{target.Name}" };
            case ExportBlocks:
                return new[] { $"{scope}directory\n{PlcFileTargets.Canonical(item.DirectoryPath ?? string.Empty)}" };
            default:
                return new[] { $"{scope}file\n{PlcFileTargets.Canonical(item.FilePath ?? string.Empty)}" };
        }
    }

    /// <summary>A block or type container operation's keys: its own container-namespace keys, plus (as a later item) the content prefixes.</summary>
    private static IEnumerable<string> ContainerKeys(string scope, string kind, bool asLater, params string[] paths)
        => paths.Select(path => PlcObjectKeys.ContainerOwn(scope, kind, path))
            .Concat(asLater ? paths.Select(path => PlcObjectKeys.Container($"{scope}{kind}", path)) : Array.Empty<string>());

    /// <summary>
    /// What a later source, folder or unit item that superseded an earlier item says about the earlier object: these
    /// items are trusted (nothing is read back), so a rename names the object's new path to re-read, a delete says
    /// the object is gone, a generation that it may have overwritten the object.
    /// </summary>
    public static string? SourceFolderSupersession(PlcTargetIdentity earlier, PlcOperationRequest later, PlcTargetIdentity latest)
    {
        string? oldPrefix = null, newPrefix = null;
        switch (later.Operation)
        {
            case RenameBlockGroup when latest.BlockPath is { } path:
                (oldPrefix, newPrefix) = (path, RenamedPath(path, later.NewName!));
                break;
            case RenameTypeGroup when latest.TypePath is { } path:
                (oldPrefix, newPrefix) = (path, RenamedPath(path, later.NewName!));
                break;
            case RenameTagTableGroup:
                (oldPrefix, newPrefix) = (FolderOf(latest.FolderPath), Renamed(FolderOf(latest.FolderPath), later.NewName!));
                break;
            case RenameWatchTableGroup when latest.FolderPath is { } group:
                (oldPrefix, newPrefix) = (group, Renamed(group, later.NewName!));
                break;
            case RenameSoftwareUnit:
                (oldPrefix, newPrefix) = ($"{latest.PlcName}/Units/{latest.Name}", $"{latest.PlcName}/Units/{later.NewName}");
                break;
            case DeleteTypeGroup or DeleteTagTableGroup or DeleteSoftwareUnit or DeleteExternalSource:
                return $"the later {later.Operation} removed the object or a container of it; nothing is left to verify.";
            case GenerateBlocksFromSource:
                return "the later generate_blocks_from_source may have created or overwritten the object; it is not re-verified. Re-read it with plc_read.";
            case var operation when FileWrites.Contains(operation, StringComparer.Ordinal):
                return $"the later {later.Operation} wrote the same target; its result reports what is on disk.";
            default:
                return $"the later {later.Operation} is not read back; re-read the object with plc_read.";
        }

        var objectPath = earlier.BlockPath ?? earlier.TypePath
            ?? (earlier.TableName is { } table ? (earlier.FolderPath is null or "/" ? "/" + table : $"{earlier.FolderPath}/{table}") : earlier.FolderPath);
        var (from, to) = (oldPrefix!, newPrefix!);
        var mapped = objectPath is not null && (string.Equals(objectPath, from, StringComparison.OrdinalIgnoreCase)
                || objectPath.StartsWith(from == "/" ? "/" : from + "/", StringComparison.OrdinalIgnoreCase))
            ? to + objectPath[from.Length..]
            : to;
        return $"the later {later.Operation} renamed a container of the object, which is not re-verified; re-read it at '{mapped}' with plc_read.";
    }

    /// <summary>The path a rename produces: the last '/' segment replaced.</summary>
    private static string RenamedPath(string path, string newName) => path[..(path.LastIndexOf('/') + 1)] + newName;

    /// <summary>The requested identity of an item whose PLC could not be verified.</summary>
    private static PlcTargetIdentity? UnresolvedSourceFolderTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => item.Operation switch
        {
            ImportExternalSource or GenerateBlocksFromSource or DeleteExternalSource
                => new(PlcSourceFolderKinds.ExternalSource, plcName, deviceName, UnitFolder(item.UnitName, null), null,
                    item.SourceName ?? (item.FilePath is null ? null : PlcFileTargets.DefaultSourceName(item.FilePath)), null, null),
            RenameBlockGroup => new(PlcNameRules.BlockGroup, plcName, deviceName, null, null, null, item.BlockPath, null),
            RenameTypeGroup => new(PlcSourceFolderKinds.TypeGroup, plcName, deviceName, null, null, null, null, item.TypePath),
            RenameTagTableGroup => new(PlcSourceFolderKinds.TagTableGroup, plcName, deviceName, UnitFolder(item.UnitName, NormalizeFolder(item.FolderPath)), null, null, null, null),
            RenameWatchTableGroup => new(WatchTableGroupKind, plcName, deviceName, WatchGroupPath(item.FolderPath), null, null, null, null),
            CreateTypeGroup or DeleteTypeGroup => new(PlcSourceFolderKinds.TypeGroup, plcName, deviceName, null, null, item.TypePath?.Split('/')[^1], null, item.TypePath),
            CreateTagTableGroup or DeleteTagTableGroup => new(PlcSourceFolderKinds.TagTableGroup, plcName, deviceName, UnitFolder(item.UnitName, NormalizeFolder(item.FolderPath)), null,
                NormalizeFolder(item.FolderPath).Split('/')[^1], null, null),
            CreateSoftwareUnit or DeleteSoftwareUnit or RenameSoftwareUnit => new(PlcSourceFolderKinds.SoftwareUnit, plcName, deviceName, UnitFolder(item.UnitName, null), null, item.UnitName, null, null),
            ExportBlocks => new(PlcSourceFolderKinds.Directory, plcName, deviceName, null, null, null, item.BlockPath, item.TypePath),
            _ when IsWrite(item.Operation) => new(PlcSourceFolderKinds.File, plcName, deviceName, item.FolderPath, item.TableName, item.FilePath, null, null),
            _ => null,
        };

    /// <summary>The note a guard adds when the external source inventory is incomplete, or empty.</summary>
    private string SourcesIncompleteNote
        => _sourcesComplete ? string.Empty
            : $" The external source inventory of PLC '{_plc}' is incomplete{(_sourcesProblem is null ? string.Empty : $" ({_sourcesProblem})")}, so its sources may not all be listed.";

    private PlcExternalSourceInfo RequireSource(PlcOperationRequest item)
    {
        if (_sources is null)
            throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the external sources of PLC '{_plc}', which were not read.");
        // PlcExternalSourceComposition.Find is the worker's lookup; its case rule is undocumented, so the plan matches exactly.
        var matches = _sources.Where(s => string.Equals(s.Name, item.SourceName, StringComparison.Ordinal)
            && Names.Equals(s.UnitName ?? string.Empty, item.UnitName ?? string.Empty)).Take(2).ToList();
        var scope = item.UnitName is null ? $"PLC '{_plc}'" : $"software unit '{item.UnitName}' of PLC '{_plc}'";
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw NotFound(item, $"external source '{item.SourceName}' was not found in {scope} (list them with plc_read list_external_sources).{SourcesIncompleteNote}", !_sourcesComplete),
            _ => throw new ResolutionException(WorkerFailureCategories.TargetAmbiguous, $"Operation '{item.OperationId}': external source '{item.SourceName}' is ambiguous in {scope}."),
        };
    }

    private void RequireFolder(PlcOperationRequest item, HashSet<string>? folders, string? unit, string folder, string what, bool hidden)
    {
        if (folders is null)
            throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the project tree of PLC '{_plc}', which was not read.");
        if (folder == "/")
            throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': the system group cannot be renamed or deleted.");
        if (!folders.Contains(FolderKey(unit, folder)))
            throw NotFound(item, $"{what} '{folder}'{(unit is null ? string.Empty : $" of software unit '{unit}'")} was not found in PLC '{_plc}'.", hidden);
    }

    private Group OwnerOf(PlcOperationRequest item, string? unit)
        => unit is null ? _root : _unitRoots.GetValueOrDefault(unit) ?? throw NotFound(item, $"software unit '{unit}' was not found in PLC '{_plc}'.", HiddenAt(_basePath));

    /// <summary>Replaces <paramref name="group"/> in its parent by a renamed copy whose own and descendants' paths follow the new name.</summary>
    private static Group RenameGroup(Group group, string newName)
    {
        var parent = group.Parent!;
        var renamed = Rebuild(group, parent, newName, $"{parent.Path}/{newName}",
            parent.Selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, newName)).ToArray());
        parent.Groups[parent.Groups.IndexOf(group)] = renamed;
        return renamed;
    }

    private static Group Rebuild(Group old, Group parent, string name, string path, IReadOnlyList<ProjectTreeSelectorSegment> selector)
    {
        var copy = new Group(name, path, parent, selector, old.IsSystem);
        copy.Blocks.AddRange(old.Blocks);
        foreach (var child in old.Groups)
            copy.Groups.Add(Rebuild(child, copy, child.Name, $"{path}/{child.Name}", selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, child.Name)).ToArray()));
        return copy;
    }

    private static void RenameFolder(HashSet<string>? folders, string? unit, string folder, string newName)
    {
        if (folders is null) return;
        var prefix = FolderKey(unit, folder);
        var renamed = FolderKey(unit, Renamed(folder, newName));
        foreach (var key in folders.Where(k => string.Equals(k, prefix, StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            folders.Remove(key);
            folders.Add(renamed + key[prefix.Length..]);
        }
    }

    private string Canonical(PlcGroupPath path)
        => string.Join("/", new[] { _plc }.Concat(path.UnitName is null ? Array.Empty<string>() : new[] { "Units", path.UnitName })
            .Append(path.Kind).Concat(path.Folders));

    private PlcTargetIdentity SourceTarget(string? unit, string name) => new(PlcSourceFolderKinds.ExternalSource, _plc, _device, UnitFolder(unit, null), null, name, null, null);

    private string SourcePlacement(string? unit) => unit is null ? $"{_plc} external sources" : $"{_plc}/Units/{unit} external sources";

    private string SourceRootPlacement(string? unit) => unit is null ? $"{_plc} (root groups)" : $"{_plc}/Units/{unit} (root groups)";

    private static string SourceName(PlcOperationRequest item)
        => string.IsNullOrWhiteSpace(item.SourceName) ? PlcFileTargets.DefaultSourceName(item.FilePath!) : item.SourceName!.Trim();

    private static PlcGroupPath GroupPathOf(string? path)
        => PlcGroupPath.TryParse(path, out var group, out var error) ? group! : throw new ArgumentException(error);

    /// <summary>An external source's or tag-table folder's scope inside <see cref="PlcTargetIdentity.FolderPath"/>: <c>Units/Unit</c> prefixes a software unit's.</summary>
    private static string? UnitFolder(string? unit, string? folder)
        => unit is null ? folder : folder is null ? $"Units/{unit}" : $"Units/{unit}{folder}";

    private static string? UnitOf(string? folderPath)
        => folderPath is not null && folderPath.StartsWith("Units/", StringComparison.Ordinal)
            ? folderPath["Units/".Length..].Split('/')[0] : null;

    private static string FolderOf(string? folderPath)
    {
        if (folderPath is null) return "/";
        if (!folderPath.StartsWith("Units/", StringComparison.Ordinal)) return folderPath;
        var rest = folderPath["Units/".Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? "/" : rest[slash..];
    }

    private static string Renamed(string folder, string newName)
        => folder.LastIndexOf('/') is var slash and > 0 ? folder[..slash] + "/" + newName : "/" + newName;

    private static bool WithinFolder(string candidate, string folder)
        => Names.Equals(candidate, folder) || candidate.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    private static string FolderKey(string? unit, string folder) => (unit ?? string.Empty) + "\n" + folder;

    private static string FolderTouchKey(string kind, string? unit, string folder) => $"{kind}:{(unit ?? string.Empty).ToUpperInvariant()}\n{folder.ToUpperInvariant()}";

    private static string SourceKey(string? unit, string name) => $"source:{(unit ?? string.Empty).ToUpperInvariant()}\n{name}";

    private static string FileKey(string path) => "file:" + PlcFileTargets.Canonical(path).ToUpperInvariant();
}

/// <summary>The info guards of the external-source, group-rename and export operations.</summary>
public static class PlcSourceFolderGuards
{
    public const string GeneratesFromSource = "plc_generates_from_source";
    public const string DeletesExternalSource = "plc_deletes_external_source";
    public const string RenamesGroup = "plc_renames_group";
    public const string ExportOverwritesFiles = "plc_export_overwrites_files";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = new[]
    {
        new WriteGuardDefinition(GeneratesFromSource, WriteGuardSeverities.Info, "generate_blocks_from_source creates or overwrites the blocks and PLC data types the external source declares; GenerateBlockOption.None deletes what it generated on an error, KeepOnError keeps objects that contain errors."),
        new WriteGuardDefinition(DeletesExternalSource, WriteGuardSeverities.Info, "Deleting an external source removes the source node only; blocks and PLC data types generated from it stay."),
        new WriteGuardDefinition(RenamesGroup, WriteGuardSeverities.Info, "Renaming a group changes the paths (blockPath, typePath or folderPath) of everything inside it; object names and references between objects do not change."),
        new WriteGuardDefinition(ExportOverwritesFiles, WriteGuardSeverities.Info, "The export replaces existing files because overwrite:true was requested."),
    };
}
