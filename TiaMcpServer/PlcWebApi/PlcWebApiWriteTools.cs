using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcWebApi;

/// <summary>plc_online_write, registered in read-write and full (OnlineControl) only.</summary>
[McpServerToolType]
public class PlcWebApiWriteTools
{
    [McpServerTool(Name = PlcWebApiWriteDomain.Tool, ReadOnly = false, Destructive = true, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcOnlineWriteResponse))]
    [Description("Change real S7-1500 CPUs (and PLCSIM Advanced instances with the web server enabled) online through the SIMATIC S7-1500 Web API, with up to 50 ordered operations; the first failure stops the call and nothing is rolled back. "
        + "Every operation takes target, the exact name of a PLC configured by the user in the server's --plc-webapi-config file. "
        + "Operations: change_operating_mode (operatingMode run|stop), write_tags (tags [{name, value}], representation simple|raw; stops at the first failed tag), acknowledge_alarm (alarmId), flash_leds (hwid), "
        + "set_system_time (timestamp, RFC 3339 UTC), create_backup (path: absolute local file to write, overwrite), restore_backup (path: absolute local backup file; needs a target with a configured user; the PLC restarts). "
        + "Set dryRun=true to see the resolved targets, effects and guards without contacting any PLC. Read-write asks the user to confirm each actual call once (form elicitation: guards first, then every effect with its values quoted; tag values over 512 characters as length and SHA-256); full runs under policy without prompting. "
        + "Guards (acknowledge): online_changes_real_controller on every call, operating_mode_stop for STOP, restore_backup_replaces_program for a restore. Each call writes one audit record (recordKind plc_online). "
        + "Calls run one at a time, and calls to one PLC (reads included) one after another. Failures: target_not_found, validation_error, state_changed, webapi_unreachable, webapi_authentication_failed, webapi_permission_denied, webapi_rejected (Web API error code and message in the item result).")]
    public static Task<CallToolResult> PlcOnlineWrite(
        PlcWebApiService webApi,
        [Description("Ordered online changes, each with a unique operationId (no control or Unicode format characters: it is shown in the confirmation prompt), an operation and a target.")] PlcOnlineWriteRequest[] operations,
        [Description("Resolve and evaluate guards without contacting any PLC or asking for confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => webApi.Execution.RunAsync(new PlcWebApiWriteDomain(webApi, cancellationToken), new WriteCall<PlcOnlineWriteRequest>(operations, dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            PlcWebApiWriteGate.Strategy, cancellationToken);
}

/// <summary>
/// The guarded pipeline's binding gate for plc_online_write. Online writes are not bound to a TIA
/// project: the gate holds one fixed "not project-bound" snapshot (so a project binding change can
/// never invalidate a confirmation) and its lease runs one plc_online_write call at a time.
/// </summary>
public sealed class PlcWebApiWriteGate : IWriteBindingGate
{
    internal static readonly ProjectBindingSnapshot NotProjectBound = new(ProjectBindingSnapshot.UnboundState, "plc-online", 0,
        projectPath: null, workerSessionId: null, sessionGeneration: null, portalProcessId: null, invalidatedReason: null);

    private readonly PlcWebApiService _service;
    private readonly SemaphoreSlim _lease = new(1, 1);

    public PlcWebApiWriteGate(PlcWebApiService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>Server-selected preparation: pins <see cref="NotProjectBound"/> without any worker call.</summary>
    public static IWriteBindingStrategy<PlcOnlineWriteRequest> Strategy { get; } = new NotProjectBoundStrategy();

    public McpAccessMode AccessMode => _service.AccessMode;

    public ProjectBindingSnapshot CurrentBinding => NotProjectBound;

    public Task<WorkerCallResult> RequireVerifiedWriteBindingAsync() => Task.FromResult(WorkerCallResult.Ok(string.Empty));

    public async Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(ProjectBindingSnapshot binding, Func<Task<T>> operation, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!ReferenceEquals(binding, NotProjectBound))
            return WriteLeaseResult<T>.Fail(new WriteToolError(Contracts.Worker.WorkerFailureCategories.BindingConflict,
                "plc_online_write runs only under its own not-project-bound lease."));
        await _lease.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return WriteLeaseResult<T>.Ok(await operation().ConfigureAwait(false));
        }
        finally
        {
            _lease.Release();
        }
    }

    private sealed class NotProjectBoundStrategy : IWriteBindingStrategy<PlcOnlineWriteRequest>
    {
        public Task<WriteBindingPreparation> PrepareAsync(WriteCall<PlcOnlineWriteRequest> call, CancellationToken cancellationToken = default)
            => Task.FromResult(WriteBindingPreparation.Prepared(NotProjectBound));
    }
}

/// <summary>
/// Writes plc_online_write's pipeline audit records into the shared audit stream with
/// <c>recordKind</c> <c>plc_online</c>; there is no project, so <c>projectPath</c> and <c>binding</c> are null.
/// The record never contains credentials: they are not tool arguments and never enter a response.
/// </summary>
public sealed class PlcWebApiAuditSink : IWriteAuditSink
{
    public const string RecordKind = "plc_online";

    private readonly IWriteAuditSink _inner;

    public PlcWebApiAuditSink(IWriteAuditSink inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Append(WriteAuditRecord record)
        => _inner.Append(record with { RecordKind = RecordKind, ProjectPath = null, Binding = null });
}
