namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Siemens-free core of <c>compare_to_online</c>'s connection handling. It reads the online state
/// before the compare and, whether the compare returned or threw, restores <c>Offline</c> with
/// GoOffline when the PLC was not online before and is <c>Online</c> or <c>Connecting</c> afterwards
/// (the compare opened that connection; Siemens guidance: go offline only for a connection this
/// application established). The restore is reported as attempted, reached or failed; it never hides
/// the compare's own exception, except that a fatal restore failure (a lost Portal) wins.
/// </summary>
public sealed class PlcCompareOnlineRun
{
    public const int RestoreTimeoutMilliseconds = 10000;

    private readonly Func<string?>? _readState;
    private readonly Action? _goOffline;
    private readonly Action<int> _sleep;
    private readonly Func<long> _elapsedSinceStart;
    private readonly Func<Exception, bool> _isFatal;
    private readonly int _restoreTimeoutMilliseconds;

    /// <param name="readState">Reads <c>OnlineProvider.State</c> by name; null when the PLC has no online provider.</param>
    /// <param name="goOffline">Calls <c>OnlineProvider.GoOffline()</c>; null when the PLC has no online provider.</param>
    /// <param name="elapsedSinceStart">Milliseconds on a clock the run may read repeatedly (a stopwatch started by the caller).</param>
    public PlcCompareOnlineRun(Func<string?>? readState, Action? goOffline, Action<int> sleep, Func<long> elapsedSinceStart,
        Func<Exception, bool>? isFatal = null, int restoreTimeoutMilliseconds = RestoreTimeoutMilliseconds)
    {
        if ((readState is null) != (goOffline is null))
            throw new ArgumentException("readState and goOffline are both supplied or both omitted.");
        _readState = readState;
        _goOffline = goOffline;
        _sleep = sleep ?? throw new ArgumentNullException(nameof(sleep));
        _elapsedSinceStart = elapsedSinceStart ?? throw new ArgumentNullException(nameof(elapsedSinceStart));
        _isFatal = isFatal ?? (_ => false);
        _restoreTimeoutMilliseconds = restoreTimeoutMilliseconds;
    }

    public string? StateBefore { get; private set; }

    /// <summary>The state right after the compare (before any restore).</summary>
    public string? StateAfterCompare { get; private set; }

    /// <summary>The final state: after the restore when one ran, otherwise <see cref="StateAfterCompare"/>.</summary>
    public string? StateAfter { get; private set; }

    public bool RestoreAttempted { get; private set; }

    /// <summary>True only when GoOffline was called and <c>Offline</c> was observed within the restore timeout.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>A non-fatal exception GoOffline or the state read threw during the restore.</summary>
    public Exception? RestoreFailure { get; private set; }

    public T Run<T>(Func<T> compare)
    {
        if (compare is null) throw new ArgumentNullException(nameof(compare));
        StateBefore = _readState?.Invoke();
        try
        {
            return compare();
        }
        finally
        {
            Restore();
        }
    }

    /// <summary>A sentence for a failure message or a warning; null when the PLC needed no restore.</summary>
    public string? RestoreSummary()
    {
        if (!RestoreAttempted) return null;
        if (Disconnected) return $"The connection the compare opened was closed again (state '{StateAfter}').";
        return RestoreFailure is not null
            ? $"The compare left the PLC '{StateAfterCompare}' and closing that connection failed ({RestoreFailure.GetType().Name}: {RestoreFailure.Message}); "
                + "TIA Portal may still be online: go offline with plc_tia_online."
            : $"The compare left the PLC '{StateAfterCompare}' and it was not Offline within {_restoreTimeoutMilliseconds / 1000} s of GoOffline "
                + $"(state '{StateAfter}'); TIA Portal may still be online: go offline with plc_tia_online.";
    }

    public void ApplyTo(PlcCompareResultInfo result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        result.OnlineStateBefore = StateBefore;
        result.OnlineStateAfter = StateAfter;
        result.DisconnectedAfterCompare = _readState is null ? null : Disconnected;
        result.RestoreAttempted = _readState is null ? null : RestoreAttempted;
    }

    private void Restore()
    {
        if (_readState is null) return;
        try
        {
            StateAfterCompare = _readState();
            StateAfter = StateAfterCompare;
            if (StateBefore == PlcOnlinePolicy.States.Online
                || StateAfterCompare is not (PlcOnlinePolicy.States.Online or PlcOnlinePolicy.States.Connecting))
                return;
            RestoreAttempted = true;
            _goOffline!();
            var start = _elapsedSinceStart();
            var wait = PlcOnlinePolicy.Wait(_readState, PlcOnlinePolicy.States.Offline, _restoreTimeoutMilliseconds,
                () => _elapsedSinceStart() - start, _sleep);
            StateAfter = wait.FinalState;
            Disconnected = wait.GoalReached;
        }
        catch (Exception ex) when (!_isFatal(ex))
        {
            RestoreFailure = ex;
        }
    }
}
