using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>Wildcard matching for find_objects: <c>*</c> any run of characters, <c>?</c> one character, whole text, case-insensitive.</summary>
public static class PlcGlob
{
    public static bool IsMatch(string pattern, string text)
    {
        var p = pattern.ToUpperInvariant();
        var t = text.ToUpperInvariant();
        int pi = 0, ti = 0, star = -1, resume = 0;
        while (ti < t.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || p[pi] == t[ti]))
            {
                pi++;
                ti++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                star = pi++;
                resume = ti;
            }
            else if (star >= 0)
            {
                pi = star + 1;
                ti = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }
}

/// <summary>One object found by find_objects; members that do not apply to the kind are null.</summary>
public sealed record PlcFoundObject(
    string Kind,
    string Name,
    string PlcName,
    string DeviceName,
    string? UnitName,
    string? Path,
    string? FolderPath,
    string? ObjectType,
    bool IsSystem,
    string? TableName = null);

/// <summary>The find_objects result: one page of the sorted matches.</summary>
public sealed record PlcObjectFindResult(
    string Pattern,
    IReadOnlyList<string> Kinds,
    int Offset,
    int Limit,
    int TotalMatches,
    int? NextOffset,
    bool Complete,
    IReadOnlyList<string> Messages,
    IReadOnlyList<PlcFoundObject> Matches);

/// <summary>
/// The block, type and tag-table inventory of one PLC software (and its software units), with the groups
/// themselves, taken from one project-tree snapshot (the walk browse_project_tree shows). Siemens-free.
/// Folder lists exclude the scope's root (system) group.
/// </summary>
internal sealed class PlcScopeInventory
{
    internal sealed record Item(string Kind, string Name, string? Unit, IReadOnlyList<string> Folders, string? NodeType, string? Language, bool IsSystem);

    private PlcScopeInventory(string plcName, string deviceName)
    {
        PlcName = plcName;
        DeviceName = deviceName;
    }

    public string PlcName { get; }

    public string DeviceName { get; }

    public List<Item> Items { get; } = new();

    /// <summary>The PLC's software units, also the empty ones (an empty unit has no items).</summary>
    public HashSet<string> Units { get; } = new(PlcNameRules.Comparer);

    public int SkippedNodeCount { get; private set; }

    /// <summary>Every PLC software of the tree that <paramref name="plcName"/> selects (all when null), by software or device name.</summary>
    public static IReadOnlyList<PlcScopeInventory> Select(ProjectTreeObservation tree, string? plcName)
    {
        var result = new List<PlcScopeInventory>();
        foreach (var device in tree.Roots.Where(node => node.NodeType == ProjectTreeNodeTypes.Device))
            foreach (var software in (device.Children ?? new()).Where(node => node.NodeType == ProjectTreeNodeTypes.PlcSoftware))
            {
                if (plcName is not null && !PlcNameRules.Comparer.Equals(software.Name, plcName) && !PlcNameRules.Comparer.Equals(device.Name, plcName)) continue;
                result.Add(FromSoftware(software, device.Name, tree.Skipped.Count(node => node.ParentPath.Count >= 2
                    && PlcNameRules.Comparer.Equals(node.ParentPath[0].Name, device.Name)
                    && PlcNameRules.Comparer.Equals(node.ParentPath[1].Name, software.Name))));
            }

        return result;
    }

    /// <summary>The inventory of one PlcSoftware node (a tree read with a device/PLC start selector has it as its root).</summary>
    public static PlcScopeInventory FromSoftware(ProjectTreeNode software, string deviceName, int skippedNodeCount)
    {
        var inventory = new PlcScopeInventory(software.Name, deviceName) { SkippedNodeCount = skippedNodeCount };
        inventory.WalkScope(software, unit: null);
        foreach (var unit in (software.Children ?? new()).Where(node => node.NodeType == ProjectTreeNodeTypes.SoftwareUnit))
        {
            inventory.Units.Add(unit.Name);
            inventory.WalkScope(unit, unit.Name);
        }
        return inventory;
    }

    private void WalkScope(ProjectTreeNode scope, string? unit)
    {
        foreach (var child in scope.Children ?? new())
        {
            switch (child.NodeType)
            {
                case ProjectTreeNodeTypes.BlockFolder:
                    WalkBlocks(child, unit, Array.Empty<string>(), system: false);
                    break;
                case ProjectTreeNodeTypes.TypeFolder:
                    Walk(child, unit, Array.Empty<string>(), ProjectTreeNodeTypes.TypeFolder, ProjectTreeNodeTypes.Type, "type", "type_group");
                    break;
                case ProjectTreeNodeTypes.TagTableFolder:
                    Walk(child, unit, Array.Empty<string>(), ProjectTreeNodeTypes.TagTableFolder, ProjectTreeNodeTypes.TagTable, "tag_table", "tag_table_group");
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
                Items.Add(new Item("block", child.Name, unit, folders, child.NodeType, child.Details?.GetValueOrDefault("ProgrammingLanguage"), isSystem));
            }
            else if (child.NodeType is ProjectTreeNodeTypes.BlockFolder or ProjectTreeNodeTypes.SystemBlockFolder)
            {
                var childSystem = system || child.NodeType == ProjectTreeNodeTypes.SystemBlockFolder;
                var path = folders.Append(child.Name).ToArray();
                Items.Add(new Item("block_group", child.Name, unit, path, child.NodeType, null, childSystem));
                WalkBlocks(child, unit, path, childSystem);
            }
        }
    }

    private void Walk(ProjectTreeNode folder, string? unit, IReadOnlyList<string> folders, string folderType, string leafType, string leafKind, string groupKind)
    {
        foreach (var child in folder.Children ?? new())
        {
            if (child.NodeType == leafType)
            {
                Items.Add(new Item(leafKind, child.Name, unit, folders, child.NodeType, null, false));
            }
            else if (child.NodeType == folderType)
            {
                var path = folders.Append(child.Name).ToArray();
                Items.Add(new Item(groupKind, child.Name, unit, path, child.NodeType, null, false));
                Walk(child, unit, path, folderType, leafType, leafKind, groupKind);
            }
        }
    }

    /// <summary>The deterministic plc_read path (PLC[/Units/U]/Blocks|Types/folders[/name]), or null when a segment cannot be written in one.</summary>
    public string? PathOf(string? unit, string kindSegment, IReadOnlyList<string> folders, string? name)
    {
        var segments = new List<string> { PlcName };
        if (unit is not null) segments.AddRange(new[] { "Units", unit });
        segments.Add(kindSegment);
        segments.AddRange(folders);
        if (name is not null) segments.Add(name);
        return segments.Any(segment => segment.Contains('/') || segment.Trim() != segment || segment.Length == 0) ? null : string.Join("/", segments);
    }

    public static string FolderText(IReadOnlyList<string> folders) => "/" + string.Join("/", folders);
}

/// <summary>
/// find_objects: a host computation over existing reads, each read at most once per item — the project tree
/// snapshot (blocks, block groups, PLC data types and their groups, tag tables and their groups, for the PLC and
/// its software units), <c>list_watch_and_force_tables</c> (watch tables, force tables, their groups) and
/// <c>list_external_sources</c> — only for the kinds asked for. Matches are sorted deterministically (PLC, kind,
/// unit, path, name) and paged by offset/limit; a read failure fails the item, never an empty match list.
/// Software units come from the tree; tags and user constants from <c>list_tag_tables</c> (the PLC's own tag
/// tables: the tag inventory reads no software-unit tag table). Technology objects are not searched here.
/// </summary>
public static class PlcObjectFinder
{
    private static readonly string[] TreeKinds = { "software_unit", "block", "block_group", "type", "type_group", "tag_table", "tag_table_group" };
    private static readonly string[] TagKinds = { "tag", "user_constant" };
    private static readonly string[] WatchKinds = { "watch_table", "force_table", "watch_table_group" };

    public static bool Handles(string? operation) => operation == PlcSourceFolderMethods.FindObjects;

    public static async Task<StructuredOperationItem> ExecuteAsync(OpennessWorkerClient client, PlcOperationRequest operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kinds = operation.Kinds?.ToArray() ?? PlcOperationCatalog.FindKinds.ToArray();
        var pattern = operation.Pattern!;
        var messages = new List<string>();
        var warnings = new List<string>();
        var complete = true;
        var found = new List<PlcFoundObject>();
        var plcSeen = false;

        if (kinds.Any(kind => TreeKinds.Contains(kind)))
        {
            var call = await client.BrowseProjectTreeV3SnapshotAsync().ConfigureAwait(false);
            warnings.AddRange(call.WorkerResult.Warnings);
            if (!call.WorkerResult.Success)
                return StructuredOperationItem.Failed(operation, call.WorkerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    $"The project tree could not be read: {call.WorkerResult.Error}", warnings);
            ProjectTreeObservation tree;
            try
            {
                tree = ProjectTreeWorkerPayloadContract.Decode(call.WorkerResult, null, null);
            }
            catch (ProjectTreeProtocolException exception)
            {
                return StructuredOperationItem.Failed(operation, exception.Category, exception.Message, warnings);
            }

            foreach (var inventory in PlcScopeInventory.Select(tree, operation.PlcName))
            {
                plcSeen = true;
                if (inventory.SkippedNodeCount > 0)
                {
                    complete = false;
                    messages.Add($"{inventory.SkippedNodeCount} project-tree nodes of PLC '{inventory.PlcName}' could not be read; matches among them are missing.");
                }

                foreach (var item in inventory.Items.Where(item => kinds.Contains(item.Kind) && PlcGlob.IsMatch(pattern, item.Name)))
                    found.Add(Describe(inventory, item));
                if (kinds.Contains("software_unit"))
                    found.AddRange(inventory.Units.Where(unit => PlcGlob.IsMatch(pattern, unit))
                        .Select(unit => new PlcFoundObject("software_unit", unit, inventory.PlcName, inventory.DeviceName, unit, $"{inventory.PlcName}/Units/{unit}", null, null, false)));
            }
        }

        if (kinds.Any(kind => TagKinds.Contains(kind)))
        {
            var call = await client.ListTagTablesAsync(operation.PlcName).ConfigureAwait(false);
            warnings.AddRange(call.Warnings);
            var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = operation.OperationId, Operation = "list_tag_tables" }, call);
            if (projected.Result is not { } element)
                return StructuredOperationItem.Failed(operation, projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed,
                    $"The tag tables could not be read: {projected.Failure?.Message}", warnings);
            var tags = CanonicalJson.Deserialize<PlcTagInventoryInfo>(element.GetRawText());
            if (!tags.IsComplete)
            {
                complete = false;
                messages.AddRange(tags.Messages);
            }

            foreach (var plc in tags.Plcs)
            {
                plcSeen = true;
                foreach (var table in plc.Tables)
                {
                    if (kinds.Contains("tag"))
                        found.AddRange(table.Tags.Where(t => PlcGlob.IsMatch(pattern, t.Name)).Select(t =>
                            new PlcFoundObject("tag", t.Name, plc.PlcName, plc.DeviceName ?? string.Empty, null, null, table.FolderPath, t.DataType, false, table.Name)));
                    if (kinds.Contains("user_constant"))
                        found.AddRange(table.UserConstants.Where(c => PlcGlob.IsMatch(pattern, c.Name)).Select(c =>
                            new PlcFoundObject("user_constant", c.Name, plc.PlcName, plc.DeviceName ?? string.Empty, null, null, table.FolderPath, c.DataType, false, table.Name)));
                }
            }
        }

        if (kinds.Any(kind => WatchKinds.Contains(kind)))
        {
            var call = await client.ListWatchAndForceTablesAsync(operation.PlcName).ConfigureAwait(false);
            warnings.AddRange(call.Warnings);
            if (!call.Success)
                return StructuredOperationItem.Failed(operation, call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    $"The watch and force tables could not be read: {call.Error}", warnings);
            PlcWatchTableInventoryInfo watch;
            try
            {
                watch = PlcOnlinePayloadContract.DecodeWatchTables(call.Payload);
            }
            catch (JsonException)
            {
                return StructuredOperationItem.Failed(operation, WorkerFailureCategories.ProtocolError, PlcOnlinePayloadContract.ProtocolFailureMessage, warnings);
            }

            foreach (var plc in watch.Plcs)
            {
                plcSeen = true;
                if (!plc.Complete)
                {
                    complete = false;
                    messages.Add($"The watch-and-force table inventory of PLC '{plc.PlcName}' is incomplete (at most {PlcWatchTablePolicy.MaxTables} entries are listed).");
                }

                if (kinds.Contains("watch_table"))
                    found.AddRange(plc.WatchTables.Where(t => PlcGlob.IsMatch(pattern, t.Name))
                        .Select(t => new PlcFoundObject("watch_table", t.Name, plc.PlcName, plc.DeviceName, null, null, t.GroupPath, null, false)));
                if (kinds.Contains("force_table"))
                    found.AddRange(plc.ForceTables.Where(t => PlcGlob.IsMatch(pattern, t.Name))
                        .Select(t => new PlcFoundObject("force_table", t.Name, plc.PlcName, plc.DeviceName, null, null, t.GroupPath, null, false)));
                if (kinds.Contains("watch_table_group"))
                    found.AddRange(plc.Groups.Select(g => (Path: g, Name: g[(g.LastIndexOf('/') + 1)..])).Where(g => PlcGlob.IsMatch(pattern, g.Name))
                        .Select(g => new PlcFoundObject("watch_table_group", g.Name, plc.PlcName, plc.DeviceName, null, null, g.Path, null, false)));
            }
        }

        if (kinds.Contains("external_source"))
        {
            var request = new PlcOperationRequest { OperationId = operation.OperationId, Operation = PlcSourceFolderMethods.ListExternalSources, PlcName = operation.PlcName };
            var call = await PlcSourceFolderOperations.InvokeReadAsync(client, request).ConfigureAwait(false);
            warnings.AddRange(call.Warnings);
            if (!call.Success)
                return StructuredOperationItem.Failed(operation, call.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    $"The external sources could not be read: {call.Error}", warnings);
            PlcExternalSourceInventoryInfo sources;
            try
            {
                sources = CanonicalJson.Deserialize<PlcExternalSourceInventoryInfo>(PlcSourceFolderOperations.DecodeRead(request, call.Payload).GetRawText());
            }
            catch (JsonException)
            {
                return StructuredOperationItem.Failed(operation, WorkerFailureCategories.ProtocolError,
                    "The list_external_sources payload did not match its declared result contract and was rejected.", warnings);
            }

            if (!sources.Complete)
            {
                complete = false;
                messages.AddRange(sources.Messages);
            }

            foreach (var plc in sources.Plcs)
            {
                plcSeen = true;
                found.AddRange(plc.Sources.Where(s => PlcGlob.IsMatch(pattern, s.Name))
                    .Select(s => new PlcFoundObject("external_source", s.Name, plc.PlcName, plc.DeviceName, s.UnitName, null, null, null, false)));
            }
        }

        if (operation.PlcName is not null && !plcSeen)
            return StructuredOperationItem.Failed(operation, WorkerFailureCategories.TargetNotFound,
                $"PLC '{operation.PlcName}' matches no PLC software in the project.", warnings);

        var kindOrder = PlcOperationCatalog.FindKinds.ToList();
        var sorted = found
            .OrderBy(f => f.PlcName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => kindOrder.IndexOf(f.Kind))
            .ThenBy(f => f.UnitName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path ?? f.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.TableName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
        var offset = operation.Offset ?? 0;
        var limit = operation.Limit ?? PlcOperationCatalog.DefaultFindLimit;
        var page = sorted.Skip(offset).Take(limit).ToList();
        var next = offset + page.Count < sorted.Count ? offset + page.Count : (int?)null;
        var result = new PlcObjectFindResult(pattern, kinds, offset, limit, sorted.Count, next, complete, messages, page);
        return new StructuredOperationItem(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded,
            CanonicalJson.ToElement(result), Failure: null, Omission: null, SkipReason: null, warnings);
    }

    private static PlcFoundObject Describe(PlcScopeInventory inventory, PlcScopeInventory.Item item)
        => item.Kind switch
        {
            "block" => new("block", item.Name, inventory.PlcName, inventory.DeviceName, item.Unit,
                item.IsSystem ? null : inventory.PathOf(item.Unit, "Blocks", item.Folders, item.Name), PlcScopeInventory.FolderText(item.Folders), item.NodeType, item.IsSystem),
            "block_group" => new("block_group", item.Name, inventory.PlcName, inventory.DeviceName, item.Unit,
                item.IsSystem ? null : inventory.PathOf(item.Unit, "Blocks", item.Folders, null), PlcScopeInventory.FolderText(item.Folders), item.NodeType, item.IsSystem),
            "type" => new("type", item.Name, inventory.PlcName, inventory.DeviceName, item.Unit,
                inventory.PathOf(item.Unit, "Types", item.Folders, item.Name), PlcScopeInventory.FolderText(item.Folders), null, false),
            "type_group" => new("type_group", item.Name, inventory.PlcName, inventory.DeviceName, item.Unit,
                inventory.PathOf(item.Unit, "Types", item.Folders, null), PlcScopeInventory.FolderText(item.Folders), null, false),
            // Tag tables are addressed by tableName + folderPath (+ unitName); tag table groups by folderPath.
            _ => new(item.Kind, item.Name, inventory.PlcName, inventory.DeviceName, item.Unit, null, PlcScopeInventory.FolderText(item.Folders), null, false),
        };
}
