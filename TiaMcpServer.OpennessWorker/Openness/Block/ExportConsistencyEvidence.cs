using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// K20: when an export of a block or technology object throws a recoverable Siemens exception, the object's documented
/// <c>PlcBlock.IsConsistent</c> (property or attribute, read as <see cref="OpennessReflection.ReadPropertyOrAttribute"/>)
/// is read; false records a <see cref="PlcExportConsistency"/> hint for the current request. The exception propagates
/// unchanged, so the failure category is decided as before (by type); <see cref="Annotate"/> then appends the hint to the
/// failed response's error, or adds it as a warning when the request still succeeded (an export whose failure was
/// handled). Nothing is compiled. One request runs at a time on the worker's request thread.
/// </summary>
internal static class ExportConsistencyEvidence
{
    [ThreadStatic] private static List<string>? _hints;

    public static void BeginRequest() => _hints = null;

    public static T Observe<T>(object? exported, Func<string> hint, Func<T> export)
    {
        try
        {
            return export();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            Record(exported, hint);
            throw;
        }
    }

    private static void Record(object? exported, Func<string> hint)
    {
        if (exported is null) return;
        string? consistent;
        try
        {
            consistent = OpennessReflection.ReadPropertyOrAttribute(exported, "IsConsistent");
        }
        catch (EngineeringException)
        {
            return;
        }

        if (!bool.TryParse(consistent, out var value) || value) return;
        var text = hint();
        _hints ??= new List<string>();
        if (!_hints.Contains(text, StringComparer.Ordinal)) _hints.Add(text);
    }

    /// <summary>Adds the request's hints to <paramref name="response"/> (once each).</summary>
    public static void Annotate(WorkerResponse response)
    {
        var hints = _hints;
        _hints = null;
        if (hints is null) return;
        foreach (var hint in hints)
        {
            if (!response.Success)
            {
                if (response.Error?.Contains(hint) != true) response.Error = string.IsNullOrEmpty(response.Error) ? hint : response.Error + " " + hint;
                continue;
            }

            response.Warnings ??= new List<string>();
            if (!response.Warnings.Contains(hint)) response.Warnings.Add(hint);
        }
    }
}
