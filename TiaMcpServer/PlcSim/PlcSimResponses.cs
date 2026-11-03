using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.PlcSim;

/// <summary>Phases of plcsim_write and plcsim_test documents.</summary>
public static class PlcSimPhases
{
    /// <summary>dryRun: validated and planned; the worker was not started and the runtime not touched.</summary>
    public const string Preview = "preview";

    /// <summary>plcsim_write ran its items (all, or up to the first failure).</summary>
    public const string Applied = "applied";

    /// <summary>plcsim_test ran its scenario (completed, stopped at a failure, or aborted).</summary>
    public const string Executed = "executed";

    /// <summary>The call failed before running: validation, access, or a worker-level failure.</summary>
    public const string Error = "error";
}

/// <summary>What one plcsim_write item will do; returned by a dry run.</summary>
public sealed record PlcSimPlannedOperation(
    string OperationId,
    string Operation,
    string WorkerMethod,
    string Instance,
    IReadOnlyList<string> ApiMembers,
    PlcSimRequestInfo Arguments,
    IReadOnlyList<string> Warnings);

/// <summary>
/// plcsim_write document. <see cref="Plan"/> is set for a dry run, <see cref="Batch"/> once items ran,
/// <see cref="Error"/> when the call was rejected (then MCP isError is true).
/// </summary>
public sealed record PlcSimWriteResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    string Phase,
    bool DryRun,
    OperationBatchReadError? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PlcSimPlannedOperation>? Plan,
    StructuredOperationBatch? Batch);

/// <summary>The normalized scenario a plcsim_test dry run would send to the worker.</summary>
public sealed record PlcSimTestPlan(
    string Instance,
    bool ContinueOnFailure,
    int MaxDurationMs,
    int WorkerRequestTimeoutMs,
    IReadOnlyList<PlcSimTestStepInfo> Steps,
    IReadOnlyList<string> ApiMembers);

/// <summary>
/// plcsim_test document. <see cref="Success"/> is true only when the scenario ran and passed;
/// a failing scenario still has MCP isError false (the call ran) with <c>result.passed</c> false.
/// </summary>
public sealed record PlcSimTestResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    string Phase,
    bool DryRun,
    OperationBatchReadError? Error,
    IReadOnlyList<string> Warnings,
    PlcSimTestPlan? Plan,
    PlcSimTestResultInfo? Result);
