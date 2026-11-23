using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>Owner decision 2026-11-09: a read that lost the binding re-binds to the same open project and runs once more; nothing else does.</summary>
public sealed class ReadReattachPolicyTests
{
    private const string Path = @"C:\Projects\Line\Line.ap21";

    private static ProjectBindingSnapshot Snapshot(string state, string? path = Path, long revision = 1)
        => new(state, "b", revision, path, "w", 1, 42, state == ProjectBindingSnapshot.InvalidatedState ? "lost" : null);

    private static WorkerCallResult Lost(WorkerDispatchState dispatch = WorkerDispatchState.Sent)
        => WorkerCallResult.Fail(WorkerFailureCategories.PortalLost, "The Portal connection was lost.") with { DispatchState = dispatch };

    private static readonly ProjectBindingSnapshot Verified = Snapshot(ProjectBindingSnapshot.VerifiedState);
    private static readonly ProjectBindingSnapshot Invalidated = Snapshot(ProjectBindingSnapshot.InvalidatedState, revision: 2);

    [Theory]
    [InlineData("list_tag_tables")]
    [InlineData("get_block_content")]
    [InlineData("hmi_list_hmi_devices")]
    public void AReadThatLostTheBinding_Reattaches(string method)
        => Assert.True(ReadReattachPolicy.ShouldReattach(method, Verified, Invalidated, Lost(), insideOperation: false));

    [Theory]
    [InlineData("update_block_logic")]
    [InlineData("create_tag")]
    [InlineData("save_project")]
    [InlineData("compile_check")]
    [InlineData("open_project")]
    [InlineData("select_portal_project")]
    [InlineData("plc_download")]
    public void NoWriteLifecycleOnlineOrSelectionCall_IsRepeated(string method)
        => Assert.False(ReadReattachPolicy.ShouldReattach(method, Verified, Invalidated, Lost(), insideOperation: false));

    [Fact]
    public void OnlyWhenTheBindingWasVerifiedBefore_AndIsLostAfter_AndTheReadReachedTheWorker()
    {
        Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Invalidated, Invalidated, Lost(), false));
        Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Verified, Verified, Lost(), false));
        Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Verified, Invalidated, Lost(WorkerDispatchState.NotSent), false));
        Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Verified, Invalidated, WorkerCallResult.Ok("{}"), false));
        Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Snapshot(ProjectBindingSnapshot.VerifiedState, path: null), Invalidated, Lost(), false));
    }

    [Fact]
    public void StatusProbesAndIdentityDivergence_StillNeedAnExplicitBind()
    {
        Assert.False(ReadReattachPolicy.ShouldReattach("get_project_status", Verified, Invalidated, Lost(), false));
        var divergence = WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, "owner replaced") with { DispatchState = WorkerDispatchState.Sent };
        Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Verified, Invalidated, divergence, false));
    }

    [Fact]
    public void InsideAPinnedOrSerializedOperation_NothingIsRepeated()
        => Assert.False(ReadReattachPolicy.ShouldReattach("list_tag_tables", Verified, Invalidated, Lost(), insideOperation: true));

    [Fact]
    public void TheRetryRunsOnlyOnTheSameProject()
    {
        Assert.True(ReadReattachPolicy.SameProject(Verified, Snapshot(ProjectBindingSnapshot.VerifiedState, @"c:\projects\line\LINE.ap21", 3)));
        Assert.False(ReadReattachPolicy.SameProject(Verified, Snapshot(ProjectBindingSnapshot.VerifiedState, @"C:\Projects\Other\Other.ap21", 3)));
        Assert.False(ReadReattachPolicy.SameProject(Verified, Invalidated));
    }
}
