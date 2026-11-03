using System.Globalization;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The planner's in-call picture of one PLC: its tag inventory and, only when an item needs it, its
/// block tree, with every earlier planned item applied. Update and delete targets are resolved
/// (missing or ambiguous is an error); creates are never checked against existing state, Portal
/// rejects them. Incomplete or unreadable evidence only fires <see cref="PlcGuardDefinitions.StateUnverifiable"/>.
/// </summary>
internal sealed partial class PlcWorkingState
{
    private sealed record Table(string Folder, string Name, List<TagInfo> Tags, List<UserConstantInfo> Constants);

    private sealed record Block(string Name, string Type, string? Language, bool IsSystem, int? Number);

    private sealed record Group(string Name, string Path, Group? Parent, IReadOnlyList<ProjectTreeSelectorSegment> Selector, bool IsSystem)
    {
        public List<Group> Groups { get; } = new();
        public List<Block> Blocks { get; } = new();
    }

    /// <summary><paramref name="hidden"/>: the miss may be caused by unreadable evidence, so it is not proof of absence.</summary>
    private sealed class ResolutionException(string category, string message, bool hidden = false) : Exception(message)
    {
        public string Category { get; } = category;
        public bool Hidden { get; } = hidden;
    }

    private static readonly StringComparer Names = PlcNameRules.Comparer;
    private readonly string _plc;
    private readonly string? _device;
    private readonly bool _inventoryComplete;
    private readonly List<Table> _tables;
    private readonly bool _hasTree;
    private readonly Group _root;
    private readonly Dictionary<string, Group> _unitRoots = new(Names);
    private readonly IReadOnlyList<ProjectTreeSkippedNodeInfo> _skipped;
    private readonly IReadOnlyList<ProjectTreeSelectorSegment> _basePath;
    private readonly Dictionary<string, (int Sequence, string OperationId)> _touches = new(StringComparer.Ordinal);
    private int _sequence;

    private PlcWorkingState(PlcTagInventoryPlcInfo plc, bool inventoryComplete, ProjectTreeObservation? tree)
    {
        _plc = plc.PlcName;
        _device = plc.DeviceName;
        _inventoryComplete = inventoryComplete;
        _tables = plc.Tables.Select(t => new Table(NormalizeFolder(t.FolderPath), t.Name,
            t.Tags.Select(Copy).ToList(),
            t.UserConstants.Select(c => new UserConstantInfo { Name = c.Name, DataType = c.DataType, Value = c.Value }).ToList())).ToList();

        var basePath = new[] { Segment(ProjectTreeNodeTypes.Device, _device ?? string.Empty), Segment(ProjectTreeNodeTypes.PlcSoftware, _plc) };
        _basePath = basePath;
        _hasTree = tree is not null;
        _skipped = tree?.Skipped.Where(s => s.ParentPath.Count >= 2 && SameSelector(s.ParentPath.Take(2).ToArray(), basePath)).ToArray()
            ?? Array.Empty<ProjectTreeSkippedNodeInfo>();
        var software = tree?.Roots.FirstOrDefault(r => r.NodeType == ProjectTreeNodeTypes.PlcSoftware && Names.Equals(r.Name, _plc));
        var children = software?.Children ?? new List<ProjectTreeNode>();
        _root = OwnerRoot(children, $"{_plc}/Blocks", basePath);
        foreach (var unit in children.Where(c => c.NodeType == ProjectTreeNodeTypes.SoftwareUnit))
            _unitRoots[unit.Name] = OwnerRoot(unit.Children ?? new(), $"{_plc}/Units/{unit.Name}/Blocks",
                basePath.Append(Segment(ProjectTreeNodeTypes.SoftwareUnit, unit.Name)).ToArray());
    }

    /// <param name="tree">The PLC's project tree, or null when no item of the call needs it (see <see cref="NeedsTree"/>).</param>
    public static PlcWorkingState Create(PlcTagInventoryPlcInfo plc, bool inventoryComplete, ProjectTreeObservation? tree)
        => new(plc, inventoryComplete, tree);

    /// <summary>
    /// Block deletes resolve their target and enumerate what they remove from the tree; an OB create
    /// needs the OB inventory for <see cref="PlcGuardDefinitions.ObSingletonExists"/>. Nothing else reads it.
    /// </summary>
    public static bool NeedsTree(PlcOperationRequest item)
        => item.Operation is "delete_block" or "delete_block_group"
            or PlcBlockEditMethods.RenameBlock or PlcBlockEditMethods.RenumberBlock or PlcBlockEditMethods.CopyBlock or PlcBlockEditMethods.MoveBlock
            || item.Operation == "create_block" && IsOb(item)
            || item.Operation == PlcBlockEditMethods.RenameObjects
            || item.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument
            || NeedsSourceFolderTree(item);

    public PlcResolution Resolve(PlcOperationRequest item)
    {
        var guards = new List<FiredGuard>();
        if (!_inventoryComplete)
            guards.Add(Fire(StateUnverifiable, item, $"The tag inventory is incomplete, so the state of PLC '{_plc}' cannot be verified. Inspect the list_tag_tables messages."));
        try
        {
            return new PlcResolution(ResolveEffect(item, guards), guards, null);
        }
        catch (ResolutionException ex) when (ex.Hidden && ex.Category == WorkerFailureCategories.TargetNotFound)
        {
            // Unreadable evidence never becomes absence: the call is blocked instead of failed.
            if (!guards.Any(g => g.Id == StateUnverifiable))
                guards.Add(Fire(StateUnverifiable, item, $"{ex.Message} Part of the evidence is unreadable, so the target may exist but cannot be verified."));
            return new PlcResolution(Unresolved(item, _plc, _device), guards, null);
        }
        catch (ResolutionException ex)
        {
            return new PlcResolution(null, Array.Empty<FiredGuard>(), new WriteToolError(ex.Category, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return new PlcResolution(null, Array.Empty<FiredGuard>(), new WriteToolError(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {ex.Message}"));
        }
    }

    /// <summary>Applies a resolvable item's creation, deletion, rename or value change to the working state.</summary>
    public void Apply(PlcOperationRequest item)
    {
        if (Resolve(item).Error is not null) return;
        _sequence++;
        try
        {
            ApplyResolved(item);
        }
        catch (ResolutionException)
        {
            // An unverifiable target (its block guard stops the call) or a create into a parent the
            // tree lacks (Portal rejects it at mutation) is not applied; its touches are already recorded.
        }
    }

    /// <summary>The requested identity of an item whose target could not be verified.</summary>
    public static PlcWriteEffect Unresolved(PlcOperationRequest item, string plcName, string? deviceName)
    {
        if ((UnresolvedWatchTarget(item, plcName, deviceName) ?? UnresolvedSourceFolderTarget(item, plcName, deviceName)
                ?? UnresolvedAlarmTarget(item, plcName, deviceName)) is { } watchTarget)
            return new PlcWriteEffect(item.Operation, watchTarget, Array.Empty<PlcFieldChange>(), null, null, null, null);
        if (PlcSafetyMethods.IsWrite(item.Operation))
            return new PlcWriteEffect(item.Operation, new PlcTargetIdentity(PlcSafetyMethods.IsRuntimeGroupWrite(item.Operation) ? PlcSafetyPlanning.RuntimeGroupKind : PlcSafetyPlanning.AdministrationKind,
                plcName, deviceName, null, null, item.RuntimeGroupName, null, null), Array.Empty<PlcFieldChange>(), null, null, null, null);
        if (UnresolvedEditingTarget(item, plcName, deviceName) is { } editingTarget)
            return new PlcWriteEffect(item.Operation, editingTarget, Array.Empty<PlcFieldChange>(), null, null, null, null);
        if (UnresolvedTechnologyTarget(item, plcName, deviceName) is { } technologyTarget)
            return new PlcWriteEffect(item.Operation, technologyTarget, Array.Empty<PlcFieldChange>(), null, null, null, null);
        if (UnresolvedSecurityTarget(item, plcName, deviceName) is { } securityTarget)
            return new PlcWriteEffect(item.Operation, securityTarget, Array.Empty<PlcFieldChange>(), null, null, null, null);
        if (item.Operation == PlcSimaticSdOperation && SimaticSdBlockDocumentName(item) is { } documentBlock)
            return new PlcWriteEffect(item.Operation, new PlcTargetIdentity(PlcNameRules.Block, plcName, deviceName, null, null, documentBlock,
                $"{item.TargetGroupPath?.TrimEnd('/')}/{documentBlock}", null), Array.Empty<PlcFieldChange>(), null, null, null, null);
        var tableOperation = item.Operation.Contains("tag_table", StringComparison.Ordinal);
        var target = item switch
        {
            { BlockPath: { } path } => new PlcTargetIdentity(item.Operation.Contains("group", StringComparison.Ordinal) ? PlcNameRules.BlockGroup : PlcNameRules.Block,
                plcName, deviceName, null, null, path.Split('/')[^1], path, null),
            { TypePath: { } type } => new PlcTargetIdentity("Type", plcName, deviceName, null, null, type.Split('/')[^1], null, type),
            _ => new PlcTargetIdentity(tableOperation ? PlcNameRules.TagTable : item.Operation.Contains("user_constant", StringComparison.Ordinal) ? PlcNameRules.UserConstant : PlcNameRules.Tag,
                plcName, deviceName, NormalizeFolder(item.FolderPath), item.TableName, tableOperation ? null : item.Name, null, null),
        };
        return new PlcWriteEffect(item.Operation, target, Array.Empty<PlcFieldChange>(), null, null, null, null);
    }

    private void ApplyResolved(PlcOperationRequest item)
    {
        switch (item.Operation)
        {
            case "create_tag_table":
                _tables.Add(new Table(NormalizeFolder(item.FolderPath), item.TableName!, new(), new()));
                Touch(TableKey(item.TableName!), item);
                break;
            case "delete_tag_table":
                var removed = RequireTable(item);
                _tables.Remove(removed);
                Touch(TableKey(removed.Name), item);
                foreach (var name in removed.Tags.Select(t => t.Name).Concat(removed.Constants.Select(c => c.Name))) Touch(CpuKey(name), item);
                break;
            case "create_tag":
                // ponytail: new tags assume TIA's default external-access flags (all true).
                RequireTable(item).Tags.Add(new TagInfo { Name = item.Name!, DataType = item.DataType!, LogicalAddress = item.LogicalAddress ?? string.Empty,
                    ExternalAccessible = item.ExternalAccessible ?? true, ExternalVisible = item.ExternalVisible ?? true, ExternalWritable = item.ExternalWritable ?? true });
                Touch(CpuKey(item.Name!), item);
                break;
            case "update_tag":
                var tag = RequireTag(item, RequireTable(item));
                tag.DataType = item.DataType ?? tag.DataType;
                tag.LogicalAddress = item.LogicalAddress ?? tag.LogicalAddress;
                tag.ExternalAccessible = item.ExternalAccessible ?? tag.ExternalAccessible;
                tag.ExternalVisible = item.ExternalVisible ?? tag.ExternalVisible;
                tag.ExternalWritable = item.ExternalWritable ?? tag.ExternalWritable;
                if (item.NewName is { } newName)
                {
                    Touch(CpuKey(tag.Name), item);
                    Touch(CpuKey(newName), item);
                    tag.Name = newName;
                }
                break;
            case "delete_tag":
                var tags = RequireTable(item).Tags;
                tags.Remove(RequireTag(item, RequireTable(item)));
                Touch(CpuKey(item.Name!), item);
                break;
            case PlcTagMoveMethods.MoveTag:
                var from = RequireTable(item);
                var moving = RequireTag(item, from);
                var to = RequireTargetTable(item);
                from.Tags.Remove(moving);
                to.Tags.Add(moving);
                Touch(CpuKey(moving.Name), item);
                break;
            case "create_user_constant":
                RequireTable(item).Constants.Add(new UserConstantInfo { Name = item.Name!, DataType = item.DataType!, Value = item.Value });
                Touch(CpuKey(item.Name!), item);
                break;
            case "update_user_constant":
                var constant = RequireConstant(item, RequireTable(item));
                constant.DataType = item.DataType ?? constant.DataType;
                constant.Value = item.Value ?? constant.Value;
                break;
            case "delete_user_constant":
                RequireTable(item).Constants.Remove(RequireConstant(item, RequireTable(item)));
                Touch(CpuKey(item.Name!), item);
                break;
            case "create_block":
                var address = BlockAddress.Parse(item.BlockPath!);
                Touch(CpuKey(address.BlockName), item);
                // Without a tree no later item reads blocks; a missing parent is Portal's rejection, never a plan error.
                if (_hasTree)
                    CreationParent(item, address).Blocks.Add(new Block(address.BlockName, BlockNodeType(item.BlockType!),
                        item.BlockType == "GlobalDB" ? "DB" : item.Language ?? "LAD", false, IsOb(item) ? PlanObNumber(ObClass(item)) : null));
                break;
            case "delete_block":
                var (group, block) = FindBlock(item, BlockAddress.Parse(item.BlockPath!));
                group.Blocks.Remove(block);
                Touch(CpuKey(block.Name), item);
                break;
            case "create_block_group":
                var target = BlockAddress.Parse(item.BlockPath!);
                Touch(GroupKey(PlannedPath(target)), item);
                if (!_hasTree) break;
                var parent = CreationParent(item, target);
                parent.Groups.Add(new Group(target.BlockName, $"{parent.Path}/{target.BlockName}", parent,
                    parent.Selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, target.BlockName)).ToArray(), false));
                break;
            case PlcBlockEditMethods.AddInterfaceMember:
            case PlcBlockEditMethods.UpdateInterfaceMember:
            case PlcBlockEditMethods.DeleteInterfaceMember:
            case PlcBlockEditMethods.UpdateDbMember:
            case PlcBlockEditMethods.SetNetworkText:
            case PlcBlockEditMethods.ReplaceNetworkText:
            case PlcBlockEditMethods.RenameMembers:
            case Contracts.Plc.SimaticSd.SimaticSdMethods.InsertNetwork:
            case Contracts.Plc.SimaticSd.SimaticSdMethods.ReplaceNetwork:
            case Contracts.Plc.SimaticSd.SimaticSdMethods.DeleteNetwork:
                // A later item on the same block or type is re-planned against the real state.
                if (item.TypePath is { } typePath) Touch(TypeKey(PlcTypeAddress.Parse(typePath).TypeName), item);
                else Touch(CpuKey(BlockAddress.Parse(item.BlockPath!).BlockName), item);
                break;
            case PlcBlockEditMethods.CreateInstanceDb:
                var instance = BlockAddress.Parse(item.BlockPath!);
                Touch(CpuKey(instance.BlockName), item);
                if (_hasTree)
                    CreationParent(item, instance).Blocks.Add(new Block(instance.BlockName, ProjectTreeNodeTypes.InstanceDb, "DB", false, item.Number));
                break;
            case PlcBlockEditMethods.RenameBlock:
            {
                var (owner, renamed) = FindBlock(item, BlockAddress.Parse(item.BlockPath!));
                owner.Blocks[owner.Blocks.IndexOf(renamed)] = renamed with { Name = item.NewName! };
                Touch(CpuKey(renamed.Name), item);
                Touch(CpuKey(item.NewName!), item);
                break;
            }
            case PlcBlockEditMethods.RenumberBlock:
            {
                var (owner, renumbered) = FindBlock(item, BlockAddress.Parse(item.BlockPath!));
                owner.Blocks[owner.Blocks.IndexOf(renumbered)] = renumbered with { Number = item.AutoNumber == true ? null : item.Number };
                Touch(CpuKey(renumbered.Name), item);
                break;
            }
            case PlcBlockEditMethods.CopyBlock:
            {
                var (owner, original) = FindBlock(item, BlockAddress.Parse(item.BlockPath!));
                Touch(CpuKey(item.NewName!), item);
                var into = item.TargetGroupPath is null ? owner : FindGroupByPath(TargetGroupPath(item.TargetGroupPath));
                into?.Blocks.Add(original with { Name = item.NewName!, Number = item.Number });
                break;
            }
            case PlcBlockEditMethods.MoveBlock:
            {
                var (owner, movedBlock) = FindBlock(item, BlockAddress.Parse(item.BlockPath!));
                Touch(CpuKey(movedBlock.Name), item);
                if (FindGroupByPath(TargetGroupPath(item.TargetGroupPath!)) is { } into)
                {
                    owner.Blocks.Remove(movedBlock);
                    into.Blocks.Add(movedBlock);
                }

                break;
            }
            case PlcBlockEditMethods.MoveTagTable:
            {
                var table = RequireTable(item);
                _tables[_tables.IndexOf(table)] = table with { Folder = NormalizeFolder(item.TargetFolderPath) };
                Touch(TableKey(table.Name), item);
                foreach (var name in table.Tags.Select(t => t.Name).Concat(table.Constants.Select(c => c.Name))) Touch(CpuKey(name), item);
                break;
            }
            case "delete_block_group":
                var doomed = DeletionGroup(item, BlockAddress.Parse(item.BlockPath!));
                doomed.Parent!.Groups.Remove(doomed);
                foreach (var g in Descendants(doomed).Prepend(doomed)) Touch(GroupKey(g.Path), item);
                foreach (var b in DescendantBlocks(doomed)) Touch(CpuKey(b.Block.Name), item);
                break;
            default:
                if (IsAlarmOperation(item.Operation))
                {
                    ApplyAlarm(item);
                }
                else
                {
                    ApplyWatch(item);
                    ApplySourceFolder(item);
                    ApplyTechnology(item);
                    ApplyEditing(item);
                    ApplySimaticSd(item);
                }

                break;
        }
    }

    /// <summary>
    /// The latest earlier item that created, deleted or renamed the target or a container it needs;
    /// a move_tag also needs its target table, which an earlier item may have created.
    /// </summary>
    public string? LastTouchedBy(PlcTargetIdentity target, PlcOperationRequest? item = null)
    {
        IEnumerable<string> keys = target.Kind switch
        {
            PlcNameRules.Tag or PlcNameRules.UserConstant => new[] { CpuKey(target.Name!), TableKey(target.TableName!) },
            PlcNameRules.TagTable => new[] { TableKey(target.TableName!) },
            PlcNameRules.Block => GroupKeys(ParentPath(target.BlockPath!)).Append(CpuKey(target.Name!)),
            PlcNameRules.BlockGroup => GroupKeys(target.BlockPath!),
            "Type" => new[] { TypeKey(target.Name!) },
            _ => WatchKeys(target).Concat(SourceFolderKeys(target)).Concat(AlarmKeys(target)).Concat(TechnologyKeys(target, item)).Concat(EditingKeys(target, item)),
        };
        if (item?.Operation == PlcTagMoveMethods.MoveTag && item.TargetTableName is { } targetTable) keys = keys.Append(TableKey(targetTable));
        keys = keys.Concat(ContainerDependencyKeys(target, item));
        return keys.Where(_touches.ContainsKey).Select(k => _touches[k]).OrderByDescending(t => t.Sequence)
            .Select(t => t.OperationId).FirstOrDefault();
    }

    private PlcWriteEffect ResolveEffect(PlcOperationRequest item, List<FiredGuard> guards)
    {
        switch (item.Operation)
        {
            case "create_tag_table":
            {
                var folder = NormalizeFolder(item.FolderPath);
                return Effect(item, new(PlcNameRules.TagTable, _plc, _device, folder, item.TableName, null, null, null), Change("tableName", null, item.TableName), folder);
            }
            case "delete_tag_table":
            {
                var table = RequireTable(item);
                if (table.Tags.Count + table.Constants.Count > 0)
                    guards.Add(Fire(DeletesTableContents, item, $"Deleting tag table '{table.Name}' in '{table.Folder}' removes {table.Tags.Count} tags and {table.Constants.Count} user constants."));
                return Effect(item, TableTarget(table), Array.Empty<PlcFieldChange>(), null, new(table.Tags.Count, table.Constants.Count, Array.Empty<string>(), Array.Empty<string>(), null, null));
            }
            case "create_tag":
            case "create_user_constant":
            {
                var table = RequireTable(item);
                if (item.LogicalAddress is not null) AddressOverlapGuard(item, null, guards);
                var kind = item.Operation == "create_tag" ? PlcNameRules.Tag : PlcNameRules.UserConstant;
                return Effect(item, MemberTarget(kind, table, item.Name!),
                    Change("name", null, item.Name).Concat(Change("dataType", null, item.DataType)).Concat(Change("logicalAddress", null, item.LogicalAddress))
                        .Concat(Change("value", null, item.Value)).Concat(Change($"comment[{item.CommentCulture}]", null, item.Comment)).ToArray(), TablePath(table));
            }
            case "update_tag":
            {
                var table = RequireTable(item);
                var tag = RequireTag(item, table);
                if (item.LogicalAddress is not null) AddressOverlapGuard(item, tag, guards);
                return Effect(item, MemberTarget(PlcNameRules.Tag, table, tag.Name),
                    Change("name", tag.Name, item.NewName).Concat(Change("dataType", tag.DataType, item.DataType))
                        .Concat(Change("logicalAddress", tag.LogicalAddress, item.LogicalAddress))
                        .Concat(Change("externalAccessible", Flag(tag.ExternalAccessible), Flag(item.ExternalAccessible)))
                        .Concat(Change("externalVisible", Flag(tag.ExternalVisible), Flag(item.ExternalVisible)))
                        .Concat(Change("externalWritable", Flag(tag.ExternalWritable), Flag(item.ExternalWritable)))
                        .Concat(Change($"comment[{item.CommentCulture}]", null, item.Comment)).ToArray(), null);
            }
            case "delete_tag":
            {
                var table = RequireTable(item);
                var tag = RequireTag(item, table);
                return Effect(item, MemberTarget(PlcNameRules.Tag, table, tag.Name), Array.Empty<PlcFieldChange>(), null,
                    new(1, 0, Array.Empty<string>(), Array.Empty<string>(), null, null));
            }
            case PlcTagMoveMethods.MoveTag:
            {
                var table = RequireTable(item);
                var tag = RequireTag(item, table);
                var target = RequireTargetTable(item);
                if (ReferenceEquals(table, target))
                    throw new ResolutionException(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': tag '{tag.Name}' is already in tag table '{target.Name}' ('{target.Folder}').");
                guards.Add(Fire(MoveTagRecreates, item, $"Moving tag '{tag.Name}' from '{TablePath(table)}' to '{TablePath(target)}' re-creates it: "
                    + "DataTypeName, LogicalAddress, the three external-access flags and the multilingual comment (each language the new tag has a text item for) are copied, "
                    + "other attributes are not; the source is deleted after the copy is created "
                    + "and the copy then takes the original name. Run compile_check afterwards so blocks re-bind the symbol."));
                return Effect(item, MemberTarget(PlcNameRules.Tag, table, tag.Name),
                    Change("tableName", table.Name, target.Name).Concat(Change("folderPath", table.Folder, target.Folder == table.Folder ? null : target.Folder)).ToArray(),
                    TablePath(target));
            }
            case "update_user_constant":
            case "delete_user_constant":
            {
                var table = RequireTable(item);
                var constant = RequireConstant(item, table);
                var delete = item.Operation == "delete_user_constant";
                // Only the update's effect reports the value it replaces; a delete needs no value.
                if (!delete && constant.Value is null)
                    guards.Add(Fire(StateUnverifiable, item, $"The value of user constant '{constant.Name}' is unreadable, so its current state cannot be verified."));
                return Effect(item, MemberTarget(PlcNameRules.UserConstant, table, constant.Name),
                    delete ? Array.Empty<PlcFieldChange>() : Change("dataType", constant.DataType, item.DataType).Concat(Change("value", constant.Value, item.Value)).ToArray(),
                    null, delete ? new(0, 1, Array.Empty<string>(), Array.Empty<string>(), null, null) : null);
            }
            case "create_block":
            {
                var address = BlockAddress.Parse(item.BlockPath!);
                var path = PlannedPath(address);
                var cls = IsOb(item) ? ObClass(item) : null;
                string? number = null;
                if (cls is not null && cls.IsSingleton && ObBlocks().FirstOrDefault(o => o.Block.Number == cls.BaseNumber) is { Block: not null } holder)
                    guards.Add(Fire(ObSingletonExists, item, $"OB number {cls.BaseNumber} of singleton event class '{cls.Name}' is already held by '{holder.Group.Path}/{holder.Block.Name}'; only one OB of that class can exist."));
                else if (cls is not null)
                    number = (PlanObNumber(cls) ?? throw new ArgumentException($"no OB number from {ObNumberRules.FirstFree} to {ObNumberRules.Max} is free in PLC '{_plc}'."))
                        .ToString(CultureInfo.InvariantCulture);
                return Effect(item, new(PlcNameRules.Block, _plc, _device, null, null, address.BlockName, path, null),
                    Change("blockType", null, item.BlockType).Concat(Change("language", null, item.Language)).Concat(Change("obEventClass", null, cls?.Name))
                        .Concat(Change("number", null, number)).ToArray(),
                    ParentPath(path));
            }
            case "update_block_logic":
            {
                // The export resolves the block (target_not_found/target_ambiguous); the tree is not read.
                var address = BlockAddress.Parse(item.BlockPath!);
                var path = address.IsDeterministic ? PlannedPath(address) : $"{_plc}/{address.BlockName}";
                return Effect(item, new(PlcNameRules.Block, _plc, _device, null, null, address.BlockName, path, null), Array.Empty<PlcFieldChange>(), null);
            }
            case "delete_block":
            {
                var (group, block) = FindBlock(item, BlockAddress.Parse(item.BlockPath!), guards);
                var path = $"{group.Path}/{block.Name}";
                var target = new PlcTargetIdentity(PlcNameRules.Block, _plc, _device, null, null, block.Name, path, null);
                guards.Add(Fire(DeletesBlock, item, $"Deleting block '{path}' ({block.Type}, {block.Language ?? "unknown language"}) removes it and its content."));
                return Effect(item, target, Array.Empty<PlcFieldChange>(), null, new(0, 0, new[] { path }, Array.Empty<string>(), block.Type, block.Language));
            }
            case "create_block_group":
            {
                var address = BlockAddress.Parse(item.BlockPath!);
                var path = PlannedPath(address);
                return Effect(item, new(PlcNameRules.BlockGroup, _plc, _device, null, null, address.BlockName, path, null),
                    Change("name", null, address.BlockName), ParentPath(path));
            }
            case "delete_block_group":
            {
                var group = DeletionGroup(item, BlockAddress.Parse(item.BlockPath!));
                var blocks = DescendantBlocks(group).Select(b => $"{b.Group.Path}/{b.Block.Name}").ToArray();
                var groups = Descendants(group).Select(g => g.Path).ToArray();
                if (blocks.Length + groups.Length > 0)
                    guards.Add(Fire(DeletesGroupContents, item, $"Deleting block group '{group.Path}' removes {blocks.Length} blocks and {groups.Length} groups; the full list is in the effect's removes."));
                if (_skipped.Any(s => StartsWith(s.ParentPath, group.Selector))) guards.Add(SkippedGuard(item));
                return Effect(item, new(PlcNameRules.BlockGroup, _plc, _device, null, null, group.Name, group.Path, null), Array.Empty<PlcFieldChange>(), null,
                    new(0, 0, blocks, groups, null, null));
            }
            case PlcBlockEditMethods.AddInterfaceMember:
            case PlcBlockEditMethods.UpdateInterfaceMember:
            case PlcBlockEditMethods.DeleteInterfaceMember:
            case PlcBlockEditMethods.UpdateDbMember:
            case PlcBlockEditMethods.SetNetworkText:
            case PlcBlockEditMethods.ReplaceNetworkText:
            case PlcBlockEditMethods.RenameMembers:
            case Contracts.Plc.SimaticSd.SimaticSdMethods.InsertNetwork:
            case Contracts.Plc.SimaticSd.SimaticSdMethods.ReplaceNetwork:
            case Contracts.Plc.SimaticSd.SimaticSdMethods.DeleteNetwork:
            {
                // The planner reads the document or values and fills in the changes.
                if (item.TypePath is { } typePath)
                {
                    var type = PlcTypeAddress.Parse(typePath);
                    return Effect(item, new("Type", _plc, _device, null, null, type.TypeName, null, typePath), Array.Empty<PlcFieldChange>(), null);
                }

                var address = BlockAddress.Parse(item.BlockPath!);
                var path = address.IsDeterministic ? PlannedPath(address) : $"{_plc}/{address.BlockName}";
                return Effect(item, new(PlcNameRules.Block, _plc, _device, null, null, address.BlockName, path, null), Array.Empty<PlcFieldChange>(), null);
            }
            case PlcBlockEditMethods.CreateInstanceDb:
            {
                var address = BlockAddress.Parse(item.BlockPath!);
                var path = PlannedPath(address);
                return Effect(item, new(PlcNameRules.Block, _plc, _device, null, null, address.BlockName, path, null),
                    Change("blockType", null, "InstanceDB").Concat(Change("instanceOfName", null, item.InstanceOfName))
                        .Concat(Change("number", null, NumberText(item.Number))).ToArray(),
                    ParentPath(path));
            }
            case PlcBlockEditMethods.RenameBlock:
            case PlcBlockEditMethods.RenumberBlock:
            case PlcBlockEditMethods.CopyBlock:
            case PlcBlockEditMethods.MoveBlock:
            {
                var (group, block) = FindBlock(item, BlockAddress.Parse(item.BlockPath!), guards);
                var path = $"{group.Path}/{block.Name}";
                var target = new PlcTargetIdentity(PlcNameRules.Block, _plc, _device, null, null, block.Name, path, null);
                switch (item.Operation)
                {
                    case PlcBlockEditMethods.RenameBlock:
                        return Effect(item, target, Change("name", block.Name, item.NewName), null);
                    case PlcBlockEditMethods.RenumberBlock:
                        return Effect(item, target, new[] { new PlcFieldChange("number", NumberText(block.Number), item.AutoNumber == true ? "auto" : NumberText(item.Number)) }, null);
                    case PlcBlockEditMethods.CopyBlock:
                    {
                        var into = item.TargetGroupPath is null ? group.Path : TargetGroupPath(item.TargetGroupPath);
                        guards.Add(Fire(BlockRoundTrip, item, $"copy_block imports the SimaticML export of '{path}' as '{into}/{item.NewName}': a new block with its own identity and no library-type link."));
                        return Effect(item, target, Change("copy", null, $"{into}/{item.NewName}").Concat(Change("number", null, NumberText(item.Number))).ToArray(), into);
                    }
                    default:
                    {
                        var into = TargetGroupPath(item.TargetGroupPath!);
                        guards.Add(Fire(BlockRoundTrip, item, $"move_block exports '{path}', deletes it and imports the export into '{into}': the block gets a new object identity, a library-type link is not carried over, and instance DBs and callers resolve it again by name at the next compile. A failed import re-imports it into '{group.Path}'."));
                        return Effect(item, target, Change("blockPath", path, $"{into}/{block.Name}"), into);
                    }
                }
            }
            case PlcBlockEditMethods.MoveTagTable:
            {
                var table = RequireTable(item);
                var into = NormalizeFolder(item.TargetFolderPath);
                guards.Add(Fire(MovesTagTable, item, $"move_tag_table exports '{TablePath(table)}' ({table.Tags.Count} tags, {table.Constants.Count} user constants), deletes it and imports the export into '{into}'; a failed import re-imports it into '{table.Folder}'."));
                return Effect(item, TableTarget(table), Change("folderPath", table.Folder, into), into);
            }
            case "update_type_content":
            {
                var address = PlcTypeAddress.Parse(item.TypePath!);
                return Effect(item, new("Type", _plc, _device, null, null, address.TypeName, null, item.TypePath), Array.Empty<PlcFieldChange>(), null);
            }
            default:
                if (IsWatchOperation(item.Operation)) return ResolveWatch(item, guards);
                if (HandlesSourceFolder(item.Operation)) return ResolveSourceFolder(item, guards);
                if (IsAlarmOperation(item.Operation)) return ResolveAlarm(item, guards);
                if (IsTechnologyOperation(item.Operation)) return ResolveTechnology(item, guards);
                if (IsSecurityOperation(item.Operation)) return ResolveSecurity(item, guards);
                if (IsSafetyOperation(item.Operation)) return ResolveSafety(item);
                if (IsEditingOperation(item.Operation)) return ResolveEditing(item, guards);
                if (IsSimaticSdOperation(item.Operation)) return ResolveSimaticSd(item, guards);
                throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Unsupported PLC write operation '{item.Operation}'.");
        }
    }

    private Table RequireTable(PlcOperationRequest item)
    {
        var folder = NormalizeFolder(item.FolderPath);
        return _tables.FirstOrDefault(t => Names.Equals(t.Folder, folder) && Names.Equals(t.Name, item.TableName))
            ?? throw NotFound(item, $"tag table '{item.TableName}' was not found in '{folder}' of PLC '{_plc}'.", !_inventoryComplete);
    }

    private Table RequireTargetTable(PlcOperationRequest item)
    {
        var folder = NormalizeFolder(item.TargetFolderPath);
        return _tables.FirstOrDefault(t => Names.Equals(t.Folder, folder) && Names.Equals(t.Name, item.TargetTableName))
            ?? throw NotFound(item, $"target tag table '{item.TargetTableName}' was not found in '{folder}' of PLC '{_plc}'.", !_inventoryComplete);
    }

    private TagInfo RequireTag(PlcOperationRequest item, Table table)
        => table.Tags.FirstOrDefault(t => Names.Equals(t.Name, item.Name))
            ?? throw NotFound(item, $"tag '{item.Name}' was not found in tag table '{table.Name}'.", !_inventoryComplete);

    private UserConstantInfo RequireConstant(PlcOperationRequest item, Table table)
        => table.Constants.FirstOrDefault(c => Names.Equals(c.Name, item.Name))
            ?? throw NotFound(item, $"user constant '{item.Name}' was not found in tag table '{table.Name}'.", !_inventoryComplete);

    private void AddressOverlapGuard(PlcOperationRequest item, TagInfo? self, List<FiredGuard> guards)
    {
        var wanted = NormalizeAddress(item.LogicalAddress);
        // ponytail: exact address equality; range overlap (%IW0 vs %I0.0) is not computed.
        var other = _tables.SelectMany(t => t.Tags.Select(tag => (Table: t, Tag: tag)))
            .FirstOrDefault(x => !ReferenceEquals(x.Tag, self) && wanted.Length > 0 && NormalizeAddress(x.Tag.LogicalAddress) == wanted);
        if (other.Tag is not null)
            guards.Add(Fire(AddressOverlap, item, $"Logical address '{item.LogicalAddress}' is already used by tag '{other.Tag.Name}' in '{TablePath(other.Table)}'."));
    }

    private (Group Group, Block Block) FindBlock(PlcOperationRequest item, BlockAddress address, List<FiredGuard>? guards = null)
    {
        RequireTree(item);
        if (address.IsDeterministic)
        {
            var group = ResolveGroup(item, Owner(item, address), address.FolderPath);
            var block = group.Blocks.FirstOrDefault(b => Names.Equals(b.Name, address.BlockName))
                ?? throw NotFound(item, $"block '{address.BlockName}' was not found at '{group.Path}'.", HiddenAt(group.Selector));
            return (group, block);
        }

        // A unique lookup across the PLC; a skipped block could be a hidden second match.
        if (_skipped.Any(IsBlockTreeNode)) guards?.Add(SkippedGuard(item));
        var matches = _unitRoots.Values.Prepend(_root).SelectMany(UserBlocks).Where(b => Names.Equals(b.Block.Name, address.BlockName)).ToArray();
        return matches.Length switch
        {
            0 => throw NotFound(item, $"block '{address.BlockName}' was not found in PLC '{_plc}'.", _skipped.Any(IsBlockTreeNode)),
            1 => matches[0],
            _ => throw new ResolutionException(WorkerFailureCategories.TargetAmbiguous,
                $"Operation '{item.OperationId}': block '{address.BlockName}' is ambiguous in PLC '{_plc}'. Use the deterministic path, for example '{matches[0].Group.Path}/{address.BlockName}'."),
        };
    }

    private Group CreationParent(PlcOperationRequest item, BlockAddress address)
        => address.IsDeterministic ? ResolveGroup(item, Owner(item, address), address.FolderPath) : _root;

    private Group DeletionGroup(PlcOperationRequest item, BlockAddress address)
    {
        RequireTree(item);
        return address.IsDeterministic
            ? ResolveGroup(item, Owner(item, address), address.FolderPath.Append(address.BlockName).ToArray())
            : ResolveGroup(item, _root, new[] { address.BlockName });
    }

    /// <summary>A planner defect, never a caller error: the item's PLC was planned without its tree.</summary>
    private void RequireTree(PlcOperationRequest item)
    {
        if (!_hasTree) throw new InvalidOperationException($"Operation '{item.OperationId}' ({item.Operation}) needs the project tree of PLC '{_plc}', which was not read.");
    }

    /// <summary>The canonical path a block or group path names; an undetermined path names the owner's root group.</summary>
    private string PlannedPath(BlockAddress address)
        => string.Join("/", (address.UnitName is null ? new[] { _plc, "Blocks" } : new[] { _plc, "Units", address.UnitName, "Blocks" })
            .Concat(address.FolderPath).Append(address.BlockName));

    private Group Owner(PlcOperationRequest? item, BlockAddress address)
        => address.UnitName is null ? _root
            : _unitRoots.GetValueOrDefault(address.UnitName) ?? throw NotFound(item, $"software unit '{address.UnitName}' was not found in PLC '{_plc}'.", HiddenAt(_basePath));

    private Group ResolveGroup(PlcOperationRequest? item, Group owner, IReadOnlyList<string> folders)
    {
        var current = owner;
        foreach (var folder in folders)
            current = current.Groups.FirstOrDefault(g => !g.IsSystem && Names.Equals(g.Name, folder))
                ?? throw NotFound(item, $"block group '{current.Path}/{folder}' was not found.", HiddenAt(current.Selector));
        return current;
    }

    private static IEnumerable<Group> Descendants(Group group) => group.Groups.SelectMany(g => Descendants(g).Prepend(g));

    private static IEnumerable<(Group Group, Block Block)> DescendantBlocks(Group group)
        => Descendants(group).Prepend(group).SelectMany(g => g.Blocks.Select(b => (g, b)));

    /// <summary>Every OB with a readable number, in all groups (system groups included) and software units.</summary>
    private IEnumerable<(Group Group, Block Block)> ObBlocks()
        => _hasTree ? _unitRoots.Values.Prepend(_root).SelectMany(DescendantBlocks).Where(b => b.Block.Type == ProjectTreeNodeTypes.Ob && b.Block.Number is not null)
            : throw new InvalidOperationException($"The OB inventory of PLC '{_plc}' was not read.");

    private int? PlanObNumber(ObEventClass cls) => ObNumberRules.Pick(cls, ObBlocks().Select(b => b.Block.Number!.Value).ToHashSet());

    private static bool IsOb(PlcOperationRequest item) => string.Equals(item.BlockType, "OB", StringComparison.OrdinalIgnoreCase);

    /// <summary>The catalog already rejected unknown classes; omitted means the default class.</summary>
    private static ObEventClass ObClass(PlcOperationRequest item)
        => ObEventClasses.TryGet(item.ObEventClass ?? ObEventClasses.Default, out var cls) ? cls
            : throw new ArgumentException($"obEventClass '{item.ObEventClass}' is not valid. Valid values: {ObEventClasses.NamesForMessage()}.");

    private static IEnumerable<(Group Group, Block Block)> UserBlocks(Group group)
        => group.Blocks.Select(b => (group, b)).Concat(group.Groups.Where(g => !g.IsSystem).SelectMany(UserBlocks));

    private Group OwnerRoot(IEnumerable<ProjectTreeNode> children, string path, IReadOnlyList<ProjectTreeSelectorSegment> selector)
    {
        var node = children.FirstOrDefault(c => c.NodeType == ProjectTreeNodeTypes.BlockFolder);
        var root = new Group(node?.Name ?? "Program blocks", path, null,
            selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, node?.Name ?? "Program blocks")).ToArray(), false);
        if (node is not null) Fill(root, node);
        return root;
    }

    private static void Fill(Group group, ProjectTreeNode node)
    {
        foreach (var child in node.Children ?? new())
        {
            if (child.NodeType is ProjectTreeNodeTypes.BlockFolder or ProjectTreeNodeTypes.SystemBlockFolder)
            {
                var sub = new Group(child.Name, $"{group.Path}/{child.Name}", group, group.Selector.Append(Segment(child.NodeType, child.Name)).ToArray(),
                    group.IsSystem || child.NodeType == ProjectTreeNodeTypes.SystemBlockFolder);
                Fill(sub, child);
                group.Groups.Add(sub);
            }
            else if (ProjectTreeNodeTypes.BlockLeaves.Contains(child.NodeType))
            {
                group.Blocks.Add(new Block(child.Name, child.NodeType, child.Details?.GetValueOrDefault("ProgrammingLanguage"),
                    group.IsSystem || child.Details?.GetValueOrDefault("IsSystemBlock") == "true",
                    int.TryParse(child.Details?.GetValueOrDefault("Number"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null));
            }
        }
    }

    private PlcWriteEffect Effect(PlcOperationRequest item, PlcTargetIdentity target, IReadOnlyList<PlcFieldChange> changes, string? placement, PlcRemoval? removes = null)
        => new(item.Operation, target, changes, placement, removes, null, null);

    private PlcTargetIdentity TableTarget(Table table) => new(PlcNameRules.TagTable, _plc, _device, table.Folder, table.Name, null, null, null);

    private PlcTargetIdentity MemberTarget(string kind, Table table, string name) => new(kind, _plc, _device, table.Folder, table.Name, name, null, null);

    private static PlcFieldChange[] Change(string field, string? current, string? requested)
        => requested is null ? Array.Empty<PlcFieldChange>() : new[] { new PlcFieldChange(field, current, requested) };

    private static string? Flag(bool? value) => value switch { null => null, true => "true", false => "false" };

    private FiredGuard SkippedGuard(PlcOperationRequest item)
        => Fire(StateUnverifiable, item, $"Part of the project tree of PLC '{_plc}' could not be read, so the target's surroundings cannot be verified.");

    private static FiredGuard Fire(string id, PlcOperationRequest item, string message) => new(id, item.OperationId, $"Operation '{item.OperationId}': {message}");

    private static ResolutionException NotFound(PlcOperationRequest? item, string message, bool hidden = false)
        => new(WorkerFailureCategories.TargetNotFound, item is null ? char.ToUpperInvariant(message[0]) + message[1..] : $"Operation '{item.OperationId}': {message}", hidden);

    /// <summary>A skipped node at or above <paramref name="container"/> may be, or hide, the missing object.</summary>
    private bool HiddenAt(IReadOnlyList<ProjectTreeSelectorSegment> container) => _skipped.Any(s => StartsWith(container, s.ParentPath));

    private void Touch(string key, PlcOperationRequest item) => _touches[key] = (_sequence, item.OperationId);

    private static bool IsBlockTreeNode(ProjectTreeSkippedNodeInfo node) => node.NodeType is not (ProjectTreeNodeTypes.TagTable
        or ProjectTreeNodeTypes.TagTableFolder or ProjectTreeNodeTypes.Type or ProjectTreeNodeTypes.TypeFolder);

    private static bool StartsWith(IReadOnlyList<ProjectTreeSelectorSegment> path, IReadOnlyList<ProjectTreeSelectorSegment> prefix)
        => path.Count >= prefix.Count && SameSelector(path.Take(prefix.Count).ToArray(), prefix);

    private static bool SameSelector(IReadOnlyList<ProjectTreeSelectorSegment> left, IReadOnlyList<ProjectTreeSelectorSegment> right)
        => left.Count == right.Count && left.Zip(right).All(p => p.First.NodeType == p.Second.NodeType && Names.Equals(p.First.Name, p.Second.Name));

    private static ProjectTreeSelectorSegment Segment(string type, string name) => new() { NodeType = type, Name = name };

    private static string TablePath(Table table) => table.Folder == "/" ? "/" + table.Name : $"{table.Folder}/{table.Name}";

    private static string CpuKey(string name) => "cpu:" + name.ToUpperInvariant();

    private static string TypeKey(string name) => "type:" + name.ToUpperInvariant();

    private static string? NumberText(int? number) => number?.ToString(CultureInfo.InvariantCulture);

    /// <summary>The canonical path of a deterministic target group of this PLC, e.g. PLC_1/Blocks/Line.</summary>
    /// <exception cref="ArgumentException">The path is not a deterministic group path of this PLC.</exception>
    private string TargetGroupPath(string groupPath)
    {
        var address = BlockAddress.Parse(groupPath.TrimEnd('/') + "/_");
        if (!address.IsDeterministic)
            throw new ArgumentException($"targetGroupPath '{groupPath}' must be a deterministic group path such as '{_plc}/Blocks' or '{_plc}/Blocks/Folder'.");
        if (!Names.Equals(address.PlcName, _plc) && !Names.Equals(address.PlcName, _device))
            throw new ArgumentException($"targetGroupPath '{groupPath}' names another PLC than '{_plc}'; copy and move stay within one PLC.");
        var planned = PlannedPath(address);
        return planned.Substring(0, planned.Length - 2);
    }

    private Group? FindGroupByPath(string path)
        => _unitRoots.Values.Prepend(_root).SelectMany(root => Descendants(root).Prepend(root))
            .FirstOrDefault(group => Names.Equals(group.Path, path));

    private static string TableKey(string name) => "table:" + name.ToUpperInvariant();

    private static string GroupKey(string path) => "group:" + path.ToUpperInvariant();

    private static string ParentPath(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;

    /// <summary>Keys of the group at <paramref name="path"/> and its ancestors below the owner's root group.</summary>
    private static IEnumerable<string> GroupKeys(string path)
    {
        var segments = path.Split('/');
        var rootLength = segments.Length > 1 && Names.Equals(segments[1], "Units") ? 4 : 2;
        for (var length = rootLength + 1; length <= segments.Length; length++)
            yield return GroupKey(string.Join("/", segments.Take(length)));
    }

    private static string NormalizeFolder(string? folderPath)
    {
        var segments = (folderPath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? "/" : "/" + string.Join("/", segments);
    }

    private static string NormalizeAddress(string? address) => string.Concat((address ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    private static string BlockNodeType(string blockType) => blockType.ToUpperInvariant() switch
    {
        "OB" => ProjectTreeNodeTypes.Ob, "FB" => ProjectTreeNodeTypes.Fb, "FC" => ProjectTreeNodeTypes.Fc,
        "GLOBALDB" => ProjectTreeNodeTypes.GlobalDb, _ => ProjectTreeNodeTypes.Block,
    };

    private static TagInfo Copy(TagInfo tag) => new()
    {
        Name = tag.Name, DataType = tag.DataType, LogicalAddress = tag.LogicalAddress,
        ExternalAccessible = tag.ExternalAccessible, ExternalVisible = tag.ExternalVisible, ExternalWritable = tag.ExternalWritable,
    };
}
