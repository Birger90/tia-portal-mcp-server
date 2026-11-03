namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Work a worker call leaves active after it returns, ended when the next worker request starts: for
/// example plc_tia_online go_online keeps a fail-closed online-legitimation handler registered, because
/// TIA Portal may still be connecting after the call's polling stopped. <see cref="CloseAll"/> runs every
/// deferred closer exactly once, in order, outside the lock (a closer may touch Siemens objects), isolates
/// a throwing closer, and returns the warning lines the closers report for that next response.
/// Thread-safe: <see cref="Defer"/> may race with <see cref="CloseAll"/>; a closer is run by exactly one
/// <see cref="CloseAll"/>, never twice and never lost.
/// </summary>
public sealed class DeferredUntilNextRequest
{
    private readonly object _sync = new();
    private List<Func<IReadOnlyList<string>>> _closers = new();

    public int Pending
    {
        get
        {
            lock (_sync) return _closers.Count;
        }
    }

    public void Defer(Func<IReadOnlyList<string>> close)
    {
        if (close is null) throw new ArgumentNullException(nameof(close));
        lock (_sync) _closers.Add(close);
    }

    public IReadOnlyList<string> CloseAll()
    {
        List<Func<IReadOnlyList<string>>> closers;
        lock (_sync)
        {
            closers = _closers;
            _closers = new List<Func<IReadOnlyList<string>>>();
        }

        var lines = new List<string>();
        foreach (var close in closers)
        {
            try
            {
                lines.AddRange(close() ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                lines.Add($"Ending a scope left by the previous call failed ({ex.GetType().Name}); it was dropped.");
            }
        }

        return lines;
    }
}
