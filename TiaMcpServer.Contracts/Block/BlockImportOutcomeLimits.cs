namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// Bounds of the compile report embedded in a <see cref="BlockImportOutcomeInfo"/>, shared by the worker projection
/// (<c>BlockImportOutcomeProjection</c>) and the host validator (<c>BlockImportOutcomeValidator</c>) so the two cannot
/// drift. Within them the projection keeps TIA Portal's own message texts and paths (errors first, then warnings,
/// then information) and omits only what does not fit.
/// </summary>
public static class BlockImportOutcomeLimits
{
    public const int MaxPlcs = 8;
    public const int MaxMessages = 20;
    public const int MaxNotes = 8;
    public const int MaxFieldChars = 256;

    /// <summary>Message descriptions, message paths and diagnostic notes together (1,024 until 2026-11-02, defect K04).</summary>
    public const int MaxDiagnosticChars = 6_000;

    /// <summary>The whole outcome serialized camelCase with nulls (the host's presentation).</summary>
    public const int MaxSerializedChars = 12_000;
}
