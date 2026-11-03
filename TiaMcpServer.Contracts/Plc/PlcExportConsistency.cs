namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// The hint added to an export or read that failed for an object TIA Portal reports as inconsistent (K20, live V21
/// 2026-11-02 and 2026-11-01 D1/D2: "Inconsistent blocks and PLC data types (UDT) cannot be exported" after a Retain change,
/// a renumber, a technology object parameter change, rename or import). The server never compiles on its own: the hint
/// names compile_check. The worker adds it only from typed evidence, never from the Siemens message (which follows the
/// Portal UI language): the export threw and the object's documented <c>PlcBlock.IsConsistent</c> reads false. The host
/// recognises the hint by <see cref="Marker"/>, which is this server's own text. Siemens-free; shared by worker,
/// FakeWorker and host.
/// </summary>
public static class PlcExportConsistency
{
    /// <summary>The fixed start of every hint; the host's only test for it.</summary>
    public const string Marker = "TIA Portal exports only consistent objects:";

    /// <summary>A block (data blocks included) that reads IsConsistent=false.</summary>
    public static string BlockHint(string blockPath)
        => $"{Marker} '{blockPath}' reads IsConsistent=false (changed since its last compile). Run compile_check with blockPath '{blockPath}' and retry; "
            + "this server never compiles on its own.";

    /// <summary>A technology object (an instance DB that compile_check's blockPath does not address) that reads IsConsistent=false.</summary>
    public static string TechnologyObjectHint(string plcName, string objectName)
        => $"{Marker} technology object '{objectName}' reads IsConsistent=false (changed since its last compile). Run compile_check with plcName '{plcName}' and retry; "
            + "this server never compiles on its own.";

    /// <summary>True when <paramref name="message"/> carries a hint of this class.</summary>
    public static bool Carries(string? message) => message is not null && message.IndexOf(Marker, StringComparison.Ordinal) >= 0;

    /// <summary>The hint part of <paramref name="message"/> (from the marker on), or null.</summary>
    public static string? HintOf(string? message)
    {
        if (message is null) return null;
        var start = message.IndexOf(Marker, StringComparison.Ordinal);
        return start < 0 ? null : message.Substring(start);
    }
}
