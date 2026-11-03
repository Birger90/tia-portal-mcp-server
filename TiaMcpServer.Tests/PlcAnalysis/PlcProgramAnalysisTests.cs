using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.PlcAnalysis;
using Xunit;

namespace TiaMcpServer.Tests.PlcAnalysis;

/// <summary>
/// The program graph and its analyses over fixture data shaped like the read_cross_references
/// reader's output (sources, children, references, locations with closed access/referenceType names).
/// The fixture is built from the reader's contract, not from recorded Siemens block results.
/// </summary>
public sealed class PlcProgramAnalysisTests
{
    private const string Device = "Station_1";

    private static ProjectTreeNode Block(string name, string type, string? unitOrSystem = null)
        => new()
        {
            Name = name, NodeType = type, Children = new(),
            Details = unitOrSystem == "system" ? new() { ["Number"] = "1", ["IsSystemBlock"] = "true" } : new() { ["Number"] = "1" },
        };

    private static ProjectTreeNode Folder(string name, string type, params ProjectTreeNode[] children) => new() { Name = name, NodeType = type, Children = children.ToList() };

    private static List<ProjectTreeNode> Tree(params ProjectTreeNode[] rootBlocks)
        => new()
        {
            Folder(Device, ProjectTreeNodeTypes.Device,
                Folder("PLC_1", ProjectTreeNodeTypes.PlcSoftware,
                    Folder("Program blocks", ProjectTreeNodeTypes.BlockFolder, rootBlocks),
                    Folder("PLC tags", ProjectTreeNodeTypes.TagTableFolder, Folder("Default tag table", ProjectTreeNodeTypes.TagTable),
                        Folder("Line", ProjectTreeNodeTypes.TagTableFolder, Folder("Motors", ProjectTreeNodeTypes.TagTable))),
                    Folder("PLC data types", ProjectTreeNodeTypes.TypeFolder, Folder("UDT_A", ProjectTreeNodeTypes.Type), Folder("UDT_B", ProjectTreeNodeTypes.Type)),
                    Folder("Unit_A", ProjectTreeNodeTypes.SoftwareUnit,
                        Folder("Program blocks", ProjectTreeNodeTypes.BlockFolder, Block("Unit_FC", ProjectTreeNodeTypes.Fc), Block("Dup", ProjectTreeNodeTypes.Fc))))),
        };

    private static ProjectTreeNode[] Program() => new[]
    {
        Block("Main", ProjectTreeNodeTypes.Ob), Block("Cyclic", ProjectTreeNodeTypes.Ob),
        Block("FB_Line", ProjectTreeNodeTypes.Fb), Block("FC_A", ProjectTreeNodeTypes.Fc), Block("FC_B", ProjectTreeNodeTypes.Fc),
        Block("FC_Shared", ProjectTreeNodeTypes.Fc), Block("FC_Self", ProjectTreeNodeTypes.Fc), Block("FC_Old", ProjectTreeNodeTypes.Fc),
        Block("FC_Dead", ProjectTreeNodeTypes.Fc), Block("FC_Lone", ProjectTreeNodeTypes.Fc), Block("FC_Maybe", ProjectTreeNodeTypes.Fc),
        Block("Line_DB", ProjectTreeNodeTypes.InstanceDb), Block("Data_DB", ProjectTreeNodeTypes.GlobalDb), Block("Hmi_DB", ProjectTreeNodeTypes.GlobalDb),
        Block("Dup", ProjectTreeNodeTypes.Fc),
        Folder("Motors", ProjectTreeNodeTypes.BlockFolder, Block("FB_Motor", ProjectTreeNodeTypes.Fb)),
        Folder("System blocks", ProjectTreeNodeTypes.SystemBlockFolder, Block("Sys_FB", ProjectTreeNodeTypes.Fb, "system")),
    };

    private static CrossReferenceTargetInfo Ref(string name, string access, string referenceType, string device = Device, string at = "NW1")
        => new()
        {
            Name = name, TypeName = "Block", Device = device,
            Locations = { new CrossReferenceLocationInfo { Name = at, Access = access, ReferenceType = referenceType, ReferenceLocation = at } },
        };

    private static CrossReferenceSourceInfo Src(string name, params CrossReferenceTargetInfo[] references)
        => new() { Name = name, Device = Device, References = references.ToList() };

    private static CrossReferenceReport Report(params CrossReferenceSourceInfo[] sources)
        => new() { Filter = CrossReferenceFilterNames.ObjectsWithReferences, IsComplete = true, OwnerQueryCount = 20, SuccessfulOwnerQueryCount = 20, Sources = sources.ToList() };

    private static CrossReferenceSourceInfo[] RootSources() => new[]
    {
        Src("Main", Ref("FB_Line", "Call", "Uses"), Ref("Line_DB", "InstanceDB", "Uses"), Ref("FC_A", "Call", "Uses"), Ref("FC_B", "Call", "Uses"),
            Ref("FC_Maybe", "Call", "Undefined"), Ref("Unit_FC", "Call", "Uses"), Ref("Dup", "Call", "Uses"), Ref("TON", "Call", "Uses")),
        new()
        {
            Name = "FB_Line", Device = Device, References = { Ref("FB_Motor", "Call", "Uses", at: "FB_Line line 12") },
            Children = { Src("Motor1", Ref("FB_Motor", "Multiinstance", "Uses", at: "FB_Line Static")) },
        },
        Src("FC_A", Ref("FC_Shared", "Call", "Uses")),
        Src("FC_B", Ref("FC_Shared", "Call", "Uses")),
        Src("FC_Shared", Ref("FC_Self", "Call", "Uses")),
        Src("FC_Self", Ref("FC_Self", "Call", "Uses"), Ref("FC_Self", "Definition", "Uses")),
        Src("FC_Old", Ref("FC_Dead", "Call", "Uses")),
        // The used side states the relation: Data_DB is read by Cyclic (UsedBy on a member child).
        new() { Name = "Data_DB", Device = Device, Children = { Src("Data_DB.Speed", Ref("Cyclic", "Read", "UsedBy")) } },
        Src("Hmi_DB", Ref("Panel_Tag", "Read", "UsedBy", device: "HMI_1")),
    };

    private static PlcProgramGraph Graph(bool withTypes = false, CrossReferenceSourceInfo[]? root = null, int skipped = 0)
    {
        var skips = Enumerable.Range(0, skipped).Select(_ => new ProjectTreeSkippedNodeInfo
        {
            ParentPath = new() { new() { NodeType = ProjectTreeNodeTypes.Device, Name = Device }, new() { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = "PLC_1" } },
            NodeType = ProjectTreeNodeTypes.Fc, Reason = "access denied",
        }).ToList();
        var (inventory, failure) = PlcProgramInventory.Select(Tree(Program()), skips, "PLC_1");
        Assert.Null(failure);
        var sweeps = new List<PlcSweep>
        {
            new(inventory!.BlockRoots[0], PlcSweepKind.Blocks, Report(root ?? RootSources())),
            new(inventory.BlockRoots[1], PlcSweepKind.Blocks, Report(Src("Unit_FC", Ref("Main", "Call", "UsedBy")))),
        };
        if (withTypes)
            sweeps.Add(new(inventory.TypeRoots[0], PlcSweepKind.Types, Report(
                Src("UDT_A", Ref("UDT_B", "Declaration", "UsedBy")), Src("UDT_B", Ref("Data_DB", "Declaration", "UsedBy")))));
        return PlcProgramGraph.Build(inventory, sweeps);
    }

    private static int Id(PlcProgramGraph graph, string name) => Assert.Single(graph.Lookup(name));

    private static bool Never() => false;

    [Fact]
    public void Inventory_ReadsBlocksPathsUnitsSystemBlocksTypesAndTables()
    {
        var (inventory, _) = PlcProgramInventory.Select(Tree(Program()), new List<ProjectTreeSkippedNodeInfo>(), plcName: null);
        Assert.Equal("PLC_1/Blocks/Motors/FB_Motor", inventory!.Blocks.Single(b => b.Name == "FB_Motor").BlockPath);
        Assert.Equal("PLC_1/Units/Unit_A/Blocks/Unit_FC", inventory.Blocks.Single(b => b.Name == "Unit_FC").BlockPath);
        var system = inventory.Blocks.Single(b => b.Name == "Sys_FB");
        Assert.True(system.IsSystemBlock);
        Assert.Null(system.BlockPath);
        Assert.Equal(new[] { "Station_1/PLC_1/Program blocks", "Station_1/PLC_1/Unit_A/Program blocks" },
            inventory.BlockRoots.Select(root => string.Join("/", root.Selector.Select(s => s.Name))));
        Assert.Equal("PLC_1/Types/UDT_A", inventory.Types[0].TypePath);
        Assert.Equal("/Line", inventory.TagTables.Single(t => t.Name == "Motors").FolderPath);

        Assert.Equal(WorkerFailureCategories.TargetNotFound, PlcProgramInventory.Select(Tree(), new List<ProjectTreeSkippedNodeInfo>(), "Other").Failure!.Category);
        Assert.Equal("FB_Motor", inventory.ResolveBlock(BlockAddress.Parse("PLC_1/Blocks/Motors/FB_Motor")).Block!.Name);
        Assert.Equal("FB_Motor", inventory.ResolveBlock(BlockAddress.Parse("FB_Motor")).Block!.Name);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, inventory.ResolveBlock(BlockAddress.Parse("PLC_1/Blocks/FB_Motor")).Failure!.Category);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, inventory.ResolveBlock(BlockAddress.Parse("Dup")).Failure!.Category);
        Assert.Equal("Unit_A", inventory.ResolveBlock(BlockAddress.Parse("PLC_1/Units/Unit_A/Blocks/Dup")).Block!.SoftwareUnit);
        Assert.Equal("UDT_B", inventory.ResolveType(PlcTypeAddress.Parse("PLC_1/Types/UDT_B")).Type!.Name);
        Assert.Equal("Motors", inventory.ResolveTagTable("motors", "/Line/").Table!.Name);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, inventory.ResolveTagTable("Motors", "/").Failure!.Category);
    }

    [Fact]
    public void Graph_MergesBothSidesAndKeepsUndirectedExternalAndAmbiguousEvidenceApart()
    {
        var graph = Graph();
        var main = Id(graph, "Main");
        var unitFc = Id(graph, "Unit_FC");
        var edge = Assert.Single(graph.Outgoing(main), e => e.To == unitFc);
        // Main's Uses and Unit_FC's UsedBy state the same call from both sides.
        Assert.Equal(new[] { "usedBy", "uses" }, edge.ReportedAs);
        Assert.Equal(2, edge.LocationCount);

        var line = Assert.Single(graph.Outgoing(Id(graph, "FB_Line")));
        Assert.Equal(new[] { PlcAnalysisPolicy.Call, PlcAnalysisPolicy.MultiInstance }, line.Relations);
        Assert.Equal(new[] { "Call", "Multiinstance" }, line.Accesses);

        var pair = Assert.Single(graph.Undirected);
        Assert.Equal(("Main", "FC_Maybe", "Call", "Undefined"), (graph.Nodes[pair.First].Name, graph.Nodes[pair.Second].Name, pair.Access, pair.ReferenceType));
        Assert.DoesNotContain(graph.Outgoing(main), e => graph.Nodes[e.To].Name == "FC_Maybe");

        Assert.Equal("Panel_Tag", Assert.Single(graph.ExternalUsers(Id(graph, "Hmi_DB"))).Name);
        Assert.Equal("TON", Assert.Single(graph.ExternalCallees(main)).Name);
        Assert.Single(graph.Outgoing(Id(graph, "Data_DB")).Concat(graph.Incoming(Id(graph, "Data_DB"))), e => graph.Nodes[e.From].Name == "Cyclic");

        // "Dup" exists in the PLC root and in Unit_A: never resolved, and the evidence says so.
        var evidence = graph.Evidence();
        Assert.Equal(1, evidence.AmbiguousReferenceCount);
        Assert.Equal(new[] { "Dup" }, evidence.AmbiguousNames);
        Assert.False(evidence.Complete);
        Assert.Equal(2, evidence.CrossReferenceQueries.Count);
        Assert.NotEmpty(evidence.Rules);
        Assert.NotEmpty(evidence.Limits);
    }

    [Fact]
    public void Graph_UnmatchedSourcesAndSkippedTreeNodesMakeTheEvidenceIncomplete()
    {
        var clean = RootSources().Select(source => source).ToList();
        clean[0] = Src("Main", Ref("FB_Line", "Call", "Uses"));
        Assert.True(Graph(root: clean.ToArray()).EvidenceComplete);

        var withGhost = clean.Append(Src("Ghost", Ref("Main", "Call", "UsedBy"))).ToArray();
        var ghost = Graph(root: withGhost).Evidence();
        Assert.False(ghost.Complete);
        Assert.Equal(new[] { "Ghost" }, ghost.UnmatchedSources);

        Assert.False(Graph(root: clean.ToArray(), skipped: 1).EvidenceComplete);
    }

    [Fact]
    public void CallTree_ShowsCallsInstancesCyclesRepeatsAndExternalCallees()
    {
        var graph = Graph();
        var tree = PlcProgramAnalyzer.CallTree(graph, Id(graph, "Main"), maxDepth: 10, maxElements: 100, Never);
        var byName = tree.Nodes.GroupBy(n => n.Block.Name).ToDictionary(g => g.Key, g => g.ToList());
        var root = tree.Nodes[0];
        Assert.Equal("Main", root.Block.Name);
        Assert.Null(root.Evidence);
        Assert.Equal("Line_DB", Assert.Single(root.Instances).Block.Name);
        Assert.Equal(PlcAnalysisPolicy.Instance, Assert.Single(root.Instances[0].Evidence.Relations));
        // FC_Shared is called by FC_A and FC_B: expanded once, then marked.
        Assert.Equal(2, byName["FC_Shared"].Count);
        Assert.Single(byName["FC_Shared"], n => n.ExpandedElsewhere && n.ChildrenOmitted);
        // FC_Self calls itself: the inner node is a cycle and not expanded.
        Assert.Single(byName["FC_Self"], n => n.Cycle);
        var ton = Assert.Single(byName["TON"]);
        Assert.False(ton.Block.InInventory);
        Assert.Null(ton.Block.Kind);
        Assert.Equal("FB_Motor", Assert.Single(byName["FB_Motor"]).Block.Name);
        Assert.Equal("PLC_1/Blocks/Motors/FB_Motor", byName["FB_Motor"][0].Block.Path);
        Assert.Equal("FC_Maybe", Assert.Single(tree.UndirectedCallEvidence).Second);
        Assert.DoesNotContain("FC_Maybe", byName.Keys);
        Assert.False(tree.Complete); // the evidence has the ambiguous "Dup"
        Assert.Empty(tree.Truncation);
        Assert.All(tree.Nodes.Skip(1), n => Assert.NotNull(n.ParentId));
    }

    [Fact]
    public void CallTree_DepthAndElementCapsAreNamed()
    {
        var graph = Graph();
        var shallow = PlcProgramAnalyzer.CallTree(graph, Id(graph, "Main"), maxDepth: 1, maxElements: 100, Never);
        Assert.Equal(new[] { PlcProgramAnalyzer.TruncatedByDepth }, shallow.Truncation);
        Assert.True(shallow.Nodes.Single(n => n.Block.Name == "FB_Line").ChildrenOmitted);
        Assert.All(shallow.Nodes, n => Assert.True(n.Depth <= 1));

        var few = PlcProgramAnalyzer.CallTree(graph, Id(graph, "Main"), maxDepth: 10, maxElements: 3, Never);
        Assert.Equal(3, few.NodeCount);
        Assert.Contains(PlcProgramAnalyzer.TruncatedByElements, few.Truncation);

        var timedOut = PlcProgramAnalyzer.CallTree(graph, Id(graph, "Main"), maxDepth: 10, maxElements: 100, () => true);
        Assert.Equal(new[] { PlcProgramAnalyzer.TruncatedByTime }, timedOut.Truncation);
        Assert.Empty(timedOut.Nodes);
    }

    [Fact]
    public void CallPaths_EnumeratesEveryObPathAndTreatsTheLastHopOfADataTargetAsAnyReference()
    {
        var graph = Graph();
        var shared = PlcProgramAnalyzer.CallPaths(graph, Id(graph, "FC_Self"), maxDepth: 20, maxPaths: 50, Never);
        Assert.Equal(new[] { "Main>FC_A>FC_Shared>FC_Self", "Main>FC_B>FC_Shared>FC_Self" },
            shared.Paths.Select(p => string.Join(">", p.Blocks)).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(new[] { "Main" }, shared.Roots);
        Assert.Equal("FC_Shared", Assert.Single(shared.DirectCallers).Name);

        var motor = PlcProgramAnalyzer.CallPaths(graph, Id(graph, "FB_Motor"), 20, 50, Never);
        var path = Assert.Single(motor.Paths);
        Assert.Equal(new[] { "call", "call+multiInstance" }, path.Relations);

        var data = PlcProgramAnalyzer.CallPaths(graph, Id(graph, "Data_DB"), 20, 50, Never);
        Assert.Equal("Cyclic>Data_DB", string.Join(">", Assert.Single(data.Paths).Blocks));
        Assert.Equal(new[] { "reference" }, data.Paths[0].Relations);

        var dead = PlcProgramAnalyzer.CallPaths(graph, Id(graph, "FC_Dead"), 20, 50, Never);
        Assert.Empty(dead.Paths);
        Assert.Equal("FC_Old", Assert.Single(dead.DirectCallers).Name);

        var capped = PlcProgramAnalyzer.CallPaths(graph, Id(graph, "FC_Self"), 20, 1, Never);
        Assert.Equal(1, capped.PathCount);
        Assert.Contains(PlcProgramAnalyzer.TruncatedByElements, capped.Truncation);
        var deep = PlcProgramAnalyzer.CallPaths(graph, Id(graph, "FC_Self"), 2, 50, Never);
        Assert.Empty(deep.Paths);
        Assert.Contains(PlcProgramAnalyzer.TruncatedByDepth, deep.Truncation);
    }

    [Fact]
    public void Unused_CategorizesUnreachableBlocksAndNeverCallsUndirectedOrExternalUseUnused()
    {
        var graph = Graph();
        var result = PlcProgramAnalyzer.Unused(graph, maxElements: 500);
        // "Dup" exists twice (PLC root and Unit_A); both copies are unreachable.
        Assert.Equal(2, result.Unused.Count(u => u.Block.Name == "Dup"));
        var unused = result.Unused.GroupBy(u => u.Block.Name).ToDictionary(g => g.Key, g => g.First().Category);
        Assert.Equal("unreferenced", unused["FC_Old"]);
        Assert.Equal("unreferenced", unused["FC_Lone"]);
        Assert.Equal("referencedOnlyByUnreachableBlocks", unused["FC_Dead"]);
        Assert.Equal(new[] { "FC_Old" }, result.Unused.Single(u => u.Block.Name == "FC_Dead").ReferencedBy);
        Assert.True(result.Unused.Single(u => u.Block.Name == "Sys_FB").Block.IsSystemBlock);
        Assert.Equal("Hmi_DB", Assert.Single(result.UsedOnlyOutsideProgram).Block.Name);
        Assert.Equal("FC_Maybe", Assert.Single(result.Undetermined).Block.Name);
        foreach (var reached in new[] { "Main", "Cyclic", "FB_Line", "FB_Motor", "Line_DB", "Data_DB", "FC_Self", "Unit_FC" })
            Assert.DoesNotContain(reached, unused.Keys);
        Assert.Equal(new[] { "Cyclic", "Main" }, result.Roots);

        var capped = PlcProgramAnalyzer.Unused(graph, maxElements: 2);
        Assert.Equal(2, capped.Unused.Count + capped.UsedOnlyOutsideProgram.Count + capped.Undetermined.Count);
        Assert.Equal(new[] { PlcProgramAnalyzer.TruncatedByElements }, capped.Truncation);
        Assert.Equal(result.UnusedCount, capped.UnusedCount);
    }

    [Fact]
    public void Unused_PropagatesUndeterminedOverWhatAnUndeterminedBlockUses_AndMakesTheResultIncomplete()
    {
        // The review probe: Main -> FC_Maybe is undirected evidence, FC_Maybe -> FC_Lone a directed call. FC_Lone is reached
        // only through the undetermined FC_Maybe, so it is undetermined too, never unused with complete:true. FC_Maybe also
        // shares undirected evidence with FC_Old, which calls FC_Dead: both follow FC_Maybe.
        CrossReferenceSourceInfo[] Probe(bool undirected) => new[]
        {
            Src("Main", undirected ? new[] { Ref("FB_Line", "Call", "Uses"), Ref("FC_Maybe", "Call", "Undefined") } : new[] { Ref("FB_Line", "Call", "Uses") }),
            Src("FC_Maybe", Ref("FC_Lone", "Call", "Uses"), Ref("FC_Old", "Read", "Undefined")),
            Src("FC_Old", Ref("FC_Dead", "Call", "Uses")),
        };

        var graph = Graph(root: Probe(undirected: true));
        Assert.True(graph.EvidenceComplete);
        var result = PlcProgramAnalyzer.Unused(graph, maxElements: 500);
        Assert.Equal(new[] { "FC_Dead", "FC_Lone", "FC_Maybe", "FC_Old" }, result.Undetermined.Select(u => u.Block.Name));
        Assert.Equal(4, result.UndeterminedCount);
        Assert.DoesNotContain(result.Unused, u => u.Block.Name is "FC_Lone" or "FC_Maybe" or "FC_Old" or "FC_Dead");
        Assert.Equal(new[] { "FC_Maybe" }, result.Undetermined.Single(u => u.Block.Name == "FC_Lone").ReferencedBy);
        Assert.False(result.Complete);
        Assert.Empty(result.Truncation);
        Assert.Contains(result.Evidence.Limits, limit => limit.StartsWith("4 block(s) are undetermined", StringComparison.Ordinal));
        Assert.True(result.Evidence.Complete);
        Assert.False(PlcProgramAnalyzer.Undetermined(graph, PlcProgramAnalyzer.Reachable(graph))[Id(graph, "Main")]);

        // Without the undirected evidence nothing is undetermined: the same blocks are plainly unused and the result is complete.
        var decided = PlcProgramAnalyzer.Unused(Graph(root: Probe(undirected: false)), maxElements: 500);
        Assert.Empty(decided.Undetermined);
        Assert.True(decided.Complete);
        Assert.Equal("referencedOnlyByUnreachableBlocks", decided.Unused.Single(u => u.Block.Name == "FC_Lone").Category);
        Assert.DoesNotContain(decided.Evidence.Limits, limit => limit.Contains("undetermined", StringComparison.Ordinal));
    }

    [Fact]
    public void InstanceDbs_AreOrphansOnlyWhenTheirFbIsUnreachable_UnresolvedAndAmbiguousNamesAreSeparate()
    {
        var graph = Graph();
        var reached = PlcProgramAnalyzer.Reachable(graph);
        var db = graph.Nodes[Id(graph, "Line_DB")];
        void AssertResolved(string name)
        {
            var (resolved, none) = PlcProgramAnalyzer.ClassifyInstanceDb(graph, reached, db, name);
            Assert.Equal(PlcProgramAnalyzer.InstanceDbBucket.Resolved, resolved);
            Assert.Null(none);
        }

        AssertResolved("FB_Line");
        var (bucket, missing) = PlcProgramAnalyzer.ClassifyInstanceDb(graph, reached, db, "FB_Gone");
        Assert.Equal(PlcProgramAnalyzer.InstanceDbBucket.Unresolved, bucket);
        Assert.Equal("instanceOfNotInInventory", missing!.Category);
        Assert.Contains("system FB", missing.Reason);
        Assert.Contains("Not reported as an orphan", missing.Reason);
        Assert.True(missing.DbReachable);
        var (orphanBucket, orphan) = PlcProgramAnalyzer.ClassifyInstanceDb(graph, reached, db, "Sys_FB");
        Assert.Equal((PlcProgramAnalyzer.InstanceDbBucket.Orphan, "instanceOfUnreachable"), (orphanBucket, orphan!.Category));
        AssertResolved("UDT_A");
        // "Dup" names two FCs (PLC root and Unit_A): never resolved, never silently counted as checked.
        var (ambiguousBucket, ambiguous) = PlcProgramAnalyzer.ClassifyInstanceDb(graph, reached, db, "Dup");
        Assert.Equal(PlcProgramAnalyzer.InstanceDbBucket.Ambiguous, ambiguousBucket);
        Assert.Equal(("instanceOfAmbiguous", "Dup"), (ambiguous!.Category, ambiguous.InstanceOfName));
        Assert.Contains("2 blocks or PLC data types", ambiguous.Reason);
    }

    [Fact]
    public void Impact_FollowsEveryDirectedRelationBackToTheRoots()
    {
        var graph = Graph(withTypes: true);
        var direct = graph.Incoming(Id(graph, "UDT_A")).Select(e => (e.From, "UDT_A", (IReadOnlyList<string>)e.Relations.ToArray(), (IReadOnlyList<string>)e.Accesses.ToArray())).ToList();
        var (users, directCount, truncation) = PlcProgramAnalyzer.Impact(graph, direct, Id(graph, "UDT_A"), 10, 500, Never);
        Assert.Equal(1, directCount);
        Assert.Equal(new[] { ("UDT_B", 1, "UDT_A"), ("Data_DB", 2, "UDT_B"), ("Cyclic", 3, "Data_DB") },
            users.Select(u => (u.Object.Name, u.Depth, u.Via)));
        Assert.Equal("Type", users[0].Object.Kind);
        Assert.Empty(truncation);

        var (shallow, _, cut) = PlcProgramAnalyzer.Impact(graph, direct, Id(graph, "UDT_A"), 2, 500, Never);
        Assert.Equal(2, shallow.Count);
        Assert.Equal(new[] { PlcProgramAnalyzer.TruncatedByDepth }, cut);
    }

    [Fact]
    public void LeafUses_TakeUsedByAsUsersAndKeepOtherDevicesExternal()
    {
        var graph = Graph();
        var report = Report(new CrossReferenceSourceInfo
        {
            Name = "Start", Device = Device,
            References = { Ref("FB_Motor", "Read", "UsedBy"), Ref("Screen_1", "Read", "UsedBy", device: "HMI_1"), Ref("FC_A", "Read", "Undefined"), Ref("FC_B", "Write", "Uses") },
        });
        var uses = graph.CollectLeafUses(report);
        Assert.Equal("FB_Motor", graph.Nodes[Assert.Single(uses.Users).User].Name);
        Assert.Equal("Screen_1", Assert.Single(uses.ExternalUsers).Name);
        Assert.Equal("FC_A", graph.Nodes[Assert.Single(uses.Undirected).Other].Name);
    }

    [Theory]
    [InlineData("Call", "call")]
    [InlineData("UC", "call")]
    [InlineData("CC", "call")]
    [InlineData("Multiinstance", "multiInstance")]
    [InlineData("MultiinstanceAndSymbol", "multiInstance")]
    [InlineData("InstanceDB", "instance")]
    [InlineData("InstanceAndSymbol", "instance")]
    [InlineData("Parameterinstance", "parameterInstance")]
    [InlineData("Read", "reference")]
    [InlineData("Undefined", "reference")]
    [InlineData("Unknown", "reference")]
    public void Policy_MapsEveryAccessNameToOneRelation(string access, string relation)
    {
        Assert.Contains(access, CrossReferenceAccessNames.All);
        Assert.Equal(relation, PlcAnalysisPolicy.Relation(access));
    }

    [Fact]
    public void Policy_OnlyUsesAndUsedByStateADirection()
    {
        foreach (var name in CrossReferenceTypeNames.All)
            Assert.Equal(name switch { "Uses" => true, "UsedBy" => false, _ => (bool?)null }, PlcAnalysisPolicy.SourceUsesReference(name));
    }

    [Theory]
    [InlineData("get_call_tree", null, "missing required field(s): blockPath")]
    [InlineData("get_call_tree", "maxDepth", "maxDepth must be between 1 and 50")]
    [InlineData("find_call_paths", "maxElements", "maxElements must be between 1 and 1000")]
    [InlineData("find_unused_blocks", "timeoutSeconds", "timeoutSeconds must be between 1 and 600")]
    [InlineData("analyze_change_impact", null, "exactly one target")]
    [InlineData("analyze_change_impact", "twoTargets", "exactly one target")]
    [InlineData("analyze_change_impact", "tableOnly", "needs both tableName and name")]
    [InlineData("analyze_change_impact", "plcWithBlock", "plcName applies to a tag target only")]
    [InlineData("find_unused_blocks", "blockPath", "'blockPath' is not valid for find_unused_blocks")]
    [InlineData("get_call_tree", "badPath", "Block path must be")]
    public void Catalog_ValidatesAnalysisItemsBeforeAnyWorkerCall(string operation, string? fault, string expected)
    {
        var request = new PlcOperationRequest { OperationId = "a", Operation = operation };
        if (operation is "get_call_tree" or "find_call_paths" && fault is not null) request.BlockPath = "PLC_1/Main";
        switch (fault)
        {
            case "maxDepth": request.MaxDepth = 51; break;
            case "maxElements": request.MaxElements = 1001; break;
            case "timeoutSeconds": request.TimeoutSeconds = 0; break;
            case "twoTargets": request.BlockPath = "PLC_1/Main"; request.TypePath = "PLC_1/Types/UDT_A"; break;
            case "tableOnly": request.TableName = "Default tag table"; break;
            case "plcWithBlock": request.BlockPath = "PLC_1/Main"; request.PlcName = "PLC_1"; break;
            case "blockPath": request.BlockPath = "PLC_1/Main"; break;
            case "badPath": request.BlockPath = "PLC_1/Units/Main"; break;
        }

        var validation = PlcOperationCatalog.ValidateRead(new[] { request });
        Assert.False(validation.IsValid);
        Assert.Contains(expected, validation.Error);
    }

    [Fact]
    public void Catalog_AcceptsEveryAnalysisTargetShape()
    {
        var valid = new[]
        {
            new PlcOperationRequest { OperationId = "a", Operation = "get_call_tree", BlockPath = "PLC_1/Main", MaxDepth = 50, MaxElements = 2000, TimeoutSeconds = 600 },
            new PlcOperationRequest { OperationId = "b", Operation = "find_call_paths", BlockPath = "Main" },
            new PlcOperationRequest { OperationId = "c", Operation = "find_unused_blocks", PlcName = "PLC_1", MaxElements = 5000 },
            new PlcOperationRequest { OperationId = "d", Operation = "find_orphaned_instance_dbs" },
            new PlcOperationRequest { OperationId = "e", Operation = "analyze_change_impact", TypePath = "PLC_1/Types/UDT_A" },
            new PlcOperationRequest { OperationId = "f", Operation = "analyze_change_impact", TableName = "Default tag table", Name = "Start", PlcName = "PLC_1", FolderPath = "/" },
        };
        var validation = PlcOperationCatalog.ValidateRead(valid);
        Assert.True(validation.IsValid, validation.Error);
        Assert.All(PlcAnalysisMethods.All, op => Assert.Contains(op, PlcOperationCatalog.ReadOperationNames));
        Assert.All(PlcAnalysisMethods.All, op => Assert.Equal(TiaMcpServer.Contracts.Safety.OperationCapability.Observe,
            TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(op)));
    }

    [Fact]
    public void InstanceDbXml_ReadsInstanceOfNameAndNeverGuesses()
    {
        const string db = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Document><Engineering version=\"V21\" /><SW.Blocks.InstanceDB ID=\"0\"><AttributeList><InstanceOfName> FB_Line </InstanceOfName></AttributeList></SW.Blocks.InstanceDB></Document>";
        Assert.Equal("FB_Line", InstanceDbXml.Read(db).InstanceOfName);
        Assert.Equal("FB_Line", InstanceDbXml.Read($"--- FILE: Line_DB.xml ---\n{db}\n--- FILE: Line_DB.s7res ---\nx").InstanceOfName);
        Assert.Equal(InstanceDbXml.NotAnInstanceDbDocument, InstanceDbXml.Read("--- FILE: a.s7dcl ---\nx").Category);
        Assert.Equal(InstanceDbXml.NoInstanceOfName, InstanceDbXml.Read("<Document><SW.Blocks.InstanceDB><AttributeList /></SW.Blocks.InstanceDB></Document>").Category);
        Assert.Equal(InstanceDbXml.NotAnInstanceDbDocument, InstanceDbXml.Read("<Document><SW.Blocks.GlobalDB><AttributeList><InstanceOfName>X</InstanceOfName></AttributeList></SW.Blocks.GlobalDB></Document>").Category);
        Assert.Equal(InstanceDbXml.NotAnInstanceDbDocument, InstanceDbXml.Read("<Document>").Category);
        Assert.Equal(InstanceDbXml.NotAnInstanceDbDocument,
            InstanceDbXml.Read("<!DOCTYPE d [<!ENTITY e \"FB\">]><Document><SW.Blocks.InstanceDB><AttributeList><InstanceOfName>&e;</InstanceOfName></AttributeList></SW.Blocks.InstanceDB></Document>").Category);
    }
}
