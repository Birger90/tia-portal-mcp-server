using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcOnline;

/// <summary>
/// plc_tia_online (go_online / go_offline) as a single-item guarded write. Planning reads only the offline
/// <c>get_online_state</c> listing; the mutation applies exactly the planned path. Like plc_download,
/// every actual read-write call is confirmed by the user (<see cref="ConfirmsEveryCall"/>), full runs
/// under policy, and a dry run stops at <c>preview</c> without touching the target. Going online changes
/// TIA Portal's connection, never the PLC.
/// </summary>
public sealed class PlcOnlineConnectionWriteDomain
    : IWriteDomain<PlcOnlineConnectionItem, PlcOnlineConnectionEffects, object, PlcOnlineConnectionResponse>
{
    public const string Contract = "1.0";

    private readonly Func<string?, Task<WorkerCallResult>> _listState;
    private readonly Func<string, PlcOnlineRequestInfo, Task<WorkerCallResult>> _act;
    private PlcOnlineConnectionEffects? _planned;
    private StandaloneToolOutcome<PlcOnlineActionResultInfo>? _outcome;

    public PlcOnlineConnectionWriteDomain(OpennessWorkerClient workerClient)
        : this(workerClient.GetOnlineStateAsync, workerClient.PlcOnlineActionAsync)
    {
    }

    /// <summary>Worker-independent seam for unit tests of planning, guards and projection.</summary>
    internal PlcOnlineConnectionWriteDomain(Func<string?, Task<WorkerCallResult>> listState, Func<string, PlcOnlineRequestInfo, Task<WorkerCallResult>> act)
    {
        _listState = listState ?? throw new ArgumentNullException(nameof(listState));
        _act = act ?? throw new ArgumentNullException(nameof(act));
    }

    public string ToolName => PlcOnlineMethods.OnlineTool;

    public string ContractVersion => Contract;

    public bool ConfirmsEveryCall => true;

    public string DescribeForConfirmation(IReadOnlyList<PlcOnlineConnectionItem> items, IReadOnlyList<ItemPlan<PlcOnlineConnectionEffects>> plans)
    {
        // Names come from the project or the caller: quoted and escaped (password keys are identifiers).
        static string Q(string? value) => ConfirmationText.Quote(value);
        var effect = plans.Single().Effect!;
        if (effect.Action == PlcOnlineConnectionActions.GoOffline)
            return "Confirm plc_tia_online go_offline: close TIA Portal's online connection, including one opened in the TIA Portal UI. "
                + $"Nothing is sent to the PLC's program or configuration. PLC {Q(effect.PlcName)} (device {Q(effect.DeviceName)}), currently {Q(effect.StateBefore)}. "
                + PortalConfirmationSentence(effect.PortalConfirmations);
        var path = effect.Path!;
        return "Confirm plc_tia_online go_online: connect TIA Portal"
            + (effect.SimulationInterface == true ? " over the PLCSIM simulation interface" : " over a hardware interface, not PLCSIM")
            + $" and wait up to {effect.TimeoutSeconds} s for the connection. Nothing is downloaded to the PLC (the effect on the device is not live-verified). "
            + $"PLC {Q(effect.PlcName)} (device {Q(effect.DeviceName)}) over mode {Q(path.Mode)}, "
            + $"PC interface {Q(path.PcInterface.Name)} #{path.PcInterface.Number}, target interface {Q(path.TargetInterface)}. "
            + (effect.PasswordKeys.Count == 0
                ? "Any password prompt aborts the connection."
                : $"Password prompts are answered only for {ConfirmationText.Line(string.Join(", ", effect.PasswordKeys))} from the named environment variables.")
            + TiaMcpServer.PlcDownload.PlcDownloadWriteDomain.TrustSentence(effect.TrustedCertificates, "connection")
            + " Any other prompt aborts. "
            + PortalConfirmationSentence(effect.PortalConfirmations);
    }

    /// <summary>The prompt sentence for the call's <c>portalConfirmations</c> policy, worded like plc_download's.</summary>
    internal static string PortalConfirmationSentence(string policy)
        => policy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? "TIA Portal confirmation dialogs raised meanwhile are answered Yes (portalConfirmations: accept) and recorded."
            : "TIA Portal confirmation dialogs raised meanwhile are answered No, recorded and fail the call.";

    /// <summary>
    /// plc_download's rule for TIA Portal confirmations: declined ones fail the call (the evidence is kept),
    /// accepted ones are evidence and a warning. Null when none was answered or the call already failed.
    /// </summary>
    internal static StructuredOperationFailure? DeclinedConfirmations(string action, string policy, int observed, int listed, bool complete)
        => observed == 0 || policy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? null
            : new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal raised {observed} confirmation dialog(s) during {action} and this tool answered each one No "
                + $"(see result.value.portalConfirmations{(complete ? string.Empty : $"; only the first {listed} are listed")}). "
                + "Inspect the state before retrying; to answer such dialogs Yes, retry with portalConfirmations: \"accept\".");

    internal static string? AcceptedConfirmationsWarning(string action, string policy, int observed)
        => observed > 0 && policy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? $"TIA Portal raised {observed} confirmation dialog(s) during {action} and each was answered Yes (portalConfirmations: accept); "
                + "see result.value.portalConfirmations."
            : null;

    public WriteValidation Validate(IReadOnlyList<PlcOnlineConnectionItem> items, McpAccessMode accessMode)
    {
        if (items.Count != 1 || items[0] is null || PlcOnlineConnectionActions.WorkerMethod(items[0].Action) is not { } method || items[0].Operation != method)
            return items.Count == 1 && items[0] is not null && PlcOnlineConnectionActions.WorkerMethod(items[0].Action) is null
                ? Invalid($"action must be one of: {string.Join(", ", PlcOnlineConnectionActions.All)}.")
                : Invalid("Exactly one host-supplied plc_tia_online operation is required.");
        if (!OperationPolicyCatalog.IsAllowed(accessMode, method))
            return WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "plc_tia_online requires read-write or full access mode.");
        var item = items[0];
        if (item.PlcName is not null && Bad(item.PlcName)) return Invalid("plcName must be nonblank and at most 256 characters when supplied.");
        if (PlcOnlinePolicy.ValidateTimeout(item.TimeoutSeconds) is { } timeout) return Invalid(timeout);
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(item.PortalConfirmations))
            return Invalid($"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        if (item.Action == PlcOnlineConnectionActions.GoOffline)
        {
            if (item.PcInterface is not null || item.TargetInterface is not null || item.Passwords.Count > 0 || item.Mode is not null
                || item.TrustedCertificates is not null)
                return Invalid("go_offline takes only plcName, timeoutSeconds, portalConfirmations and dryRun; mode, pcInterface, targetInterface, passwords "
                    + "and trustedCertificates apply to go_online.");
            return WriteValidation.Valid();
        }

        if (item.PcInterface is null)
            return Invalid("go_online requires pcInterface { name, number } (see plc_read get_online_state); no interface is chosen by default.");
        if (PlcOnlinePolicy.ValidatePath(item.Mode ?? PlcOnlinePolicy.DefaultMode, item.PcInterface.Name, item.PcInterface.Number, null, requireTarget: false) is { } path)
            return Invalid(path);
        if (item.TargetInterface is not null && Bad(item.TargetInterface))
            return Invalid("targetInterface must be nonblank and at most 256 characters when supplied.");
        if (PlcTlsTrustPolicy.NormalizeList(item.TrustedCertificates, out _) is { } trusted) return Invalid(trusted);
        return UnsetPassword(item.Passwords) ?? (PlcCallbackPolicy.ValidateEntries(null, item.Passwords) is { } entries ? Invalid(entries) : WriteValidation.Valid());
    }

    public async Task<WritePlan<PlcOnlineConnectionEffects>> PlanAsync(IReadOnlyList<PlcOnlineConnectionItem> items)
    {
        var item = items.Single();
        var listed = await _listState(item.PlcName).ConfigureAwait(false);
        if (!listed.Success)
            return WritePlan<PlcOnlineConnectionEffects>.Fail(listed.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                listed.Error ?? "The online state could not be read.");
        PlcOnlineStateInventoryInfo inventory;
        try
        {
            inventory = PlcOnlinePayloadContract.DecodeOnlineState(listed.Payload);
        }
        catch (JsonException)
        {
            return WritePlan<PlcOnlineConnectionEffects>.Fail(WorkerFailureCategories.ProtocolError, PlcOnlinePayloadContract.ProtocolFailureMessage);
        }

        var resolution = PlcOnlinePathResolver.ResolveOnline(inventory, item);
        if (!resolution.Success) return WritePlan<PlcOnlineConnectionEffects>.Fail(resolution.Error!.Category, resolution.Error.Message);
        _planned = resolution.Effects!;
        var evidence = _planned.Path is { } path
            ? $"{path.Mode} / {path.PcInterface.Name} #{path.PcInterface.Number} / {path.TargetInterface}"
            : $"state {_planned.StateBefore}";
        return WritePlan<PlcOnlineConnectionEffects>.Ok(new[]
        {
            ItemPlan<PlcOnlineConnectionEffects>.Resolved(_planned, new[]
            {
                new CheckedPrecondition("online_path_offline", evidence, "resolved from get_online_state", Satisfied: true),
            }),
        });
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<PlcOnlineConnectionItem> items, IReadOnlyList<ItemPlan<PlcOnlineConnectionEffects>> plans)
    {
        var effect = plans.Single().Effect!;
        var operationId = items.Single().OperationId;
        if (effect.Action == PlcOnlineConnectionActions.GoOffline)
            return new[] { new FiredGuard(PlcOnlineGuardDefinitions.Disconnects, operationId,
                $"go_offline closes TIA Portal's online connection to PLC '{effect.PlcName}' (currently '{effect.StateBefore}'), including one opened in the TIA Portal UI.") };
        return effect.SimulationInterface == true
            ? Array.Empty<FiredGuard>()
            : new[] { new FiredGuard(PlcOnlineGuardDefinitions.HardwareTarget, operationId,
                $"PC interface '{effect.Path!.PcInterface.Name}' #{effect.Path.PcInterface.Number} is not the PLCSIM simulation interface: "
                + $"TIA Portal connects to the device behind it. Going online downloads nothing to PLC '{effect.PlcName}'.") };
    }

    public async Task<ItemReplan<PlcOnlineConnectionEffects>> ReplanAsync(PlcOnlineConnectionItem item)
    {
        var plan = await PlanAsync(new[] { item }).ConfigureAwait(false);
        return plan.Success ? ItemReplan<PlcOnlineConnectionEffects>.Ok(plan.Items.Single())
            : ItemReplan<PlcOnlineConnectionEffects>.Fail(plan.Error!.Category, plan.Error.Message);
    }

    public Task<WorkerCallResult> MutateAsync(PlcOnlineConnectionItem item)
    {
        var planned = _planned ?? throw new InvalidOperationException("plc_tia_online must be planned before it runs.");
        return _act(item.Operation, new PlcOnlineRequestInfo
        {
            PlcName = planned.PlcName,
            DeviceName = planned.DeviceName,
            Mode = planned.Path?.Mode,
            PcInterfaceName = planned.Path?.PcInterface.Name,
            PcInterfaceNumber = planned.Path?.PcInterface.Number ?? 0,
            TargetInterface = planned.Path?.TargetInterface,
            TimeoutSeconds = planned.TimeoutSeconds,
            Passwords = item.Passwords.ToList(),
            PortalConfirmations = planned.PortalConfirmations,
            TrustedCertificates = planned.TrustedCertificates.ToList(),
        });
    }

    public StructuredOperationItem Project(PlcOnlineConnectionItem item, WorkerCallResult result)
    {
        _outcome = ProjectOutcome(result, _planned ?? throw new InvalidOperationException("plc_tia_online must be planned before it is projected."), item.Passwords);
        return new StructuredOperationItem(item.OperationId, item.Operation, _outcome.Status,
            _outcome.Value is null ? null : CanonicalJson.ToElement(_outcome.Value), _outcome.Failure, Omission: null, SkipReason: null, result.Warnings);
    }

    /// <summary>The typed outcome of one worker result; failures keep any decoded evidence.</summary>
    internal static StandaloneToolOutcome<PlcOnlineActionResultInfo> ProjectOutcome(WorkerCallResult result, PlcOnlineConnectionEffects planned,
        IReadOnlyList<PlcCallbackPasswordInfo> passwords)
    {
        PlcOnlineActionResultInfo Decode(string payload)
            => PlcOnlinePayloadContract.DecodeOnlineAction(payload, planned.Action, planned.PlcName, planned.DeviceName, planned.Path, passwords,
                planned.PortalConfirmations, planned.TrustedCertificates);

        if (!result.Success)
        {
            var category = result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
            var failure = new StructuredOperationFailure(category, result.Error ?? "The plc_tia_online worker operation failed.", EngineeringError: result.EngineeringError);
            if (result.PlcDownloadOutcome is null) return new(OperationBatchStatus.Failed, null, failure, null);
            try
            {
                return new(OperationBatchStatus.Failed, Decode(result.PlcDownloadOutcome), failure, null);
            }
            catch (JsonException)
            {
                return new(OperationBatchStatus.Failed, null, new StructuredOperationFailure(WorkerFailureCategories.ProtocolError,
                    $"The worker reported '{category}' with online evidence that did not match the declared contract; the evidence was rejected.",
                    EngineeringError: result.EngineeringError), null);
            }
        }

        PlcOnlineActionResultInfo value;
        try
        {
            value = Decode(result.Payload);
        }
        catch (JsonException)
        {
            return new(OperationBatchStatus.Failed, null,
                new StructuredOperationFailure(WorkerFailureCategories.ProtocolError, PlcOnlinePayloadContract.ProtocolFailureMessage), null);
        }

        if (value.Abort is { } abort)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                PlcCallbackRecorder.AbortMessage(abort)), null);
        if (DeclinedConfirmations(planned.Action, value.PortalConfirmationPolicy, value.PortalConfirmationsObserved,
                value.PortalConfirmations.Count, value.PortalConfirmationsComplete) is { } declined)
            return new(OperationBatchStatus.Failed, value, declined, null);
        if (!value.GoalReached)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"{planned.Action} did not reach '{(planned.Action == PlcOnlineConnectionActions.GoOnline ? PlcOnlinePolicy.States.Online : PlcOnlinePolicy.States.Offline)}': "
                + $"the state is '{value.StateAfter}'{(value.TimedOut ? $" after the {value.TimeoutSeconds} s timeout" : string.Empty)}"
                + (value.Note is null ? "." : $". {value.Note}")), null);
        return new(OperationBatchStatus.Succeeded, value, null, null);
    }

    public Task<object?> VerifyAsync(StructuredOperationBatch batch) => Task.FromResult<object?>(null);

    public PlcOnlineConnectionResponse Compose(WriteReport<PlcOnlineConnectionEffects, object> report)
    {
        var warnings = report.Warnings
            .Concat(report.Batch?.Operations.SelectMany(operation => operation.Warnings) ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToList();
        var outcome = report.Phase == WritePhases.Applied ? _outcome : null;
        if (outcome?.Value is { Note: { } note }) warnings.Add(note);
        if (outcome?.Value is { } value
            && AcceptedConfirmationsWarning(value.Action, value.PortalConfirmationPolicy, value.PortalConfirmationsObserved) is { } accepted)
            warnings.Add(accepted);
        var response = new PlcOnlineConnectionResponse(ToolName, ContractVersion, report.Success, report.Error, warnings,
            report.Phase, report.Guards, report.Effects.FirstOrDefault()?.Effect, outcome);
        return PlcOnlineConnectionResponseBudget.Fit(response, r => r.Result, (r, o) => r with { Result = o }, r => r.Warnings.ToList(),
            (r, w) => r with { Warnings = w }, "plc_tia_online", "Read plc_read get_online_state; do not repeat the action to recover its result.");
    }

    /// <summary>A password entry whose variable is unset was redacted while binding; refuse it without repeating any name.</summary>
    internal static WriteValidation? UnsetPassword(IReadOnlyList<PlcCallbackPasswordInfo> passwords)
        => passwords.FirstOrDefault(p => p.Variable == TiaMcpServer.PlcDownload.PlcDownloadPasswordMap.UnsetVariable) is { } unset
            ? Invalid(TiaMcpServer.PlcDownload.PlcDownloadWriteDomain.UnsetPasswordVariable(unset.Type))
            : null;

    private static bool Bad(string? value) => string.IsNullOrWhiteSpace(value) || value!.Length > PlcOnlinePolicy.MaxNameLength;

    private static WriteValidation Invalid(string message) => WriteValidation.Invalid(WorkerFailureCategories.ValidationError, message);
}

/// <summary>Bounds the plc_tia_online and plc_station_upload documents: a whole result value is omitted with guidance, then warnings shed.</summary>
internal static class PlcOnlineConnectionResponseBudget
{
    public static TResponse Fit<TResponse, TValue>(TResponse response, Func<TResponse, StandaloneToolOutcome<TValue>?> outcome,
        Func<TResponse, StandaloneToolOutcome<TValue>?, TResponse> withOutcome, Func<TResponse, List<string>> warnings,
        Func<TResponse, IReadOnlyList<string>, TResponse> withWarnings, string tool, string guidance) where TValue : class
    {
        int Length(TResponse r) => CanonicalJson.Serialize(r).Length;
        if (outcome(response) is { Value: { } value } current)
        {
            var size = CanonicalJson.Serialize(value).Length;
            var reason = size > StructuredOperationBatchPayloadBudget.MaxItemChars ? StructuredOperationBatchPayloadBudget.ItemLimitReason
                : Length(response) > StructuredOperationBatchPayloadBudget.MaxDocumentChars ? StructuredOperationBatchPayloadBudget.DocumentLimitReason : null;
            if (reason is not null)
            {
                var limit = reason == StructuredOperationBatchPayloadBudget.ItemLimitReason
                    ? StructuredOperationBatchPayloadBudget.MaxItemChars : StructuredOperationBatchPayloadBudget.MaxDocumentChars;
                response = withOutcome(response, current with
                {
                    Status = OperationBatchStatus.Omitted, Value = null,
                    Omission = new StructuredOperationOmission(reason, limit, size, "plc_read", $"The complete {tool} result was omitted. {guidance}"),
                });
            }
        }

        var list = warnings(response);
        var omitted = 0;
        while (Length(response) > StructuredOperationBatchPayloadBudget.MaxDocumentChars && list.Count > 0)
        {
            list.RemoveAt(list.Select((warning, index) => (warning.Length, index)).MaxBy(entry => entry.Length).index);
            omitted++;
            response = withWarnings(response, list.ToArray());
        }

        if (omitted > 0) response = withWarnings(response, list.Append($"{omitted} warning entries were omitted to fit the {tool} response budget.").ToArray());
        return response;
    }
}
