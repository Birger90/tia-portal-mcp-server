namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// One compiler message. Only messages with a description are reported: TIA Portal's container nodes (empty or
/// blank description) are not messages; their paths are carried in <see cref="Path"/> instead.
/// </summary>
public class CompileMessageInfo
{
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Where the message belongs: the chain of the non-blank TIA Portal paths of its containers and its own, joined with
    /// '/' (<see cref="CompileMessagePaths"/>), e.g. <c>PLC_F1/Hardware configuration/Profilschiene_0</c>. Display
    /// context, not a selector.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;
}
