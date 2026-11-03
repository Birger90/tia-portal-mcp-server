using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// <c>source-folders</c> fixture for the external-source, group-rename, file-export and accessible-device
/// operations. One PLC_1 on Station_1 with a block tree (Main, FB_Line, Settings_DB, Line/FC_Ladder,
/// Line/Cell/FB_Cell, a system block folder), PLC data types (UDT_A, Motion/UDT_B), tag tables (Default tag table,
/// Line/Motors), a software unit Unit_A, watch and force tables, two external sources and two accessible devices.
/// Writes change the in-process state, so later reads of the same process observe them. It models the payload
/// shapes only; it is no evidence of Siemens behavior.
/// </summary>
sealed class SourceFolderScenario
{
    private bool _unreadableSources;

    private const string Device = "Station_1";
    private const string Plc = "PLC_1";
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private sealed class Folder(string name)
    {
        public string Name { get; set; } = name;
        public List<Folder> Folders { get; } = new();
        public List<(string Name, string Type, string Language, int Number)> Leaves { get; } = new();
    }

    private sealed class Unit(string name)
    {
        public string Name { get; set; } = name;
        public Folder Blocks { get; } = new("Program blocks");
        public Folder Tags { get; } = new("PLC tags");
        public Folder Types { get; } = new("PLC data types");
    }

    private readonly Folder _blocks = BlockTree();
    private readonly List<Unit> _units = new() { UnitA() };
    private readonly Folder _types = TypeTree();
    private readonly Folder _tags = TagTree();
    private readonly List<string> _watchGroups = new() { "/Ops" };
    private readonly List<(string Group, string Name, bool Force)> _watchTables = new() { ("/", "Watch_1", false), ("/Ops", "Watch_2", false), ("/", "Force_1", true) };
    private readonly List<PlcExternalSourceInfo> _sources = new()
    {
        new() { Name = "Main_src.scl", UnitName = null },
        new() { Name = "UnitSource.scl", UnitName = "Unit_A" },
    };

    private static Unit UnitA()
    {
        var unit = new Unit("Unit_A");
        unit.Blocks.Leaves.Add(("Unit_FC", ProjectTreeNodeTypes.Fc, "SCL", 20));
        unit.Tags.Folders.Add(new Folder("UnitTags"));
        return unit;
    }

    private Unit RequireUnit(string? name)
        => _units.FirstOrDefault(u => Names.Equals(u.Name, name))
            ?? throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"Software unit '{name}' was not found in PLC software '{Plc}'.");

    private static Folder BlockTree()
    {
        var root = new Folder("Program blocks");
        root.Leaves.AddRange(new[] { ("Main", ProjectTreeNodeTypes.Ob, "LAD", 1), ("FB_Line", ProjectTreeNodeTypes.Fb, "SCL", 1), ("Settings_DB", ProjectTreeNodeTypes.GlobalDb, "DB", 10) });
        var line = new Folder("Line");
        line.Leaves.Add(("FC_Ladder", ProjectTreeNodeTypes.Fc, "LAD", 2));
        var cell = new Folder("Cell");
        cell.Leaves.Add(("FB_Cell", ProjectTreeNodeTypes.Fb, "SCL", 3));
        line.Folders.Add(cell);
        root.Folders.Add(line);
        return root;
    }

    private static Folder TypeTree()
    {
        var root = new Folder("PLC data types");
        root.Leaves.Add(("UDT_A", ProjectTreeNodeTypes.Type, string.Empty, 0));
        var motion = new Folder("Motion");
        motion.Leaves.Add(("UDT_B", ProjectTreeNodeTypes.Type, string.Empty, 0));
        root.Folders.Add(motion);
        return root;
    }

    private static Folder TagTree()
    {
        var root = new Folder("PLC tags");
        root.Leaves.Add(("Default tag table", ProjectTreeNodeTypes.TagTable, string.Empty, 0));
        var line = new Folder("Line");
        line.Leaves.Add(("Motors", ProjectTreeNodeTypes.TagTable, string.Empty, 0));
        root.Folders.Add(line);
        return root;
    }

    /// <param name="unreadableSources">list_external_sources fails with worker_operation_failed (scenario source-folders-unreadable-sources).</param>
    public string Handle(string line, bool unreadableSources = false)
    {
        _unreadableSources = unreadableSources;
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            return request.Method switch
            {
                "list_tag_tables" => Ok(WorkerJson.SerializePayload(TagInventory(request)), request),
                "browse_project_tree_v3_snapshot" => Ok(WorkerJson.SerializePayload(Tree(request)), request),
                PlcOnlineMethods.ListWatchAndForceTables => Ok(WorkerJson.SerializePayload(WatchInventory()), request),
                PlcOnlineMethods.ReadWatchTable => Ok(WorkerJson.SerializePayload(WatchTable(request)), request),
                PlcOnlineMethods.ReadForceTable => Ok(WorkerJson.SerializePayload(ForceTable(request)), request),
                "get_block_content" => Ok(BlockContent(request), request),
                "get_type_content" => Ok(TypeContent(request), request),
                PlcSourceFolderMethods.ListExternalSources => Ok(WorkerJson.SerializePayload(Sources(request)), request),
                PlcSourceFolderMethods.ImportExternalSource => Ok(WorkerJson.SerializePayload(Import(request)), request),
                PlcSourceFolderMethods.GenerateBlocksFromSource => Ok(WorkerJson.SerializePayload(Generate(request)), request),
                PlcSourceFolderMethods.DeleteExternalSource => Ok(WorkerJson.SerializePayload(Delete(request)), request),
                PlcSourceFolderMethods.ExportSourceFile => Ok(WorkerJson.SerializePayload(ExportSource(request)), request),
                PlcSourceFolderMethods.ScanAccessibleDevices => Ok(WorkerJson.SerializePayload(Scan(request)), request),
                var method when PlcSourceFolderMethods.GroupRenames.Contains(method) => Ok(WorkerJson.SerializePayload(Rename(request)), request),
                PlcSourceFolderMethods.ReadTagComments => Ok(WorkerJson.SerializePayload(Comments(request)), request),
                PlcSourceFolderMethods.RenameSoftwareUnit => Ok(WorkerJson.SerializePayload(RenameUnit(request)), request),
                var method when PlcSourceFolderMethods.GroupCreatesDeletes.Contains(method) || PlcSourceFolderMethods.SoftwareUnitOperations.Contains(method)
                    => Ok(WorkerJson.SerializePayload(Container(request)), request),
                _ => Fail(WorkerFailureCategories.ValidationError, $"source-folders does not script '{request.Method}'."),
            };
        }
        catch (ScenarioFailure failure)
        {
            return Fail(failure.Category, failure.Message);
        }
    }

    private sealed class ScenarioFailure(string category, string message) : Exception(message)
    {
        public string Category { get; } = category;
    }

    private static PlcSourceFolderRequestInfo Info(WorkerRequest request)
    {
        if (request.PlcSourceFolder is null) throw new ScenarioFailure(WorkerFailureCategories.ValidationError, $"{request.Method} requires the external-source/folder request.");
        return request.PlcSourceFolder;
    }

    private static void RequirePlc(string? plcName)
    {
        if (plcName is not null && !Names.Equals(plcName, Plc) && !Names.Equals(plcName, Device))
            throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");
    }

    private PlcTagInventoryInfo TagInventory(WorkerRequest request)
    {
        RequirePlc(request.PlcName);
        var tables = new List<TagTableInfo>();
        void Walk(Folder folder, string path)
        {
            foreach (var leaf in folder.Leaves)
                tables.Add(new TagTableInfo
                {
                    Name = leaf.Name, FolderPath = path, IsDefault = leaf.Name == "Default tag table",
                    Tags = leaf.Name == "Default tag table"
                        ? new() { new TagInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0", ExternalAccessible = true, ExternalVisible = true, ExternalWritable = false } }
                        : new() { new TagInfo { Name = "Motor, \"main\"", DataType = "Int", LogicalAddress = "%MW10", ExternalAccessible = null, ExternalVisible = null, ExternalWritable = null } },
                    UserConstants = leaf.Name == "Default tag table" ? new() { new UserConstantInfo { Name = "Offset", DataType = "Int", Value = "-5" } } : new(),
                });
            foreach (var child in folder.Folders) Walk(child, path == "/" ? "/" + child.Name : path + "/" + child.Name);
        }

        Walk(_tags, "/");
        var folder = request.FolderPath is null ? null : "/" + string.Join("/", request.FolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var selected = tables.Where(t => (request.TableName is null || Names.Equals(t.Name, request.TableName)) && (folder is null || Names.Equals(t.FolderPath, folder))).ToList();
        if ((request.TableName ?? request.FolderPath) is not null && selected.Count == 0)
            throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, "No tag table matches the filter.");
        return new PlcTagInventoryInfo { IsComplete = true, Plcs = new() { new PlcTagInventoryPlcInfo { PlcName = Plc, DeviceName = Device, Tables = selected } } };
    }

    private ProjectTreeBrowseResultInfo Tree(WorkerRequest request)
    {
        static ProjectTreeNode Node(Folder folder, string folderType, Func<(string Name, string Type, string Language, int Number), ProjectTreeNode> leaf)
            => new() { Name = folder.Name, NodeType = folderType, Children = folder.Leaves.Select(leaf).Concat(folder.Folders.Select(f => Node(f, folderType, leaf))).ToList() };
        static ProjectTreeNode Block((string Name, string Type, string Language, int Number) b)
            => new() { Name = b.Name, NodeType = b.Type, Children = new(), Details = new() { ["Number"] = b.Number.ToString(), ["ProgrammingLanguage"] = b.Language } };
        static ProjectTreeNode Leaf((string Name, string Type, string Language, int Number) l) => new() { Name = l.Name, NodeType = l.Type, Children = new() };

        var blocks = Node(_blocks, ProjectTreeNodeTypes.BlockFolder, Block);
        blocks.Children!.Add(new ProjectTreeNode
        {
            Name = "System blocks", NodeType = ProjectTreeNodeTypes.SystemBlockFolder,
            Children = new() { new() { Name = "Sys_FB", NodeType = ProjectTreeNodeTypes.Fb, Children = new(), Details = new() { ["Number"] = "100", ["ProgrammingLanguage"] = "STL", ["IsSystemBlock"] = "true" } } },
        });
        var units = _units.Select(unit => new ProjectTreeNode
        {
            Name = unit.Name, NodeType = ProjectTreeNodeTypes.SoftwareUnit,
            Children = new()
            {
                Node(unit.Blocks, ProjectTreeNodeTypes.BlockFolder, Block),
                Node(unit.Tags, ProjectTreeNodeTypes.TagTableFolder, Leaf),
                Node(unit.Types, ProjectTreeNodeTypes.TypeFolder, Leaf),
            },
        });
        var software = new ProjectTreeNode
        {
            Name = Plc, NodeType = ProjectTreeNodeTypes.PlcSoftware,
            Children = new List<ProjectTreeNode> { blocks, Node(_tags, ProjectTreeNodeTypes.TagTableFolder, Leaf), Node(_types, ProjectTreeNodeTypes.TypeFolder, Leaf) }.Concat(units).ToList(),
        };
        if (request.StartSelector is { Count: 2 } selector)
        {
            if (!Names.Equals(selector[0].Name, Device) || !Names.Equals(selector[1].Name, Plc))
                throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, "The typed project-tree selector did not resolve to a target.");
            return new ProjectTreeBrowseResultInfo
            {
                StartSelector = new() { new() { NodeType = ProjectTreeNodeTypes.Device, Name = Device }, new() { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = Plc } },
                Depth = request.Depth, Roots = new() { software }, Skipped = new(),
            };
        }

        return new ProjectTreeBrowseResultInfo
        {
            StartSelector = null, Depth = null, Skipped = new(),
            Roots = new() { new ProjectTreeNode { Name = Device, NodeType = ProjectTreeNodeTypes.Device, Children = new() { software } } },
        };
    }

    private PlcWatchTableInventoryInfo WatchInventory() => new()
    {
        Plcs = new()
        {
            new PlcWatchTablePlcInfo
            {
                PlcName = Plc, DeviceName = Device, Complete = true, Groups = _watchGroups.ToList(),
                WatchTables = _watchTables.Where(t => !t.Force).Select(t => new PlcWatchTableSummaryInfo { GroupPath = t.Group, Name = t.Name, IsConsistent = true }).ToList(),
                ForceTables = _watchTables.Where(t => t.Force).Select(t => new PlcWatchTableSummaryInfo { GroupPath = t.Group, Name = t.Name, IsConsistent = true }).ToList(),
            },
        },
    };

    private (string Group, string Name, bool Force) RequireTable(WorkerRequest request, bool force)
    {
        var group = PlcWatchTablePolicy.NormalizeGroupPath(request.FolderPath);
        var matches = _watchTables.Where(t => t.Force == force && Names.Equals(t.Group, group) && Names.Equals(t.Name, request.TableName)).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"Table '{request.TableName}' was not found in group '{group}'.");
    }

    private PlcWatchTableReadInfo WatchTable(WorkerRequest request)
    {
        var table = RequireTable(request, force: false);
        return new PlcWatchTableReadInfo
        {
            PlcName = Plc, DeviceName = Device, GroupPath = table.Group, Name = table.Name, IsConsistent = true, EntryCount = 2, EntriesComplete = true,
            Entries = new()
            {
                new() { Index = 0, Name = "\"Start\"", Address = "%I0.0", DisplayFormat = "Bool", MonitorTrigger = "Permanent", ModifyIntention = false, ModifyTrigger = "Permanent", ModifyValue = "TRUE" },
                new() { Index = 1, Name = "=cmd", Address = null, DisplayFormat = null, MonitorTrigger = null, ModifyIntention = null, ModifyTrigger = null, ModifyValue = null, UnreadableAttributes = new() { "Address" } },
            },
        };
    }

    private PlcForceTableReadInfo ForceTable(WorkerRequest request)
    {
        var table = RequireTable(request, force: true);
        return new PlcForceTableReadInfo
        {
            PlcName = Plc, DeviceName = Device, GroupPath = table.Group, Name = table.Name, IsConsistent = true, EntryCount = 1, EntriesComplete = true,
            Entries = new() { new() { Index = 0, Name = "\"Valve\"", Address = "%Q0.1", DisplayFormat = "Bool", MonitorTrigger = "Permanent", ForceIntention = true, ForceValue = "TRUE" } },
        };
    }

    private string BlockContent(WorkerRequest request)
    {
        var name = request.BlockPath!.Split('/')[^1];
        if (name == "FC_Ladder" && request.Format == "source") throw new ScenarioFailure(WorkerFailureCategories.ValidationError, "not available as source");
        if (name == "FB_Cell") throw new ScenarioFailure(WorkerFailureCategories.WorkerOperationFailed, "The block is know-how protected.");
        return request.Format == "source"
            ? $"FUNCTION_BLOCK \"{name}\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n"
            : $"--- FILE: {name}.xml ---\n<Document><SW.Blocks.FB><AttributeList><Name>{name}</Name></AttributeList></SW.Blocks.FB></Document>\n--- FILE: {name}.s7res ---\nres";
    }

    private static string TypeContent(WorkerRequest request)
    {
        var name = request.TypePath!.Split('/')[^1];
        return request.Format == "xml"
            ? $"<Document><SW.Types.PlcStruct><AttributeList><Name>{name}</Name></AttributeList></SW.Types.PlcStruct></Document>"
            : $"TYPE \"{name}\"\r\nSTRUCT\r\nEND_STRUCT;\r\nEND_TYPE\r\n";
    }

    private PlcExternalSourceInventoryInfo Sources(WorkerRequest request)
    {
        var info = Info(request);
        RequirePlc(info.PlcName);
        if (_unreadableSources) throw new ScenarioFailure(WorkerFailureCategories.WorkerOperationFailed, "The external source group could not be read.");
        return new PlcExternalSourceInventoryInfo
        {
            Complete = true,
            Plcs = new() { new PlcExternalSourcePlcInfo { PlcName = Plc, DeviceName = Device, Sources = _sources.Select(s => new PlcExternalSourceInfo { Name = s.Name, UnitName = s.UnitName }).ToList() } },
        };
    }

    private PlcExternalSourceMutationInfo Import(WorkerRequest request)
    {
        var info = Info(request);
        RequirePlc(info.PlcName);
        if (!File.Exists(info.FilePath)) throw new ScenarioFailure(WorkerFailureCategories.ValidationError, $"The source file '{info.FilePath}' does not exist. Nothing was imported.");
        var name = info.SourceName ?? PlcFileTargets.DefaultSourceName(info.FilePath!);
        if (_sources.Any(s => s.Name == name && Names.Equals(s.UnitName ?? string.Empty, info.UnitName ?? string.Empty)))
            throw new ScenarioFailure(WorkerFailureCategories.WorkerOperationFailed, $"An external source named '{name}' already exists.");
        _sources.Add(new PlcExternalSourceInfo { Name = name, UnitName = info.UnitName });
        return new PlcExternalSourceMutationInfo
        {
            Operation = request.Method, PlcName = Plc, DeviceName = Device, UnitName = info.UnitName, SourceName = name, FilePath = info.FilePath, ReadBackConfirmed = true,
        };
    }

    private PlcExternalSourceInfo RequireSource(PlcSourceFolderRequestInfo info)
        => _sources.FirstOrDefault(s => s.Name == info.SourceName && Names.Equals(s.UnitName ?? string.Empty, info.UnitName ?? string.Empty))
            ?? throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"External source '{info.SourceName}' was not found.");

    private PlcSourceGenerationInfo Generate(WorkerRequest request)
    {
        var info = Info(request);
        var source = RequireSource(info);
        var stem = Path.GetFileNameWithoutExtension(source.Name);
        var target = info.TargetGroupPath is { } path && PlcGroupPath.TryParse(path, out var group, out _) && !group!.IsRoot ? group : null;
        if (target is not null && target.Kind == PlcGroupPath.BlocksKind)
            FindFolder(_blocks, target.Folders).Leaves.Add((stem, ProjectTreeNodeTypes.Fb, "SCL", 50));
        else if (target is null)
            _blocks.Leaves.Add((stem, ProjectTreeNodeTypes.Fb, "SCL", 50));
        return new PlcSourceGenerationInfo
        {
            PlcName = Plc, DeviceName = Device, UnitName = source.UnitName, SourceName = source.Name, GenerateOption = info.GenerateOption ?? "None",
            TargetGroupPath = target?.ToDisplayPath(), Generated = new() { new PlcGeneratedObjectInfo { Name = stem, Kind = target?.Kind == PlcGroupPath.TypesKind ? "Type" : "FB" } },
        };
    }

    private PlcExternalSourceMutationInfo Delete(WorkerRequest request)
    {
        var info = Info(request);
        var source = RequireSource(info);
        _sources.Remove(source);
        return new PlcExternalSourceMutationInfo
        {
            Operation = request.Method, PlcName = Plc, DeviceName = Device, UnitName = source.UnitName, SourceName = source.Name, FilePath = null, ReadBackConfirmed = true,
        };
    }

    private static PlcSourceFileExportInfo ExportSource(WorkerRequest request)
    {
        var info = Info(request);
        if (PlcFileTargets.WriteTargetError(info.FilePath!, info.Overwrite) is { } refusal) throw new ScenarioFailure(WorkerFailureCategories.ValidationError, refusal);
        var objects = (info.BlockPaths ?? new()).Concat(info.TypePaths ?? new()).ToList();
        var text = string.Join("\r\n", objects.Select(o => $"// generated from {o}")) + "\r\n";
        var written = PlcFileTargets.Write(info.FilePath!, PlcFileTargets.Utf8WithBom(text), info.Overwrite);
        return new PlcSourceFileExportInfo
        {
            FilePath = info.FilePath!, Overwritten = written.Replaced, Bytes = written.Bytes, Sha256 = written.Sha256, WithDependencies = info.WithDependencies,
            PlcName = Plc, UnitName = null, Objects = objects,
        };
    }

    private static PlcAccessibleDeviceScanInfo Scan(WorkerRequest request)
    {
        var info = Info(request);
        RequirePlc(info.PlcName);
        if (info.Mode != "PN/IE" || info.PcInterfaceName != "Intel(R) Ethernet" || info.PcInterfaceNumber != 1)
            throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"The PC interface '{info.PcInterfaceName}' #{info.PcInterfaceNumber} of mode '{info.Mode}' was not found on the PLC's online configuration. Nothing was scanned.");
        var devices = new List<PlcAccessibleDeviceInfo>
        {
            new() { Name = "plc_1", DeviceSeries = "S7-1500", Address = "192.168.0.1", MacAddress = "28-63-36-00-00-01" },
            new() { Name = "et200sp", DeviceSeries = "ET 200SP", Address = "192.168.0.2", MacAddress = "28-63-36-00-00-02" },
        };
        var max = info.MaxDevices ?? 256;
        return new PlcAccessibleDeviceScanInfo
        {
            PlcName = Plc, DeviceName = Device, Mode = info.Mode, PcInterfaceName = info.PcInterfaceName, PcInterfaceNumber = 1,
            DeviceCount = devices.Count, Truncated = devices.Count > max, MaxDevices = max, Devices = devices.Take(max).ToList(),
        };
    }

    private PlcGroupRenameInfo Rename(WorkerRequest request)
    {
        var info = Info(request);
        string kind, path, newPath, oldName;
        switch (request.Method)
        {
            case PlcSourceFolderMethods.RenameBlockGroup:
            case PlcSourceFolderMethods.RenameTypeGroup:
            {
                PlcGroupPath.TryParse(info.GroupPath, out var group, out var error);
                if (group is null || group.IsRoot) throw new ScenarioFailure(WorkerFailureCategories.ValidationError, error ?? "root");
                var folder = FindFolder(group.Kind == PlcGroupPath.BlocksKind
                    ? group.UnitName is null ? _blocks : RequireUnit(group.UnitName).Blocks
                    : group.UnitName is null ? _types : RequireUnit(group.UnitName).Types, group.Folders);
                oldName = folder.Name;
                folder.Name = info.NewName!;
                kind = group.Kind == PlcGroupPath.BlocksKind ? "BlockGroup" : "TypeGroup";
                path = group.ToDisplayPath();
                newPath = group.RenamedDisplayPath(info.NewName!);
                break;
            }
            case PlcSourceFolderMethods.RenameTagTableGroup:
            {
                var segments = PlcWatchTablePolicy.Segments(info.FolderPath);
                var folder = FindFolder(info.UnitName is null ? _tags : RequireUnit(info.UnitName).Tags, segments);
                oldName = folder.Name;
                folder.Name = info.NewName!;
                kind = "TagTableGroup";
                path = "/" + string.Join("/", segments);
                newPath = "/" + string.Join("/", segments.Take(segments.Count - 1).Append(info.NewName!));
                break;
            }
            default:
            {
                path = PlcWatchTablePolicy.NormalizeGroupPath(info.FolderPath);
                if (!_watchGroups.Contains(path, Names)) throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"Watch-and-force table group '{path}' was not found.");
                oldName = path[(path.LastIndexOf('/') + 1)..];
                newPath = path[..path.LastIndexOf('/')] + "/" + info.NewName;
                for (var i = 0; i < _watchGroups.Count; i++)
                    if (Names.Equals(_watchGroups[i], path) || _watchGroups[i].StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) _watchGroups[i] = newPath + _watchGroups[i][path.Length..];
                for (var i = 0; i < _watchTables.Count; i++)
                    if (Names.Equals(_watchTables[i].Group, path)) _watchTables[i] = _watchTables[i] with { Group = newPath };
                kind = "WatchTableGroup";
                break;
            }
        }

        return new PlcGroupRenameInfo
        {
            Operation = request.Method, Kind = kind, PlcName = Plc, DeviceName = Device, UnitName = info.UnitName, Path = path, NewPath = newPath, OldName = oldName, NewName = info.NewName!,
        };
    }

    private PlcTagCommentInventoryInfo Comments(WorkerRequest request)
    {
        var info = Info(request);
        RequirePlc(info.PlcName);
        var inventory = TagInventory(new WorkerRequest { Method = "list_tag_tables", PlcName = info.PlcName, TableName = info.TableName, FolderPath = info.FolderPath });
        static List<PlcCommentTextInfo> Texts(params (string Culture, string Text)[] texts) => texts.Select(t => new PlcCommentTextInfo { Culture = t.Culture, Text = t.Text }).ToList();
        return new PlcTagCommentInventoryInfo
        {
            Complete = true,
            Plcs = inventory.Plcs.Select(plc => new PlcTagCommentPlcInfo
            {
                PlcName = plc.PlcName, DeviceName = plc.DeviceName ?? Device,
                Tables = plc.Tables.Select(table => new PlcTagTableCommentsInfo
                {
                    FolderPath = table.FolderPath, TableName = table.Name,
                    Members = table.Tags.Select(tag => new PlcMemberCommentInfo
                        {
                            Kind = "Tag", Name = tag.Name,
                            Comment = tag.Name == "Start" ? Texts(("de-DE", "Starttaste"), ("en-US", "Start button")) : Texts(("en-US", "=SUM(A1)")),
                        })
                        .Concat(table.UserConstants.Select(constant => new PlcMemberCommentInfo { Kind = "UserConstant", Name = constant.Name, Comment = Texts(("en-US", "Offset value")) }))
                        .ToList(),
                }).ToList(),
            }).ToList(),
        };
    }

    private PlcContainerMutationInfo Container(WorkerRequest request)
    {
        var info = Info(request);
        string kind, path, name;
        string? unitName;
        switch (request.Method)
        {
            case PlcSourceFolderMethods.CreateTypeGroup:
            case PlcSourceFolderMethods.DeleteTypeGroup:
            {
                if (!PlcGroupPath.TryParse(info.GroupPath, out var group, out var error) || group!.IsRoot || group.Kind != PlcGroupPath.TypesKind)
                    throw new ScenarioFailure(WorkerFailureCategories.ValidationError, error ?? "not a PLC data type user group");
                var parent = FindFolder(group.UnitName is null ? _types : RequireUnit(group.UnitName).Types, group.Folders.Take(group.Folders.Count - 1).ToList());
                name = group.Folders[^1];
                AddOrRemove(parent, name, request.Method == PlcSourceFolderMethods.CreateTypeGroup);
                (kind, path, unitName) = (PlcSourceFolderKinds.TypeGroup, group.ToDisplayPath(), group.UnitName);
                break;
            }
            case PlcSourceFolderMethods.CreateTagTableGroup:
            case PlcSourceFolderMethods.DeleteTagTableGroup:
            {
                var segments = PlcWatchTablePolicy.Segments(info.FolderPath);
                var parent = FindFolder(info.UnitName is null ? _tags : RequireUnit(info.UnitName).Tags, segments.Take(segments.Count - 1).ToList());
                name = segments[^1];
                AddOrRemove(parent, name, request.Method == PlcSourceFolderMethods.CreateTagTableGroup);
                (kind, path, unitName) = (PlcSourceFolderKinds.TagTableGroup, "/" + string.Join("/", segments), info.UnitName);
                break;
            }
            case PlcSourceFolderMethods.CreateSoftwareUnit:
                if (_units.Any(u => Names.Equals(u.Name, info.UnitName)))
                    throw new ScenarioFailure(WorkerFailureCategories.WorkerOperationFailed, $"A software unit named '{info.UnitName}' already exists.");
                _units.Add(new Unit(info.UnitName!));
                (kind, path, unitName, name) = (PlcSourceFolderKinds.SoftwareUnit, $"{Plc}/Units/{info.UnitName}", info.UnitName, info.UnitName!);
                break;
            default:
            {
                var unit = RequireUnit(info.UnitName);
                _units.Remove(unit);
                _sources.RemoveAll(s => Names.Equals(s.UnitName, unit.Name));
                (kind, path, unitName, name) = (PlcSourceFolderKinds.SoftwareUnit, $"{Plc}/Units/{unit.Name}", unit.Name, unit.Name);
                break;
            }
        }

        return new PlcContainerMutationInfo { Operation = request.Method, Kind = kind, PlcName = Plc, DeviceName = Device, UnitName = unitName, Path = path, Name = name };
    }

    private PlcGroupRenameInfo RenameUnit(WorkerRequest request)
    {
        var info = Info(request);
        var unit = RequireUnit(info.UnitName);
        var old = unit.Name;
        unit.Name = info.NewName!;
        for (var i = 0; i < _sources.Count; i++)
            if (Names.Equals(_sources[i].UnitName, old)) _sources[i] = new PlcExternalSourceInfo { Name = _sources[i].Name, UnitName = unit.Name };
        return new PlcGroupRenameInfo
        {
            Operation = request.Method, Kind = PlcSourceFolderKinds.SoftwareUnit, PlcName = Plc, DeviceName = Device, UnitName = old,
            Path = $"{Plc}/Units/{old}", NewPath = $"{Plc}/Units/{unit.Name}", OldName = old, NewName = unit.Name,
        };
    }

    private static void AddOrRemove(Folder parent, string name, bool create)
    {
        var existing = parent.Folders.FirstOrDefault(f => Names.Equals(f.Name, name));
        if (create)
        {
            if (existing is not null) throw new ScenarioFailure(WorkerFailureCategories.WorkerOperationFailed, $"A group named '{name}' already exists.");
            parent.Folders.Add(new Folder(name));
        }
        else
        {
            parent.Folders.Remove(existing ?? throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"Folder '{name}' was not found."));
        }
    }

    private static Folder FindFolder(Folder root, IReadOnlyList<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
            current = current.Folders.FirstOrDefault(f => Names.Equals(f.Name, segment))
                ?? throw new ScenarioFailure(WorkerFailureCategories.TargetNotFound, $"Folder '{segment}' was not found.");
        return current;
    }

    private static string Ok(string payload, WorkerRequest request)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload, ResolvedProjectPath = request.ProjectPath ?? "source-folders" }, WorkerJson.Envelope);

    private static string Fail(string category, string message)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message }, WorkerJson.Envelope);
}
