namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// The path of a projected compiler message (<see cref="CompileMessageInfo.Path"/>): the non-blank TIA Portal
/// <c>Path</c> values of the message's container nodes and its own, outermost first, joined with
/// <see cref="Separator"/>. Live 2026-11-02 (hardware compile of a fail-safe CPU) TIA Portal reported the
/// containers as rows with an empty or blank description and a single object name as path (<c>PLC_F1</c>,
/// <c>Hardware configuration</c>, <c>Profilschiene_0</c>) and the real messages with an empty path; the chain
/// gives the real message <c>PLC_F1/Hardware configuration/Profilschiene_0</c>. Segments are TIA Portal's
/// values, not escaped: the chain is display context, not a selector.
/// </summary>
public static class CompileMessagePaths
{
    public const string Separator = "/";

    /// <summary>
    /// <paramref name="context"/> extended by <paramref name="own"/>. A blank <paramref name="own"/> adds nothing, nor
    /// does one equal to the context's last segment. An <paramref name="own"/> that equals the context or begins with
    /// the context's first segment and <see cref="Separator"/> is already qualified (offline test doubles report such
    /// paths; not observed live) and replaces the chain.
    /// </summary>
    public static string Extend(string context, string? own)
    {
        if (string.IsNullOrWhiteSpace(own)) return context;
        var segment = own!;
        if (context.Length == 0 || string.Equals(segment, context, StringComparison.Ordinal)) return segment;
        var first = context.IndexOf(Separator, StringComparison.Ordinal);
        var firstSegment = first < 0 ? context : context.Substring(0, first);
        if (segment.StartsWith(firstSegment + Separator, StringComparison.Ordinal)) return segment;
        var last = context.LastIndexOf(Separator, StringComparison.Ordinal);
        var lastSegment = last < 0 ? context : context.Substring(last + Separator.Length);
        return string.Equals(lastSegment, segment, StringComparison.Ordinal) ? context : context + Separator + segment;
    }
}
