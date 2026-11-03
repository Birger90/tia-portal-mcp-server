using TiaMcpServer.Contracts.PlcOnline;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>The Siemens-free compare projection caps, the GoOnline polling loop and the request validation rules.</summary>
public sealed class PlcOnlinePolicyTests
{
    private sealed record Node(string Name, string State, params Node[] Children);

    private static PlcCompareProjection Project(Node root, int maxDepth = 16, int maxElements = 300, bool differencesOnly = true)
        => PlcOnlinePolicy.ProjectTree(root, n => n.Name, n => n.Name + "'", n => n.State, n => n.State == "ObjectsDifferent" ? "differs" : null,
            n => n.Children, maxDepth, maxElements, differencesOnly);

    private static readonly Node Tree = new("PLC_1", "FolderContainsDifferencesOwnStateDifferent",
        new Node("Blocks", "FolderContainsDifferencesOwnStateDifferent",
            new Node("Main", "ObjectsDifferent"),
            new Node("Same", "ObjectsIdentical"),
            new Node("Gone", "RightMissing")),
        new Node("Tags", "FolderContentsIdentical", new Node("Tag", "ObjectsIdentical")));

    [Fact]
    public void ProjectTree_SkipsIdenticalElementsAndCountsThem()
    {
        var projection = Project(Tree);

        Assert.True(projection.Complete);
        Assert.Equal(4, projection.Listed);
        Assert.Equal(2, projection.Skipped);
        var root = projection.Root!;
        Assert.Equal("PLC_1", root.LeftName);
        Assert.Equal("PLC_1'", root.RightName);
        var blocks = Assert.Single(root.Elements);
        Assert.Equal(new[] { "Main", "Gone" }, blocks.Elements.Select(e => e.LeftName));
        Assert.Equal("differs", blocks.Elements[0].DetailedInformation);
        Assert.False(blocks.ElementsTruncated);
    }

    [Fact]
    public void ProjectTree_ListsEveryElementWithoutDifferencesOnly()
    {
        var projection = Project(Tree, differencesOnly: false);

        Assert.Equal(7, projection.Listed);
        Assert.Equal(0, projection.Skipped);
        Assert.True(projection.Complete);
    }

    [Fact]
    public void ProjectTree_DepthCapTruncatesAndExplains()
    {
        var projection = Project(Tree, maxDepth: 2, differencesOnly: false);

        Assert.False(projection.Complete);
        Assert.Contains("depth cap of 2", projection.Omission);
        Assert.All(projection.Root!.Elements, child => Assert.Empty(child.Elements));
        Assert.All(projection.Root.Elements, child => Assert.True(child.ElementsTruncated));
        Assert.Equal(3, projection.Listed);
    }

    [Fact]
    public void ProjectTree_ElementCapStopsTheWalkAndMarksEveryCutParent()
    {
        var projection = Project(Tree, maxElements: 3, differencesOnly: false);

        Assert.False(projection.Complete);
        Assert.Contains("element cap of 3", projection.Omission);
        Assert.Equal(3, projection.Listed);
        Assert.True(projection.Root!.ElementsTruncated);
        Assert.True(projection.Root.Elements[0].ElementsTruncated);
    }

    [Fact]
    public void ProjectTree_ProjectsAMissingRootAsEmpty()
    {
        var projection = PlcOnlinePolicy.ProjectTree<Node>(null, n => n.Name, n => n.Name, n => n.State, n => null, n => n.Children, 16, 300, true);

        Assert.Null(projection.Root);
        Assert.True(projection.Complete);
        var result = projection.ApplyTo(new PlcCompareResultInfo());
        Assert.Null(result.RootState);
        Assert.False(result.Identical);
    }

    [Theory]
    [InlineData("ObjectsIdentical", true)]
    [InlineData("FolderContentsIdentical", true)]
    [InlineData("FolderContentEqualOwnStateDifferent", false)]
    [InlineData("CompareIrrelevant", false)]
    public void ApplyTo_SetsIdenticalFromTheRootState(string state, bool identical)
    {
        var result = Project(new Node("PLC_1", state)).ApplyTo(new PlcCompareResultInfo());

        Assert.Equal(state, result.RootState);
        Assert.Equal(identical, result.Identical);
    }

    [Fact]
    public void Wait_PollsUntilOnlineAndRecordsDistinctStates()
    {
        var states = new Queue<string>(new[] { "Offline", "Connecting", "Connecting", "Online" });
        long clock = 0;
        var sleeps = 0;

        var wait = PlcOnlinePolicy.Wait(() => states.Dequeue(), PlcOnlinePolicy.States.Online, 30000, () => clock, ms => { sleeps++; clock += ms; });

        Assert.True(wait.GoalReached);
        Assert.False(wait.TimedOut);
        Assert.Equal("Online", wait.FinalState);
        Assert.Equal(new[] { "Offline", "Connecting", "Online" }, wait.Observed);
        Assert.Equal(3, sleeps);
        Assert.Equal(3 * PlcOnlinePolicy.PollIntervalMilliseconds, wait.ElapsedMilliseconds);
    }

    [Theory]
    [InlineData("NotReachable")]
    [InlineData("Protected")]
    [InlineData("Incompatible")]
    public void Wait_StopsAtATerminalStateWithoutSuccess(string terminal)
    {
        var states = new Queue<string>(new[] { "Connecting", terminal, "Online" });
        long clock = 0;

        var wait = PlcOnlinePolicy.Wait(() => states.Dequeue(), PlcOnlinePolicy.States.Online, 30000, () => clock, ms => clock += ms);

        Assert.False(wait.GoalReached);
        Assert.False(wait.TimedOut);
        Assert.Equal(terminal, wait.FinalState);
    }

    [Fact]
    public void Wait_TimesOutWhileStillConnecting()
    {
        long clock = 0;

        var wait = PlcOnlinePolicy.Wait(() => "Connecting", PlcOnlinePolicy.States.Online, 1000, () => clock, ms => clock += ms);

        Assert.True(wait.TimedOut);
        Assert.False(wait.GoalReached);
        Assert.Equal("Connecting", wait.FinalState);
        Assert.Equal(new[] { "Connecting" }, wait.Observed);
        Assert.True(wait.ElapsedMilliseconds >= 1000);
    }

    [Fact]
    public void Wait_ForOfflineIgnoresOnlineTerminalStates()
    {
        var states = new Queue<string>(new[] { "Disconnecting", "NotReachable", "Offline" });
        long clock = 0;

        var wait = PlcOnlinePolicy.Wait(() => states.Dequeue(), PlcOnlinePolicy.States.Offline, 30000, () => clock, ms => clock += ms);

        Assert.True(wait.GoalReached);
        Assert.Equal(new[] { "Disconnecting", "NotReachable", "Offline" }, wait.Observed);
    }

    [Theory]
    [InlineData(null, null, 16, 300, true, null)]
    [InlineData("plc", null, 16, 300, true, "takes no compareTarget")]
    [InlineData("plc", "PLC_2", 16, 300, false, null)]
    [InlineData("plc", null, 16, 300, false, "requires a nonblank targetName")]
    [InlineData("master_copy", "Template", 16, 300, false, null)]
    [InlineData("project_library", null, 16, 300, false, null)]
    [InlineData("project_library", "Lib", 16, 300, false, "takes no targetName")]
    [InlineData("global_library", null, 16, 300, false, "compareTarget must be one of")]
    [InlineData(null, null, 0, 300, true, "maxDepth")]
    [InlineData(null, null, 16, 5001, true, "maxElements")]
    public void ValidateCompare(string? target, string? name, int depth, int elements, bool online, string? expected)
    {
        var error = PlcOnlinePolicy.ValidateCompare(target, name, depth, elements, online);
        if (expected is null) Assert.Null(error);
        else Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("192.168.0.1", true)]
    [InlineData("2", true)]
    [InlineData("", false)]
    [InlineData(" 192.168.0.1", false)]
    [InlineData("192.168.0.1\n", false)]
    public void ValidateAddress(string address, bool valid)
        => Assert.Equal(valid, PlcOnlinePolicy.ValidateAddress(address) is null);

    [Fact]
    public void ValidateTimeoutAndPath()
    {
        Assert.Null(PlcOnlinePolicy.ValidateTimeout(1));
        Assert.Null(PlcOnlinePolicy.ValidateTimeout(PlcOnlinePolicy.MaxTimeoutSeconds));
        Assert.NotNull(PlcOnlinePolicy.ValidateTimeout(0));
        Assert.NotNull(PlcOnlinePolicy.ValidateTimeout(PlcOnlinePolicy.MaxTimeoutSeconds + 1));
        Assert.Null(PlcOnlinePolicy.ValidatePath("PN/IE", "PLCSIM", 1, "1 X1", requireTarget: true));
        Assert.Contains("targetInterface", PlcOnlinePolicy.ValidatePath("PN/IE", "PLCSIM", 1, null, requireTarget: true));
        Assert.Contains("number", PlcOnlinePolicy.ValidatePath("PN/IE", "PLCSIM", -1, null, requireTarget: false));
        Assert.Contains("mode", PlcOnlinePolicy.ValidatePath(" ", "PLCSIM", 1, null, requireTarget: false));
    }
}
