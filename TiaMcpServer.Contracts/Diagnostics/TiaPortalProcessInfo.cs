namespace TiaMcpServer.Contracts.Diagnostics;

public sealed class TiaPortalProcessInfo
{
    public int ProcessId { get; set; }

    public string? ProjectPath { get; set; }

    public bool HasUserInterface { get; set; }

    public bool AttachedByThisWorker { get; set; }

    /// <summary>
    /// Openness client sessions attached to the process other than this worker
    /// (<c>TiaPortalProcess.AttachedSessions</c>, read without attaching); null when unreadable.
    /// </summary>
    public int? OtherClientCount { get; set; }
}
