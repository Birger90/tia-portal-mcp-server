using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>One lifecycle call; operation identity is supplied by the host, never the caller.</summary>
public sealed record LifecycleWriteItem(string Operation) : IOperationBatchItem
{
    public string OperationId => "lifecycle";
    public string? ProjectPath { get; init; }
    public string? ProjectDirectory { get; init; }
    public string? ProjectName { get; init; }
    public string? Author { get; init; }
    public string? Comment { get; init; }
    public bool ForceRebind { get; init; }
    public string? TargetDirectory { get; init; }
    public string? TargetName { get; init; }
    public bool Rebind { get; init; } = true;
    public string? ArchiveDirectory { get; init; }
    public string? ArchiveName { get; init; }
    public string? Mode { get; init; }
    public bool SaveBeforeArchive { get; init; } = true;
    public bool SaveBeforeClose { get; init; } = true;
    /// <summary>retrieve_project: the archive file to retrieve.</summary>
    public string? ArchivePath { get; init; }
    /// <summary>retrieve_project: RetrieveWithUpgrade instead of Retrieve; open_project (audit N5): OpenWithUpgrade instead of Open.</summary>
    public bool Upgrade { get; init; }
    /// <summary>open_project: the UMAC user of a protected .ap21 (ProjectComposition.Open(FileInfo, UmacDelegate)); null opens without login.</summary>
    public string? UmacUserName { get; init; }
    /// <summary>open_project: <c>project</c> (default) or <c>global</c>.</summary>
    public string? UmacUserType { get; init; }
    /// <summary>open_project: the password source token of the UMAC user after the binding-time redaction (never a password).</summary>
    public string? UmacPassword { get; init; }
    /// <summary>open_project: the umacPassword came from the password map (purpose UmacProjectLogin), not the call.</summary>
    public bool UmacPasswordFromMap { get; init; }
}
