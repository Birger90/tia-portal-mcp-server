using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// <c>program-analysis</c> fixture for the plc_read program analysis and plc_online_read
/// read_watch_table_live. PLC_1 (Station_1) has a root block folder with a subfolder Motors, a system
/// block folder, a software unit Unit_A, two PLC data types, two tag tables and a watch table Watch_1.
/// Cross-reference reports state relationships from either side (Uses on the user, UsedBy on the used
/// object), one undirected pair (referenceType Undefined), a self call, an HMI user on another device
/// and an instruction (TON) outside the inventory. Instance DB exports carry InstanceOfName; one is a
/// bundle. This models the reader's payload shape only; it is no evidence of Siemens behavior.
/// </summary>
sealed class ProgramAnalysisScenario
{
    private const string Device = "Station_1";
    private const string Plc = "PLC_1";
    private const string RootFolder = "Program blocks";

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        return request.Method switch
        {
            "read_hardware_config" => Ok("{}", request),
            "browse_project_tree_v3_snapshot" => Ok(WorkerJson.SerializePayload(Tree()), request),
            "read_cross_references" => CrossReferences(request),
            "get_block_content" => Export(request),
            "list_tag_tables" => TagTables(request),
            PlcOnlineMethods.ReadWatchTable => WatchTable(request),
            _ => Fail(WorkerFailureCategories.ValidationError, $"program-analysis does not script '{request.Method}'."),
        };
    }

    private static ProjectTreeBrowseResultInfo Tree()
    {
        static ProjectTreeNode Block(string name, string type, int number, string language = "LAD", bool system = false)
            => new()
            {
                Name = name, NodeType = type, Children = new(),
                Details = system
                    ? new() { ["Number"] = number.ToString(), ["ProgrammingLanguage"] = language, ["IsSystemBlock"] = "true" }
                    : new() { ["Number"] = number.ToString(), ["ProgrammingLanguage"] = language },
            };
        static ProjectTreeNode Folder(string name, string type, params ProjectTreeNode[] children) => new() { Name = name, NodeType = type, Children = children.ToList() };

        var blocks = Folder(RootFolder, ProjectTreeNodeTypes.BlockFolder,
            Block("Main", ProjectTreeNodeTypes.Ob, 1), Block("Cyclic", ProjectTreeNodeTypes.Ob, 30),
            Block("FB_Line", ProjectTreeNodeTypes.Fb, 1, "SCL"), Block("FB_Unused", ProjectTreeNodeTypes.Fb, 3),
            Block("FC_Util", ProjectTreeNodeTypes.Fc, 1), Block("FC_Recursive", ProjectTreeNodeTypes.Fc, 2),
            Block("FC_Old", ProjectTreeNodeTypes.Fc, 3), Block("FC_Dead", ProjectTreeNodeTypes.Fc, 4),
            Block("FC_Orphan", ProjectTreeNodeTypes.Fc, 5), Block("FC_Maybe", ProjectTreeNodeTypes.Fc, 6),
            Block("Line_DB", ProjectTreeNodeTypes.InstanceDb, 1, "DB"), Block("Old_DB", ProjectTreeNodeTypes.InstanceDb, 2, "DB"),
            Block("Dead_DB", ProjectTreeNodeTypes.InstanceDb, 3, "DB"), Block("Timer_DB", ProjectTreeNodeTypes.InstanceDb, 4, "DB"),
            Block("Settings_DB", ProjectTreeNodeTypes.GlobalDb, 10, "DB"), Block("Hmi_DB", ProjectTreeNodeTypes.GlobalDb, 11, "DB"),
            Folder("Motors", ProjectTreeNodeTypes.BlockFolder, Block("FB_Motor", ProjectTreeNodeTypes.Fb, 2, "SCL")),
            Folder("System blocks", ProjectTreeNodeTypes.SystemBlockFolder, Block("Sys_FB", ProjectTreeNodeTypes.Fb, 100, "STL", system: true)));
        var tags = Folder("PLC tags", ProjectTreeNodeTypes.TagTableFolder,
            Folder("Default tag table", ProjectTreeNodeTypes.TagTable),
            Folder("Line", ProjectTreeNodeTypes.TagTableFolder, Folder("Motors", ProjectTreeNodeTypes.TagTable)));
        var types = Folder("PLC data types", ProjectTreeNodeTypes.TypeFolder,
            Folder("UDT_Settings", ProjectTreeNodeTypes.Type), Folder("UDT_Inner", ProjectTreeNodeTypes.Type));
        var unit = Folder("Unit_A", ProjectTreeNodeTypes.SoftwareUnit,
            Folder(RootFolder, ProjectTreeNodeTypes.BlockFolder, Block("Unit_FC", ProjectTreeNodeTypes.Fc, 20)),
            Folder("PLC tags", ProjectTreeNodeTypes.TagTableFolder),
            Folder("PLC data types", ProjectTreeNodeTypes.TypeFolder));
        var software = Folder(Plc, ProjectTreeNodeTypes.PlcSoftware, blocks, tags, types, unit);
        return new ProjectTreeBrowseResultInfo
        {
            StartSelector = null, Depth = null, Skipped = new(),
            Roots = new() { Folder(Device, ProjectTreeNodeTypes.Device, software) },
        };
    }

    private static CrossReferenceLocationInfo Location(string access, string referenceType, string at)
        => new() { Name = at, Access = access, ReferenceType = referenceType, ReferenceLocation = at, TypeName = "Location" };

    private static CrossReferenceTargetInfo Ref(string name, string access, string referenceType, string at, string device = Device, string typeName = "Block")
        => new() { Name = name, TypeName = typeName, Device = device, Path = string.Empty, Locations = { Location(access, referenceType, at) } };

    private static CrossReferenceSourceInfo Source(string name, params CrossReferenceTargetInfo[] references)
        => new() { Name = name, TypeName = "Block", Device = Device, References = references.ToList() };

    private static string CrossReferences(WorkerRequest request)
    {
        var selector = request.CrossReferenceSelector!;
        var path = string.Join("/", selector.Path.Select(segment => segment.Name));
        var sources = path switch
        {
            $"{Device}/{Plc}/{RootFolder}" => new List<CrossReferenceSourceInfo>
            {
                Source("Main",
                    Ref("FB_Line", "Call", "Uses", "Main NW1"), Ref("Line_DB", "InstanceDB", "Uses", "Main NW1"),
                    Ref("FC_Util", "Call", "Uses", "Main NW2"), Ref("Unit_FC", "Call", "Uses", "Main NW3"),
                    Ref("FC_Maybe", "Call", "Undefined", "Main NW4")),
                Source("Cyclic", Ref("FC_Util", "Call", "Uses", "Cyclic NW1")),
                new()
                {
                    Name = "FB_Line", TypeName = "Block", Device = Device,
                    References = { Ref("FB_Motor", "Call", "Uses", "FB_Line line 12") },
                    Children = { Source("Motor1", Ref("FB_Motor", "Multiinstance", "Uses", "FB_Line interface Static")) },
                },
                Source("FB_Motor", Ref("TON", "Call", "Uses", "FB_Motor line 3", typeName: "Instruction"), Ref("Start", "Read", "Uses", "FB_Motor line 4", typeName: "Tag")),
                Source("FC_Util", Ref("FC_Recursive", "Call", "Uses", "FC_Util NW1")),
                Source("FC_Recursive", Ref("FC_Recursive", "Call", "Uses", "FC_Recursive NW1")),
                Source("FC_Old", Ref("FC_Dead", "Call", "Uses", "FC_Old NW1")),
                new()
                {
                    Name = "Settings_DB", TypeName = "Block", Device = Device,
                    Children = { Source("Settings_DB.Speed", Ref("Main", "Read", "UsedBy", "Main NW5")) },
                },
                Source("Hmi_DB", Ref("HMI_Tag_1", "Read", "UsedBy", "HMI_RT_1 tag table", device: "HMI_1", typeName: "Tag")),
            },
            $"{Device}/{Plc}/Unit_A/{RootFolder}" => new List<CrossReferenceSourceInfo>
            {
                Source("Unit_FC", Ref("Main", "Call", "UsedBy", "Main NW3")),
            },
            $"{Device}/{Plc}/PLC data types" => new List<CrossReferenceSourceInfo>
            {
                Source("UDT_Settings", Ref("Settings_DB", "Declaration", "UsedBy", "Settings_DB interface")),
                Source("UDT_Inner", Ref("UDT_Settings", "Declaration", "UsedBy", "UDT_Settings member Inner")),
            },
            $"{Device}/{Plc}/Unit_A/PLC data types" => new List<CrossReferenceSourceInfo>(),
            $"{Device}/{Plc}/PLC tags/Default tag table" when selector.Member is { Kind: CrossReferenceMemberKinds.Tag, Name: "Start" } => new List<CrossReferenceSourceInfo>
            {
                new()
                {
                    Name = "Start", TypeName = "Tag", Device = Device, Address = "%I0.0",
                    References = { Ref("FB_Motor", "Read", "UsedBy", "FB_Motor line 4"), Ref("Screen_1", "Read", "UsedBy", "Screen_1 button", device: "HMI_1", typeName: "Screen") },
                },
            },
            _ => null,
        };
        if (sources is null)
            return Fail(WorkerFailureCategories.TargetNotFound, "The cross-reference target selector did not resolve to a target.");

        static int Count(IEnumerable<CrossReferenceSourceInfo> list) => list.Sum(source => 1 + Count(source.Children));
        static int References(IEnumerable<CrossReferenceSourceInfo> list) => list.Sum(source => source.References.Count + References(source.Children));
        var report = new CrossReferenceReport
        {
            Target = selector, Filter = request.CrossReferenceFilter ?? CrossReferenceFilterNames.Default, IsComplete = true,
            OwnerQueryCount = Math.Max(1, sources.Count), SuccessfulOwnerQueryCount = Math.Max(1, sources.Count), Sources = sources,
            TotalSourceCount = Count(sources), TotalReferenceCount = References(sources), TotalLocationCount = References(sources),
        };
        return Ok(WorkerJson.SerializePayload(report), request);
    }

    private static string InstanceDb(string name, string instanceOf)
        => $"""
           <?xml version="1.0" encoding="utf-8"?>
           <Document>
             <Engineering version="V21" />
             <SW.Blocks.InstanceDB ID="0">
               <AttributeList>
                 <InstanceOfName>{instanceOf}</InstanceOfName>
                 <Name>{name}</Name>
                 <ProgrammingLanguage>DB</ProgrammingLanguage>
               </AttributeList>
             </SW.Blocks.InstanceDB>
           </Document>
           """;

    private static string Export(WorkerRequest request)
        => request.BlockPath switch
        {
            $"{Plc}/Blocks/Line_DB" => Ok($"--- FILE: Line_DB.xml ---\n{InstanceDb("Line_DB", "FB_Line")}\n--- FILE: Line_DB.s7res ---\nres", request),
            $"{Plc}/Blocks/Old_DB" => Ok(InstanceDb("Old_DB", "FB_Gone"), request),
            $"{Plc}/Blocks/Dead_DB" => Ok(InstanceDb("Dead_DB", "FB_Unused"), request),
            $"{Plc}/Blocks/Timer_DB" => Ok("<Document><SW.Blocks.InstanceDB ID=\"0\"><AttributeList><Name>Timer_DB</Name></AttributeList></SW.Blocks.InstanceDB></Document>", request),
            _ => Fail(WorkerFailureCategories.TargetNotFound, $"Block '{request.BlockPath}' not found."),
        };

    private static List<TagTableInfo> Tables() => new()
    {
        new()
        {
            Name = "Default tag table", FolderPath = "/", IsDefault = true,
            Tags =
            {
                new TagInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0" },
                new TagInfo { Name = "Motor_On", DataType = "Bool", LogicalAddress = "%Q0.0" },
                new TagInfo { Name = "Odd\"Name", DataType = "Bool", LogicalAddress = "%M0.0" },
            },
            UserConstants = { new UserConstantInfo { Name = "MaxSpeed", DataType = "Int", Value = "100" } },
        },
        new() { Name = "Motors", FolderPath = "/Line", Tags = { new TagInfo { Name = "Speed", DataType = "Int", LogicalAddress = "%MW10" } } },
    };

    private static string TagTables(WorkerRequest request)
    {
        if (request.PlcName is { } plcName && !string.Equals(plcName, Plc, StringComparison.OrdinalIgnoreCase) && !string.Equals(plcName, Device, StringComparison.OrdinalIgnoreCase))
            return Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo()), request);
        var tables = Tables().Where(table => request.TableName is null || string.Equals(table.Name, request.TableName, StringComparison.OrdinalIgnoreCase))
            .Where(table => request.FolderPath is null || string.Equals(table.FolderPath, request.FolderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (request.TableName is not null && tables.Count == 0)
            return Fail(WorkerFailureCategories.TargetNotFound, $"Tag table '{request.TableName}' was not found.");
        return Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo
        {
            Plcs = { new PlcTagInventoryPlcInfo { PlcName = Plc, DeviceName = Device, Tables = tables } },
        }), request);
    }

    private static string WatchTable(WorkerRequest request)
    {
        if (!string.Equals(request.TableName, "Watch_1", StringComparison.OrdinalIgnoreCase))
            return Fail(WorkerFailureCategories.TargetNotFound, $"Watch table '{request.TableName}' was not found.");
        var names = new[] { "\"Motor_On\"", "Speed", "\"DB_Line\".Speed", "%MW20", null, "\"DB_Line\".Missing", "DB_Line.Speed", "\"DB_Line\".Sub.Speed" };
        var entries = names.Select((name, index) => PlcWatchTablePolicy.WatchEntry(index, attribute => attribute switch
        {
            "Name" => (true, name),
            "Address" => (true, name == "%MW20" ? "%MW20" : null),
            "ModifyIntention" => (true, false),
            _ => (true, (object?)null),
        })).ToList();
        var max = request.PlcOnline?.MaxElements ?? PlcWatchTablePolicy.DefaultMaxEntries;
        var listed = entries.Take(max).ToList();
        return Ok(WorkerJson.SerializePayload(new PlcWatchTableReadInfo
        {
            PlcName = Plc, DeviceName = Device, GroupPath = "/", Name = "Watch_1", IsConsistent = true,
            Entries = listed, EntryCount = listed.Count, EntriesComplete = listed.Count == entries.Count,
        }), request);
    }

    private static string Ok(string payload, WorkerRequest request)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload, ResolvedProjectPath = request.ProjectPath ?? "program-analysis" }, WorkerJson.Envelope);

    private static string Fail(string category, string message)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message }, WorkerJson.Envelope);
}
