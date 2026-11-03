using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.PlcAnalysis;

/// <summary>One block of the inventory. <see cref="FolderPath"/> excludes the root block folder.</summary>
internal sealed record PlcInventoryBlock(
    string Name,
    string Kind,
    int? Number,
    string? Language,
    string? SoftwareUnit,
    bool IsSystemBlock,
    IReadOnlyList<string> FolderPath,
    string? BlockPath);

/// <summary>One PLC data type with its cross-reference selector.</summary>
internal sealed record PlcInventoryType(
    string Name,
    string? SoftwareUnit,
    IReadOnlyList<string> FolderPath,
    string TypePath,
    IReadOnlyList<ProjectTreeSelectorSegment> Selector);

/// <summary>One tag table; <see cref="FolderPath"/> is written like list_tag_tables ("/" or "/Line/Cell").</summary>
internal sealed record PlcInventoryTagTable(
    string Name,
    string? SoftwareUnit,
    string FolderPath,
    IReadOnlyList<ProjectTreeSelectorSegment> Selector);

/// <summary>A root folder swept by one cross-reference query; <see cref="SoftwareUnit"/> is null for the PLC itself.</summary>
internal sealed record PlcInventoryRoot(string? SoftwareUnit, IReadOnlyList<ProjectTreeSelectorSegment> Selector);

/// <summary>
/// The block, type and tag-table inventory of one PLC software, taken from one project-tree snapshot
/// (the same walk <c>browse_project_tree</c> shows). Siemens-free.
/// </summary>
internal sealed class PlcProgramInventory
{
    private PlcProgramInventory(string plcName, string deviceName)
    {
        PlcName = plcName;
        DeviceName = deviceName;
    }

    public string PlcName { get; }

    public string DeviceName { get; }

    public List<PlcInventoryBlock> Blocks { get; } = new();

    public List<PlcInventoryType> Types { get; } = new();

    public List<PlcInventoryTagTable> TagTables { get; } = new();

    public List<PlcInventoryRoot> BlockRoots { get; } = new();

    public List<PlcInventoryRoot> TypeRoots { get; } = new();

    /// <summary>Nodes the tree walk skipped under this PLC (unreadable blocks, groups, tables, types).</summary>
    public int SkippedNodeCount { get; private set; }

    /// <summary>Selects one PLC software of the tree: by software or device name (case-insensitive), or the only one.</summary>
    public static (PlcProgramInventory? Inventory, StructuredOperationFailure? Failure) Select(
        IReadOnlyList<ProjectTreeNode> roots,
        IReadOnlyList<ProjectTreeSkippedNodeInfo> skipped,
        string? plcName)
    {
        var candidates = new List<(ProjectTreeNode Device, ProjectTreeNode Software)>();
        foreach (var device in roots.Where(node => node.NodeType == ProjectTreeNodeTypes.Device))
            foreach (var software in (device.Children ?? new()).Where(node => node.NodeType == ProjectTreeNodeTypes.PlcSoftware))
                if (plcName is null || PlcNameRules.Comparer.Equals(software.Name, plcName) || PlcNameRules.Comparer.Equals(device.Name, plcName))
                    candidates.Add((device, software));

        if (candidates.Count != 1)
            return (null, new StructuredOperationFailure(
                candidates.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                candidates.Count == 0
                    ? plcName is null ? "The project has no PLC software." : $"No PLC software named '{plcName}' was found in the project."
                    : $"{candidates.Count} PLC software instances match ({string.Join(", ", candidates.Select(c => $"'{c.Software.Name}'"))}). Name the PLC with plcName or a blockPath/typePath that starts with it."));

        var (deviceNode, softwareNode) = candidates[0];
        var inventory = new PlcProgramInventory(softwareNode.Name, deviceNode.Name);
        var plcSelector = new[] { Segment(ProjectTreeNodeTypes.Device, deviceNode.Name), Segment(ProjectTreeNodeTypes.PlcSoftware, softwareNode.Name) };
        inventory.WalkScope(softwareNode, plcSelector, unit: null);
        foreach (var unit in (softwareNode.Children ?? new()).Where(node => node.NodeType == ProjectTreeNodeTypes.SoftwareUnit))
            inventory.WalkScope(unit, plcSelector.Append(Segment(ProjectTreeNodeTypes.SoftwareUnit, unit.Name)).ToArray(), unit.Name);

        inventory.SkippedNodeCount = skipped.Count(node => node.ParentPath.Count >= 2
            && PlcNameRules.Comparer.Equals(node.ParentPath[0].Name, deviceNode.Name)
            && PlcNameRules.Comparer.Equals(node.ParentPath[1].Name, softwareNode.Name));
        return (inventory, null);
    }

    private void WalkScope(ProjectTreeNode scope, IReadOnlyList<ProjectTreeSelectorSegment> scopeSelector, string? unit)
    {
        foreach (var child in scope.Children ?? new())
        {
            var selector = scopeSelector.Append(Segment(child.NodeType, child.Name)).ToArray();
            switch (child.NodeType)
            {
                case ProjectTreeNodeTypes.BlockFolder:
                    BlockRoots.Add(new PlcInventoryRoot(unit, selector));
                    WalkBlocks(child, unit, Array.Empty<string>(), system: false);
                    break;
                case ProjectTreeNodeTypes.TypeFolder:
                    TypeRoots.Add(new PlcInventoryRoot(unit, selector));
                    WalkTypes(child, selector, unit, Array.Empty<string>());
                    break;
                case ProjectTreeNodeTypes.TagTableFolder:
                    WalkTables(child, selector, unit, Array.Empty<string>());
                    break;
            }
        }
    }

    private void WalkBlocks(ProjectTreeNode folder, string? unit, IReadOnlyList<string> folders, bool system)
    {
        foreach (var child in folder.Children ?? new())
        {
            if (ProjectTreeNodeTypes.BlockLeaves.Contains(child.NodeType))
            {
                var isSystem = system || child.Details?.GetValueOrDefault("IsSystemBlock") == "true";
                var number = int.TryParse(child.Details?.GetValueOrDefault("Number"), out var parsed) ? parsed : (int?)null;
                Blocks.Add(new PlcInventoryBlock(child.Name, child.NodeType, number, child.Details?.GetValueOrDefault("ProgrammingLanguage"),
                    unit, isSystem, folders, isSystem ? null : PathOf(unit, "Blocks", folders, child.Name)));
            }
            else if (child.NodeType is ProjectTreeNodeTypes.BlockFolder or ProjectTreeNodeTypes.SystemBlockFolder)
            {
                WalkBlocks(child, unit, folders.Append(child.Name).ToArray(), system || child.NodeType == ProjectTreeNodeTypes.SystemBlockFolder);
            }
        }
    }

    private void WalkTypes(ProjectTreeNode folder, IReadOnlyList<ProjectTreeSelectorSegment> selector, string? unit, IReadOnlyList<string> folders)
    {
        foreach (var child in folder.Children ?? new())
        {
            var childSelector = selector.Append(Segment(child.NodeType, child.Name)).ToArray();
            if (child.NodeType == ProjectTreeNodeTypes.Type)
                Types.Add(new PlcInventoryType(child.Name, unit, folders, PathOf(unit, "Types", folders, child.Name) ?? child.Name, childSelector));
            else if (child.NodeType == ProjectTreeNodeTypes.TypeFolder)
                WalkTypes(child, childSelector, unit, folders.Append(child.Name).ToArray());
        }
    }

    private void WalkTables(ProjectTreeNode folder, IReadOnlyList<ProjectTreeSelectorSegment> selector, string? unit, IReadOnlyList<string> folders)
    {
        foreach (var child in folder.Children ?? new())
        {
            var childSelector = selector.Append(Segment(child.NodeType, child.Name)).ToArray();
            if (child.NodeType == ProjectTreeNodeTypes.TagTable)
                TagTables.Add(new PlcInventoryTagTable(child.Name, unit, "/" + string.Join("/", folders), childSelector));
            else if (child.NodeType == ProjectTreeNodeTypes.TagTableFolder)
                WalkTables(child, childSelector, unit, folders.Append(child.Name).ToArray());
        }
    }

    /// <summary>The deterministic plc_read path, or null when a segment cannot be written in one ('/').</summary>
    private string? PathOf(string? unit, string kindSegment, IReadOnlyList<string> folders, string name)
    {
        var segments = new List<string> { PlcName };
        if (unit is not null) segments.AddRange(new[] { "Units", unit });
        segments.Add(kindSegment);
        segments.AddRange(folders);
        segments.Add(name);
        return segments.Any(segment => segment.Contains('/') || segment.Trim() != segment || segment.Length == 0) ? null : string.Join("/", segments);
    }

    /// <summary>Resolves a blockPath exactly as the worker's block resolver would, against this inventory.</summary>
    public (PlcInventoryBlock? Block, StructuredOperationFailure? Failure) ResolveBlock(BlockAddress address)
    {
        var matches = Blocks.Where(block => PlcNameRules.Comparer.Equals(block.Name, address.BlockName)
            && (!address.IsDeterministic || (!block.IsSystemBlock
                && PlcNameRules.Comparer.Equals(block.SoftwareUnit ?? string.Empty, address.UnitName ?? string.Empty)
                && block.FolderPath.SequenceEqual(address.FolderPath, PlcNameRules.Comparer)))).ToList();
        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, new(WorkerFailureCategories.TargetNotFound, $"Block '{address.ToDisplayPath()}' was not found in PLC '{PlcName}'.")),
            _ => (null, new(WorkerFailureCategories.TargetAmbiguous,
                $"Block '{address.BlockName}' is ambiguous in PLC '{PlcName}': {string.Join(", ", matches.Select(m => m.BlockPath ?? m.Name))}. Use the deterministic path.")),
        };
    }

    public (PlcInventoryType? Type, StructuredOperationFailure? Failure) ResolveType(PlcTypeAddress address)
    {
        var matches = Types.Where(type => PlcNameRules.Comparer.Equals(type.Name, address.TypeName)
            && (!address.IsDeterministic || (PlcNameRules.Comparer.Equals(type.SoftwareUnit ?? string.Empty, address.UnitName ?? string.Empty)
                && type.FolderPath.SequenceEqual(address.FolderPath, PlcNameRules.Comparer)))).ToList();
        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, new(WorkerFailureCategories.TargetNotFound, $"PLC data type '{address.ToDisplayPath()}' was not found in PLC '{PlcName}'.")),
            _ => (null, new(WorkerFailureCategories.TargetAmbiguous,
                $"PLC data type '{address.TypeName}' is ambiguous in PLC '{PlcName}': {string.Join(", ", matches.Select(m => m.TypePath))}. Use the deterministic path.")),
        };
    }

    public (PlcInventoryTagTable? Table, StructuredOperationFailure? Failure) ResolveTagTable(string tableName, string? folderPath)
    {
        var folder = folderPath is null ? null : "/" + string.Join("/", folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var matches = TagTables.Where(table => PlcNameRules.Comparer.Equals(table.Name, tableName)
            && (folder is null || PlcNameRules.Comparer.Equals(table.FolderPath, folder))).ToList();
        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, new(WorkerFailureCategories.TargetNotFound, $"Tag table '{tableName}'{(folder is null ? string.Empty : $" in folder '{folder}'")} was not found in PLC '{PlcName}'.")),
            _ => (null, new(WorkerFailureCategories.TargetAmbiguous, $"Tag table '{tableName}' is ambiguous in PLC '{PlcName}'; narrow it with folderPath.")),
        };
    }

    private static ProjectTreeSelectorSegment Segment(string nodeType, string name) => new() { NodeType = nodeType, Name = name };
}
