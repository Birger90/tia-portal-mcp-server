using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Safety.Pipeline;

public static class WriteVerificationChecks
{
    public const string Trusted = "trusted";
    public const string Superseded = "superseded";
}

public sealed record WriteOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? Message);

public sealed record WriteVerification(bool Success, IReadOnlyList<WriteOperationVerification> Operations,
    StructuredOperationOmission? Omission);

public interface IWriteVerificationPolicy<TItem> where TItem : IOperationBatchItem
{
    /// <summary>Public read tool named in the "may have changed" message, e.g. "plc_read".</summary>
    string ReadTool { get; }
    /// <summary>Normalized keys the item owns (asLater:false) or offers to later items (asLater:true); newline-joined segments, container prefixes member.</summary>
    IReadOnlyList<string> ObjectKeys(TItem item, bool asLater);
    bool HasReadBack(TItem item);
    Task<WriteOperationVerification> ReadBackAsync(TItem item, StructuredOperationFailure? failure);

    /// <summary>
    /// What a successful later item <paramref name="later"/> that supersedes <paramref name="item"/> says about the
    /// object's final state, when the generic "verified there" does not hold (for example a later rename that is
    /// trusted, not read back). Null keeps the generic text.
    /// </summary>
    string? SupersessionEvidence(TItem item, TItem later) => null;

    /// <summary>
    /// For an item without a verification read-back (<see cref="HasReadBack"/> false): what its worker already checked before
    /// it reported success (K40, live 2026-11-02: plug_device_item reported placedUnder from its own read, create_io_system
    /// read the IO system back, yet the verification said "nothing was read back"). Null keeps the generic trusted text.
    /// </summary>
    string? WorkerCheckEvidence(TItem item) => null;
}
