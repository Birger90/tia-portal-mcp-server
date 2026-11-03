using TiaMcpServer.Contracts.PlcOnline;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// compare_to_online's connection handling (allowed in read-only): the connection a compare opened is
/// closed again on the success and the failure path, and the result never claims a disconnect that was
/// not observed.
/// </summary>
public sealed class PlcCompareOnlineRunTests
{
    private sealed class Plc
    {
        private readonly Queue<string> _states;

        public Plc(params string[] states) => _states = new Queue<string>(states);

        public int GoOfflineCalls { get; private set; }

        public Exception? GoOfflineFailure { get; set; }

        public long Clock { get; private set; }

        public string Read() => _states.Count > 1 ? _states.Dequeue() : _states.Peek();

        public void GoOffline()
        {
            GoOfflineCalls++;
            if (GoOfflineFailure is not null) throw GoOfflineFailure;
        }

        public PlcCompareOnlineRun Run(Func<Exception, bool>? isFatal = null)
            => new(Read, GoOffline, ms => Clock += ms, () => Clock, isFatal);
    }

    [Fact]
    public void ConnectionOpenedByASuccessfulCompare_IsClosed()
    {
        var plc = new Plc("Offline", "Online", "Disconnecting", "Offline");
        var run = plc.Run();
        Assert.Equal(7, run.Run(() => 7));

        var result = new PlcCompareResultInfo();
        run.ApplyTo(result);
        Assert.Equal(1, plc.GoOfflineCalls);
        Assert.Equal("Offline", result.OnlineStateBefore);
        Assert.Equal("Offline", result.OnlineStateAfter);
        Assert.True(result.RestoreAttempted);
        Assert.True(result.DisconnectedAfterCompare);
    }

    [Fact]
    public void ConnectionOpenedByAThrowingCompare_IsClosed_AndTheCompareFailureStillPropagates()
    {
        var plc = new Plc("Offline", "Connecting", "Offline");
        var run = plc.Run();

        var thrown = Assert.Throws<InvalidOperationException>(() => run.Run<int>(() => throw new InvalidOperationException("compare failed")));
        Assert.Equal("compare failed", thrown.Message);
        Assert.Equal(1, plc.GoOfflineCalls);
        Assert.True(run.RestoreAttempted);
        Assert.True(run.Disconnected);
        Assert.Contains("closed again", run.RestoreSummary());
    }

    [Fact]
    public void RestoreThatDoesNotReachOffline_IsReportedAsNotDisconnected()
    {
        var plc = new Plc("Offline", "Online");
        var run = plc.Run();
        run.Run(() => 0);

        var result = new PlcCompareResultInfo();
        run.ApplyTo(result);
        Assert.True(result.RestoreAttempted);
        Assert.False(result.DisconnectedAfterCompare);
        Assert.Equal("Online", result.OnlineStateAfter);
        Assert.True(plc.Clock >= PlcCompareOnlineRun.RestoreTimeoutMilliseconds);
        Assert.Contains("may still be online", run.RestoreSummary());
    }

    [Fact]
    public void NonFatalGoOfflineFailure_IsEvidence_FatalOneWins()
    {
        var plc = new Plc("Offline", "Online") { GoOfflineFailure = new InvalidOperationException("offline refused") };
        var run = plc.Run();
        run.Run(() => 0);
        Assert.True(run.RestoreAttempted);
        Assert.False(run.Disconnected);
        Assert.IsType<InvalidOperationException>(run.RestoreFailure);
        Assert.Contains("offline refused", run.RestoreSummary());

        var fatal = new Plc("Offline", "Online") { GoOfflineFailure = new OutOfMemoryException("portal lost") };
        Assert.Throws<OutOfMemoryException>(() => fatal.Run(ex => ex is OutOfMemoryException).Run(() => 0));
    }

    [Fact]
    public void AlreadyOnlineOrStillOfflinePlc_IsLeftAsIs()
    {
        var online = new Plc("Online", "Online");
        var run = online.Run();
        run.Run(() => 0);
        Assert.Equal(0, online.GoOfflineCalls);
        Assert.False(run.RestoreAttempted);
        Assert.Null(run.RestoreSummary());

        var offline = new Plc("Offline", "Offline");
        var stayed = offline.Run();
        stayed.Run(() => 0);
        var result = new PlcCompareResultInfo();
        stayed.ApplyTo(result);
        Assert.Equal(0, offline.GoOfflineCalls);
        Assert.False(result.RestoreAttempted);
        Assert.False(result.DisconnectedAfterCompare);
    }

    [Fact]
    public void WithoutOnlineProvider_TheConnectionMembersAreNull()
    {
        long clock = 0;
        var run = new PlcCompareOnlineRun(null, null, ms => clock += ms, () => clock);
        run.Run(() => 0);
        var result = new PlcCompareResultInfo();
        run.ApplyTo(result);
        Assert.Null(result.OnlineStateBefore);
        Assert.Null(result.DisconnectedAfterCompare);
        Assert.Null(result.RestoreAttempted);
        Assert.Throws<ArgumentException>(() => new PlcCompareOnlineRun(() => "Offline", null, _ => { }, () => 0));
    }
}
