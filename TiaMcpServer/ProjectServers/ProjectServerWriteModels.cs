using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.ProjectServers;

/// <summary>What one project_server_write item does, planned from the bind_project inventory reads (never a mutation).</summary>
public sealed record ProjectServerWriteEffect(string Operation, string Target, IReadOnlyList<string> Consequences);

public sealed record ProjectServerWriteEffectPresentation(string OperationId, ProjectServerWriteEffect? Effect, StructuredOperationOmission? Omission);

public sealed record ProjectServerGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<ProjectServerWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission);
