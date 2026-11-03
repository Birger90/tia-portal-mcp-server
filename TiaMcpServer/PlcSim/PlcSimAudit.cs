using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.PlcSim;

/// <summary>
/// Appends one audit record per plcsim_write/plcsim_test call that reaches the tool (dry runs and
/// validation denials included; argument-shape rejections never reach it). It reuses the guarded
/// write pipeline's <see cref="IWriteAuditSink"/> and <see cref="WriteAuditRecord"/> shape, without
/// the project-binding pipeline: <c>recordKind</c> is <c>plcsim</c>, <c>projectPath</c> and
/// <c>binding</c> are null, and each item's <c>target</c> is the instance name.
/// </summary>
public sealed class PlcSimAudit
{
    public const string RecordKind = "plcsim";

    private readonly IWriteAuditSink _sink;
    private readonly TimeProvider _time;

    public PlcSimAudit(IWriteAuditSink sink, TimeProvider? time = null)
    {
        _sink = sink;
        _time = time ?? TimeProvider.System;
    }

    public DateTimeOffset Now => _time.GetUtcNow();

    public long Timestamp => _time.GetTimestamp();

    public long ElapsedMs(long startTimestamp) => (long)Math.Ceiling(_time.GetElapsedTime(startTimestamp).TotalMilliseconds);

    public void Append(string tool, McpAccessMode mode, bool actual, object requested, string phase, string responseText,
        IReadOnlyList<WriteAuditItem> items, DateTimeOffset startedAt, long startTimestamp)
    {
        try
        {
            _sink.Append(new WriteAuditRecord(
                RecordKind,
                WriteAuditRecord.CurrentVersion,
                startedAt,
                tool,
                PlcSimContractVersion.Current,
                WriteAuditRecord.ModeName(mode),
                actual && mode == McpAccessMode.Full ? new WriteAuditConfirmation("policy", "not_requested") : new WriteAuditConfirmation("none", "not_requested"),
                ProjectPath: null,
                Binding: null,
                CanonicalJson.ToElement(requested),
                phase,
                responseText,
                "sha256:" + ContentHashRules.Sha256Hex(responseText),
                Array.Empty<WriteAuditGuard>(),
                items,
                ElapsedMs(startTimestamp)));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TiaMcpServer: failed to audit a '{tool}' call: {exception.Message}");
        }
    }

    public static WriteAuditItem Item(string operationId, string operation, string? instance, string status,
        StructuredOperationFailure? failure, IReadOnlyList<string> warnings, long? durationMs)
        => Item(operationId, operation, instance, status, failure?.Category, failure?.Message, warnings, durationMs);

    public static WriteAuditItem Item(string operationId, string operation, string? instance, string status,
        string? failureCategory, string? failureMessage, IReadOnlyList<string> warnings, long? durationMs)
        => new(operationId, operation, instance, Array.Empty<CheckedPrecondition>(), status, failureCategory, failureMessage, warnings, durationMs);
}
