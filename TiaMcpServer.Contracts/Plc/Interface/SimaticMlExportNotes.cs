namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>
/// The wording of a block export that carries no SimaticML document (defect D15). TIA Portal does not export an
/// inconsistent block (for example one just created from a SIMATIC SD document and not yet compiled) as SimaticML: reported
/// by BetterTIA (pitfall B4) and confirmed live on TIA Portal V21 on 2026-11-02, where get_block_content format xml of such a
/// block returned only its .s7dcl. The worker used to drop the failed SimaticML export silently; now the read carries a
/// warning and the planning reads that need the SimaticML name the cause. Siemens-free; shared by worker, FakeWorker and host.
/// </summary>
public static class SimaticMlExportNotes
{
    /// <summary>Why a block export carries no SimaticML, and what to do.</summary>
    public const string Cause = "TIA Portal does not export inconsistent blocks as SimaticML (for example a block not compiled since it was created or imported; "
        + "BetterTIA pitfall B4, confirmed live on V21 2026-11-02); run compile_check and read again";

    /// <summary>The planning-read reason of an export without SimaticML.</summary>
    public const string MissingInPlanning = "the export carries no SimaticML document: " + Cause;

    /// <summary>The warning of a block read whose SimaticML export failed (one line).</summary>
    public static string NotExported(string blockPath, string? detail)
        => $"The block '{blockPath}' was not exported as SimaticML"
           + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({OneLine(detail!)})")
           + $": {Cause}. The result holds only the documents TIA Portal did export (the SIMATIC SD documents).";

    private static string OneLine(string text)
    {
        var line = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= 300 ? line : line.Substring(0, 299) + "…";
    }
}
