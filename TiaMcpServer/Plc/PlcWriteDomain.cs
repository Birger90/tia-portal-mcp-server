using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The plc_write domain for the guarded pipeline; one instance per call. The planner's guards are
/// cached by operationId so <see cref="EvaluateGuards"/> stays pure, and a late re-plan refreshes
/// its item's entry. Block and info guards ask nothing; only fail-safe items fire the acknowledge guard
/// <see cref="PlcGuardDefinitions.SafetyAcceptance"/>, so read-write asks the human for exactly those calls
/// (form elicitation) and full satisfies it by policy.
/// </summary>
public sealed class PlcWriteDomain(OpennessWorkerClient client)
    : IWriteDomain<PlcOperationRequest, PlcWriteEffect, PlcWriteVerification, PlcGuardedWriteResponse>
{
    private readonly PlcWritePlanner _planner = new(client);
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlcWriteEffect> _effects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    private IReadOnlyList<PlcOperationRequest> _items = Array.Empty<PlcOperationRequest>();
    private IReadOnlyList<PlcOperationRequest?> _validated = Array.Empty<PlcOperationRequest?>();

    public string ToolName => "plc_write";

    public string ContractVersion => PlcWriteContractVersion.Current;

    /// <summary>
    /// True exactly when the call contains a fail-safe write (known from the validated items) or planning found an F-block
    /// change (its items fired <see cref="PlcGuardDefinitions.SafetyAcceptance"/>), like library_write's lifecycle items:
    /// such a call confirms once in read-write, and the pipeline refuses unsafe operationIds before any prompt.
    /// </summary>
    public bool ConfirmsEveryCall
        => _validated.Any(item => Contracts.Plc.Safety.PlcSafetyMethods.IsWrite(item?.Operation) || Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsOnline(item?.Operation))
            || _guards.Values.Any(guards => guards.Any(guard => IsConfirmed(guard.Id)));

    /// <summary>
    /// The acknowledge guards a confirming call presents: fail-safe acceptance, the online master secret (issue #16) and the
    /// automatic acceptance of syslog client certificates (security review F8).
    /// </summary>
    private static bool IsConfirmed(string id) => id is PlcGuardDefinitions.SafetyAcceptance or PlcGuardDefinitions.MasterSecretOnline or PlcGuardDefinitions.SyslogAutoAcceptsCertificates;

    /// <summary>
    /// The read-write prompt of a call with fail-safe items (the pipeline shows the acknowledge guard lines, which carry the
    /// acceptance notice, before it): one line per fail-safe item with its operation, PLC and changes, every caller value
    /// quoted and escaped, then how many other items the call runs.
    /// </summary>
    public string DescribeForConfirmation(IReadOnlyList<PlcOperationRequest> items, IReadOnlyList<ItemPlan<PlcWriteEffect>> plans)
    {
        static string Q(string? value) => ConfirmationText.Quote(value);
        var safety = items.Select((item, index) => (item, effect: index < plans.Count ? plans[index]?.Effect : null))
            .Where(pair => (_guards.GetValueOrDefault(pair.item.OperationId) ?? Array.Empty<FiredGuard>()).Any(g => IsConfirmed(g.Id)))
            .Select((pair, number) => $"{number + 1}. {pair.item.Operation} on PLC {Q(pair.effect?.Target.PlcName ?? pair.item.PlcName)}"
                + (pair.effect is { Changes.Count: > 0 } effect ? ": " + string.Join(", ", effect.Changes.Select(c => $"{c.Field} {Q(c.Current)} -> {Q(c.Requested)}")) : string.Empty)
                + (pair.item.BlockPath is { } block ? $"; block {Q(block)}" : string.Empty)
                + $"; operationId {Q(pair.item.OperationId)}")
            .ToList();
        var others = items.Count - safety.Count;
        var online = items.Any(item => Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsOnline(item.Operation));
        var fail = items.Any(item => (_guards.GetValueOrDefault(item.OperationId) ?? Array.Empty<FiredGuard>()).Any(g => g.Id == PlcGuardDefinitions.SafetyAcceptance));
        var syslog = items.Any(item => (_guards.GetValueOrDefault(item.OperationId) ?? Array.Empty<FiredGuard>()).Any(g => g.Id == PlcGuardDefinitions.SyslogAutoAcceptsCertificates));
        if (syslog && !online && !fail)
            return "Confirm plc_write: this call makes a CPU accept syslog client certificates automatically (SysLogAutoAcceptClient=true) in the offline project:\n"
                + string.Join("\n", safety)
                + (others == 0 ? string.Empty : $"\nThe call also runs {others} other PLC item(s) in order; they stop at the first failure without rollback.")
                + "\nIt takes effect on the CPU after a download.";
        if (online && !fail)
            return "Confirm plc_write: this call sets or deletes the master secret of the ONLINE PLC (the connected controller, not the offline project):\n"
                + string.Join("\n", safety)
                + (others == 0 ? string.Empty : $"\nThe call also runs {others} other PLC item(s) in order; they stop at the first failure without rollback.")
                + (syslog ? "\nThe call also makes a CPU accept syslog client certificates automatically (SysLogAutoAcceptClient=true)." : string.Empty)
                + "\nThe secret is read by the worker from the named password source; it never appears in the call, the result or the audit.";
        return (online ? "Confirm plc_write: this call changes the safety (F-) program or the safety access protection of an F-CPU, or the activation tests of the Safety Validation Assistant, and the master secret of an ONLINE PLC:\n"
                : "Confirm plc_write: this call changes the safety (F-) program or the safety access protection of an F-CPU, or the activation tests of the Safety Validation Assistant:\n")
            + string.Join("\n", safety)
            + (others == 0 ? string.Empty : $"\nThe call also runs {others} other PLC item(s) in order; they stop at the first failure without rollback.")
            + (syslog ? "\nThe call also makes a CPU accept syslog client certificates automatically (SysLogAutoAcceptClient=true)." : string.Empty)
            + "\nThe worker checks the safety login again right before every F-program change. Confirming does not accept the safety program.";
    }

    public WriteValidation Validate(IReadOnlyList<PlcOperationRequest> items, McpAccessMode accessMode)
    {
        // ConfirmsEveryCall is read from these items; WriteRun refuses unsafe operationIds of a confirming call right after.
        _validated = items.ToArray();
        return WriteValidation.Compose(
            PlcOperationCatalog.ValidateWrite(items),
            () => PlcWritePayloadBudget.MeasureProtectedCore(items),
            () => PlcOperationCatalog.ValidateAccessMode(items, accessMode),
            ToolName);
    }

    public async Task<WritePlan<PlcWriteEffect>> PlanAsync(IReadOnlyList<PlcOperationRequest> items)
    {
        var result = await _planner.PlanAsync(items).ConfigureAwait(false);
        if (!result.Plan.Success) return result.Plan with { Error = WholeCallNotRun(result.Plan.Error, items.Count) };
        _items = items.ToArray();
        for (var i = 0; i < items.Count; i++)
        {
            _guards[items[i].OperationId] = result.Guards.GetValueOrDefault(items[i].OperationId) ?? Array.Empty<FiredGuard>();
            _effects[items[i].OperationId] = result.Plan.Items[i].Effect!;
        }

        return result.Plan;
    }

    /// <summary>
    /// A planning refusal of any item rejects the whole call before its first mutation (the pipeline plans every item
    /// first, like the pre-flight target_not_found of updates and deletes and the no-op refusals of the text edits). In a
    /// call of several items the message says so, because the refusal names one item only (K25, live V21 2026-11-02: a
    /// replace_tag_comments without a match rejected a call whose set_network_text and replace_network_text were valid).
    /// </summary>
    internal static WriteToolError? WholeCallNotRun(WriteToolError? error, int itemCount)
        => error is null || itemCount < 2 ? error
            : error with { Message = error.Message + $" No item of this call was run: planning checks all {itemCount} items before the first one runs, "
                + "and a refusal rejects the whole call. Correct or remove the refused item and send the call again." };

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<PlcOperationRequest> items, IReadOnlyList<ItemPlan<PlcWriteEffect>> plans)
        => items.SelectMany(item => _guards.GetValueOrDefault(item.OperationId) ?? Array.Empty<FiredGuard>()).ToArray();

    public async Task<ItemReplan<PlcWriteEffect>> ReplanAsync(PlcOperationRequest item)
    {
        var result = await _planner.ReplanAsync(item).ConfigureAwait(false);
        if (!result.Replan.Success) return result.Replan;
        // The acceptance guard the planning pass already fired for this item (and the human confirmed in read-write, or full
        // satisfied by policy) is the same consequence and is not evaluated again; an acceptance guard the planning pass did
        // not fire is a late consequence, which the pipeline blocks in read-write.
        var planned = _guards.GetValueOrDefault(item.OperationId) ?? Array.Empty<FiredGuard>();
        _guards[item.OperationId] = planned.Any(g => IsConfirmed(g.Id))
            ? result.Guards.Where(g => !(IsConfirmed(g.Id) && planned.Any(p => p.Id == g.Id))).ToArray()
            : result.Guards;
        // The re-plan reads real state, so it knows no dependency; the presented effect keeps the planned one.
        var plan = result.Replan.Plan!;
        var effect = plan.Effect! with { DependsOn = _effects.GetValueOrDefault(item.OperationId)?.DependsOn };
        _effects[item.OperationId] = effect;
        return ItemReplan<PlcWriteEffect>.Ok(plan with { Effect = effect });
    }

    public Task<WorkerCallResult> MutateAsync(PlcOperationRequest item)
    {
        if (!PlcBlockEditMethods.PlannedEdits.Contains(item.Operation, StringComparer.Ordinal))
        {
            _attempted.Add(item.OperationId);
            return PlcWorkerInvoker.InvokeWriteAsync(client, item);
        }

        // An interface or document edit imports exactly the document it was planned as, against the export hash it was computed from.
        // Without one nothing is sent, so the item is not attempted and gets no verification entry (K55, live V21 2026-11-02:
        // add_interface_member after create_block of the same block in one call failed with state_changed while its
        // verification said success true). The planning reason (TIA Portal exports no SimaticML of a block not compiled since
        // it was created) is the failure; state_changed stays for a plan that is missing for no known reason.
        if (!_planner.TryGetPlanned(item.OperationId, out var planned))
            return Task.FromResult((_planner.UnplannedReason(item.OperationId) is { } reason
                ? WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed,
                    $"{reason} Nothing was imported for this item; earlier items of the call stay applied. Send the edit again once the document can be exported.")
                : WorkerCallResult.Fail(WorkerFailureCategories.StateChanged,
                    $"Operation '{item.OperationId}': no planned document exists for this edit; nothing was imported. Re-read and retry.")) with
            {
                DispatchState = WorkerDispatchState.NotSent,
            });
        _attempted.Add(item.OperationId);
        return planned.IsType
            ? client.UpdateTypeContentAsync(item.TypePath!, planned.Content, format: planned.Format, expectedContentHash: planned.BaseHash)
            : client.UpdateBlockLogicAsync(item.BlockPath!, planned.Content, format: planned.Format, expectedContentHash: planned.BaseHash);
    }

    public StructuredOperationItem Project(PlcOperationRequest item, WorkerCallResult result)
        => PlcPayloadContract.ProjectWrite(item, _planner.Pitfalls.TryGetValue(item.OperationId, out var pitfalls) && pitfalls.Warnings.Count > 0
            ? result with { Warnings = (result.Warnings ?? Array.Empty<string>()).Concat(pitfalls.Warnings).ToArray() }
            : result);

    public async Task<PlcWriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => await PlcVerificationPolicy.VerifyAsync(client, _items, _effects,
            StructuredOperationBatch.FromItems(batch.Operations.Where(o => _attempted.Contains(o.OperationId)).ToArray()),
            _planner.Planned, _planner.Pitfalls, _planner.SafetyLinks).ConfigureAwait(false);

    public bool VerificationSucceeded(PlcWriteVerification? verification) => verification?.Success == true;

    public PlcGuardedWriteResponse Compose(WriteReport<PlcWriteEffect, PlcWriteVerification> report)
        => PlcWritePayloadBudget.Apply(new PlcGuardedWriteResponse(ToolName, ContractVersion, report.Phase, report.Success,
            report.Error, report.Warnings, report.Guards,
            report.Effects.Select(e => new PlcWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));
}
