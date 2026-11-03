using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.OpennessWorker.Openness.Block;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>The Siemens-free helpers of granular block editing: bundle text, member paths, DB value enumeration.</summary>
public sealed class PlcBlockEditContractsTests
{
    private static string Bundle() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "get_block_content.ob-lad.v2.bundle.txt"));

    [Fact]
    public void BundleReaderFindsTheSameDocumentAsTheWorkerParser()
    {
        var parsed = BlockImportBundleParser.Parse("Main.xml", Bundle());

        Assert.True(PlcBlockBundleText.TryGetSimaticMl(Bundle(), out var name, out var content));

        var authoritative = BlockImportRouting.SelectAuthoritativeDocument(parsed);
        Assert.Equal(authoritative.LogicalName, name);
        Assert.Equal(authoritative.Content, content);
    }

    [Fact]
    public void ReplacingTheDocumentKeepsEveryOtherDocumentAndDelimiter()
    {
        Assert.True(PlcBlockBundleText.TryGetSimaticMl(Bundle(), out _, out var xml));
        var edited = xml.Replace("<Name>Main</Name>", "<Name>Main</Name>").Replace("<Section Name=\"Temp\" />", "<Section Name=\"Temp\"><Member Name=\"t\" Datatype=\"Int\" /></Section>").TrimEnd('\n');

        var bundle = PlcBlockBundleText.ReplaceSimaticMl(Bundle(), edited);

        var before = BlockImportBundleParser.Parse("Main.xml", Bundle());
        var after = BlockImportBundleParser.Parse("Main.xml", bundle);
        Assert.Equal(before.Documents.Select(d => d.LogicalName), after.Documents.Select(d => d.LogicalName));
        Assert.Equal(edited + "\n", after.Documents[0].Content);
        Assert.Equal(before.Documents.Skip(1).Select(d => d.Content), after.Documents.Skip(1).Select(d => d.Content));
        BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(after, before, "Main.xml");
    }

    [Fact]
    public void ComposedBundlesRoundTrip()
    {
        var composed = BlockBundleFormat.Compose(new[]
        {
            new BlockImportDocument("Block.xml", "Block.xml", "<Document />"),
            new BlockImportDocument("Block.s7dcl", "Block.s7dcl", "FUNCTION_BLOCK \"Block\"\nEND_FUNCTION_BLOCK\n"),
        });

        Assert.True(PlcBlockBundleText.TryGetSimaticMl(composed, out _, out var xml));
        Assert.Equal("<Document />\n", xml);
        Assert.False(PlcBlockBundleText.TryGetSimaticMl("--- FILE: Block.s7dcl ---\nX", out _, out _));
        Assert.Throws<ArgumentException>(() => PlcBlockBundleText.ReplaceSimaticMl("<Document />", "<Document />"));
    }

    [Theory]
    [InlineData("Motor.Speed", true, "Motor.Speed", "Motor.Speed")]
    [InlineData("Axis[2].Position", true, "Axis[2].Position", "Axis[2].Position")]
    [InlineData("Matrix[ 1 , -2 ]", true, "Matrix[1,-2]", "Matrix[1,-2]")]
    [InlineData("\"a.b\".c", false, "\"a.b\".c", null)]
    [InlineData(" Spaced . Name ", false, "Spaced.Name", "Spaced.Name")]
    public void MemberPathsParseAndRender(string text, bool indices, string canonical, string? find)
    {
        var path = PlcMemberPath.Parse(text, indices);

        Assert.Equal(canonical, path.ToString());
        if (find is null) Assert.Throws<ArgumentException>(() => path.ToFindPath());
        else Assert.Equal(find, path.ToFindPath());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("a..b", true)]
    [InlineData("a.", true)]
    [InlineData("\"open", true)]
    [InlineData("a[1", true)]
    [InlineData("a[x]", true)]
    [InlineData("a[1]", false)]
    [InlineData("a]b", true)]
    public void MalformedMemberPathsAreRejected(string text, bool indices)
        => Assert.Throws<ArgumentException>(() => PlcMemberPath.Parse(text, indices));

    private static PlcInterfaceMemberInfo Member(string name, string dataType, params PlcInterfaceMemberInfo[] children)
        => new() { Name = name, Path = name, DataType = dataType, Members = children.ToList() };

    private static readonly IReadOnlyList<PlcInterfaceMemberInfo> Udt = new[]
    {
        Member("Gain", "Real"),
        Member("Inner", "Struct", Member("Flag", "Bool")),
    };

    private static IReadOnlyList<PlcInterfaceMemberInfo>? Resolve(string name) => name switch
    {
        "UDT_A" => Udt,
        "Loop" => new[] { Member("Self", "\"Loop\"") },
        _ => null,
    };

    [Fact]
    public void DbValuesExpandStructsTypesAndArrays()
    {
        var roots = new[]
        {
            Member("Speed", "Int"),
            Member("Limits", "Struct", Member("Min", "Int"), Member("Max", "Int")),
            Member("Config", "\"UDT_A\""),
            Member("Buffer", "Array[0..2] of Real"),
            Member("Grid", "Array[1..2, 0..1] of Bool"),
            Member("Items", "Array[0..1] of \"UDT_A\""),
            Member("Rows", "Array[0..1] of Struct", Member("X", "Int")),
            Member("Name", "String[20]"),
        };

        var tree = PlcDbValueTree.Build(roots, Resolve, subtree: null);

        Assert.True(tree.IsComplete);
        Assert.Equal(new[]
        {
            "Speed", "Limits.Min", "Limits.Max", "Config.Gain", "Config.Inner.Flag", "Buffer[0]", "Buffer[1]", "Buffer[2]",
            "Grid[1,0]", "Grid[1,1]", "Grid[2,0]", "Grid[2,1]",
            "Items[0].Gain", "Items[0].Inner.Flag", "Items[1].Gain", "Items[1].Inner.Flag", "Rows[0].X", "Rows[1].X", "Name",
        }, tree.Entries.Select(e => e.Path));
        Assert.All(tree.Entries, entry => Assert.Equal(PlcDbValueKinds.Value, entry.Kind));
        Assert.Equal("Real", tree.Entries.Single(e => e.Path == "Buffer[1]").DataType);
    }

    [Fact]
    public void UnexpandableMembersAreReportedAndMakeTheResultIncomplete()
    {
        var roots = new[]
        {
            Member("Timer", "\"TON_SYSTEM\""),
            Member("Big", "Array[0..999] of Int"),
            Member("Sym", "Array[0..MAX] of Int"),
            Member("Rec", "\"Loop\""),
            Member("a.b", "Int"),
        };

        var tree = PlcDbValueTree.Build(roots, Resolve, subtree: null);

        Assert.False(tree.IsComplete);
        Assert.Equal(new[] { "Timer", "Big", "Sym", "Rec.Self", "a.b" }, tree.Entries.Select(e => e.Path));
        Assert.All(tree.Entries, entry => Assert.Equal(PlcDbValueKinds.Unexpanded, entry.Kind));
        Assert.Contains("contains itself", tree.Entries.Single(e => e.Path == "Rec.Self").Message);
    }

    [Fact]
    public void NarrowingReachesIntoLargeArraysAndTypes()
    {
        var roots = new[] { Member("Big", "Array[0..999] of \"UDT_A\""), Member("Other", "Int") };

        var element = PlcDbValueTree.Build(roots, Resolve, "Big[500]");
        var leaf = PlcDbValueTree.Build(roots, Resolve, "big[7].inner.flag");
        var missing = PlcDbValueTree.Build(roots, Resolve, "Nope");

        Assert.True(element.SubtreeFound);
        Assert.Equal(new[] { "Big[500].Gain", "Big[500].Inner.Flag" }, element.Entries.Select(e => e.Path));
        Assert.Equal(new[] { "Big[7].Inner.Flag" }, leaf.Entries.Select(e => e.Path));
        Assert.False(missing.SubtreeFound);
        Assert.Empty(missing.Entries);
    }

    [Fact]
    public void TheEntryCapStopsTheEnumeration()
    {
        var tree = PlcDbValueTree.Build(new[] { Member("A", "Array[0..9] of Int") }, Resolve, null, maxEntries: 4);

        Assert.False(tree.IsComplete);
        Assert.Equal(4, tree.Entries.Count);
        Assert.Contains("Stopped after 4 values", Assert.Single(tree.Messages));
    }

    [Fact]
    public void MethodListsFeedTheCatalogs()
    {
        Assert.All(PlcBlockEditMethods.Read, m => Assert.Equal(TiaMcpServer.Contracts.Safety.OperationCapability.TemporaryExport,
            TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(m)));
        Assert.All(PlcBlockEditMethods.Write.Concat(PlcBlockEditMethods.InterfaceEdits), m => Assert.Equal(TiaMcpServer.Contracts.Safety.OperationCapability.ProjectMutation,
            TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(m)));
        Assert.All(PlcBlockEditMethods.Read.Concat(PlcBlockEditMethods.Write).Concat(PlcBlockEditMethods.InterfaceEdits), m =>
        {
            Assert.True(TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.Supports(TiaMcpServer.Contracts.Project.ProjectContainerKinds.StandaloneProject, m), m);
            Assert.True(TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.Supports(TiaMcpServer.Contracts.Project.ProjectContainerKinds.LocalSession, m), m);
        });
    }
}
