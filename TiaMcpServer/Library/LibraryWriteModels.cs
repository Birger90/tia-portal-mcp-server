using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Library;

/// <summary>What one library_write item does, resolved by the worker's non-mutating plan.</summary>
public sealed record LibraryWriteEffect(string Operation, string? Library, string Target, bool AlreadyOpen, IReadOnlyList<string> Consequences);

public sealed record LibraryWriteEffectPresentation(string OperationId, LibraryWriteEffect? Effect, StructuredOperationOmission? Omission);

public sealed record LibraryGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<LibraryWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission);
