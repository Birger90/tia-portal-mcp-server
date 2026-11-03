using TiaMcpServer.Contracts.PlcOnline;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>Work a call leaves active (go_online's fail-closed legitimation handler) ends exactly once, at the next request.</summary>
public sealed class DeferredUntilNextRequestTests
{
    [Fact]
    public void CloseAll_RunsEachCloserOnceInOrder_AndIsolatesAThrowingOne()
    {
        var deferred = new DeferredUntilNextRequest();
        var order = new List<int>();
        deferred.Defer(() => { order.Add(1); return new[] { "first" }; });
        deferred.Defer(() => throw new InvalidOperationException("disposed portal"));
        deferred.Defer(() => { order.Add(3); return Array.Empty<string>(); });
        Assert.Equal(3, deferred.Pending);

        var lines = deferred.CloseAll();
        Assert.Equal(new[] { 1, 3 }, order);
        Assert.Equal("first", lines[0]);
        Assert.Contains("InvalidOperationException", lines[1]);
        Assert.Equal(0, deferred.Pending);
        Assert.Empty(deferred.CloseAll());
        Assert.Equal(new[] { 1, 3 }, order);
    }

    [Fact]
    public async Task ConcurrentDeferAndCloseAll_NeverLoseOrRepeatACloser()
    {
        var deferred = new DeferredUntilNextRequest();
        var ran = 0;
        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++) deferred.Defer(() => { Interlocked.Increment(ref ran); return Array.Empty<string>(); });
        })).ToArray();
        var closer = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++) deferred.CloseAll();
        });
        await Task.WhenAll(writers.Append(closer));
        deferred.CloseAll();
        Assert.Equal(1000, ran);
    }
}
