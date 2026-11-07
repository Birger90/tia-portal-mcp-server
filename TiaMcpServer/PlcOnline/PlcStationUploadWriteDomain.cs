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
/// plc_station_upload as a single-item guarded write (<c>ProjectMutation</c>: it adds the uploaded
/// station to the offline project). Planning reads only the offline <c>list_station_upload_interfaces</c>
/// listing. Because it connects to a device, every actual read-write call is confirmed like the other
/// online actions; full runs under policy; <c>dryRun</c> previews without connecting. Callback
/// configurations are answered only by the caller's <c>decisions</c> and <c>passwords</c> (environment
/// variable names); anything else aborts with the available selections. No verification read: inspect
/// the project tree and save explicitly.
/// </summary>
public sealed class PlcStationUploadWriteDomain
    : IWriteDomain<PlcStationUploadWriteItem, PlcStationUploadEffects, object, PlcStationUploadWriteResponse>
{
    public const string Contract = "1.0";

    private const string InspectBeforeRetry =
        "A partly uploaded station may have been added to the project. Inspect the project tree (browse_project_tree) before retrying; "
        + "no rollback or replay was attempted, and nothing was saved.";

    private const string SaveNote = "The uploaded station was added to the offline project, which is not saved; save it with save_project.";

    private readonly Func<Task<WorkerCallResult>> _listInterfaces;
    private readonly Func<PlcOnlineRequestInfo, Task<WorkerCallResult>> _upload;
    private PlcStationUploadEffects? _planned;
    private StandaloneToolOutcome<PlcStationUploadResultInfo>? _outcome;

    public PlcStationUploadWriteDomain(OpennessWorkerClient workerClient)
        : this(workerClient.ListStationUploadInterfacesAsync, workerClient.PlcStationUploadAsync)
    {
    }

    /// <summary>Worker-independent seam for unit tests of planning, guards and projection.</summary>
    internal PlcStationUploadWriteDomain(Func<Task<WorkerCallResult>> listInterfaces, Func<PlcOnlineRequestInfo, Task<WorkerCallResult>> upload)
    {
        _listInterfaces = listInterfaces ?? throw new ArgumentNullException(nameof(listInterfaces));
        _upload = upload ?? throw new ArgumentNullException(nameof(upload));
    }

    public string ToolName => PlcOnlineMethods.StationUpload;

    public string ContractVersion => Contract;

    public bool ConfirmsEveryCall => true;

    public string DescribeForConfirmation(IReadOnlyList<PlcStationUploadWriteItem> items, IReadOnlyList<ItemPlan<PlcStationUploadEffects>> plans)
    {
        // The address and path names are quoted and escaped (decision and password keys are identifiers).
        var effect = plans.Single().Effect!;
        return "Confirm plc_station_upload: upload a device's station (hardware, software and files) and add it as a new "
            + "device to the offline project (not saved). Nothing is sent to the device's program or configuration. "
            + $"Device at {ConfirmationText.Quote(effect.Address)} over mode {ConfirmationText.Quote(effect.Path.Mode)}, PC interface "
            + $"{ConfirmationText.Quote(effect.Path.PcInterface.Name)} #{effect.Path.PcInterface.Number}. "
            + (effect.DecisionKeys.Count + effect.PasswordKeys.Count == 0
                ? "Any upload decision or password prompt aborts the upload."
                : $"Prompts are answered only for: {ConfirmationText.Line(string.Join(", ", effect.DecisionKeys.Concat(effect.PasswordKeys)))}; any other aborts.")
            + TiaMcpServer.PlcDownload.PlcDownloadWriteDomain.PasswordChannelSentence(effect.PasswordKeys.Count > 0, effect.AllowInsecurePasswordTransfer == true)
            + TiaMcpServer.PlcDownload.PlcDownloadWriteDomain.TrustSentence(effect.TrustedCertificates, "upload") + " "
            + PlcOnlineConnectionWriteDomain.PortalConfirmationSentence(effect.PortalConfirmations);
    }

    public WriteValidation Validate(IReadOnlyList<PlcStationUploadWriteItem> items, McpAccessMode accessMode)
    {
        if (!OperationPolicyCatalog.IsAllowed(accessMode, PlcOnlineMethods.StationUpload))
            return WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "plc_station_upload requires read-write or full access mode.");
        if (items.Count != 1 || items[0] is null || items[0].Operation != PlcOnlineMethods.StationUpload)
            return Invalid("Exactly one host-supplied plc_station_upload operation is required.");
        var item = items[0];
        if (PlcOnlinePolicy.ValidateAddress(item.Address) is { } address) return Invalid(address);
        if (PlcOnlinePolicy.ValidatePath(item.Mode, item.PcInterface?.Name, item.PcInterface?.Number ?? -1, null, requireTarget: false) is { } path)
            return Invalid(path);
        if (item.CheckDecisions.Count > 0)
            return Invalid($"plc_station_upload accepts no check decisions (true): the V21 upload namespace documents no check configuration. "
                + $"Answer a selection with its enum member name instead ({string.Join(", ", item.CheckDecisions)}).");
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(item.PortalConfirmations))
            return Invalid($"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        if (PlcTlsTrustPolicy.NormalizeList(item.TrustedCertificates, out _) is { } trusted) return Invalid(trusted);
        return PlcOnlineConnectionWriteDomain.UnsetPassword(item.Passwords)
            ?? (PlcCallbackPolicy.ValidateEntries(item.Decisions, item.Passwords) is { } entries ? Invalid(entries) : WriteValidation.Valid());
    }

    public async Task<WritePlan<PlcStationUploadEffects>> PlanAsync(IReadOnlyList<PlcStationUploadWriteItem> items)
    {
        var item = items.Single();
        var listed = await _listInterfaces().ConfigureAwait(false);
        if (!listed.Success)
            return WritePlan<PlcStationUploadEffects>.Fail(listed.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                listed.Error ?? "The station upload interfaces could not be listed.");
        PlcStationUploadInterfacesInfo interfaces;
        try
        {
            interfaces = PlcOnlinePayloadContract.DecodeUploadInterfaces(listed.Payload);
        }
        catch (JsonException)
        {
            return WritePlan<PlcStationUploadEffects>.Fail(WorkerFailureCategories.ProtocolError, PlcOnlinePayloadContract.ProtocolFailureMessage);
        }

        var resolution = PlcOnlinePathResolver.ResolveUpload(interfaces, item);
        if (!resolution.Success) return WritePlan<PlcStationUploadEffects>.Fail(resolution.Error!.Category, resolution.Error.Message);
        _planned = resolution.Effects!;
        return WritePlan<PlcStationUploadEffects>.Ok(new[]
        {
            ItemPlan<PlcStationUploadEffects>.Resolved(_planned, new[]
            {
                new CheckedPrecondition("upload_path_offline",
                    $"{_planned.Path.Mode} / {_planned.Path.PcInterface.Name} #{_planned.Path.PcInterface.Number} / {_planned.Address}",
                    "resolved from list_station_upload_interfaces", Satisfied: true),
            }),
        });
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<PlcStationUploadWriteItem> items, IReadOnlyList<ItemPlan<PlcStationUploadEffects>> plans)
    {
        var effect = plans.Single().Effect!;
        return new[]
        {
            new FiredGuard(PlcOnlineGuardDefinitions.AddsDevice, items.Single().OperationId,
                $"Station upload from '{effect.Address}' adds the uploaded station as a new device to the offline project, which is not saved; "
                + "the device at that address is read, not changed."),
        };
    }

    public async Task<ItemReplan<PlcStationUploadEffects>> ReplanAsync(PlcStationUploadWriteItem item)
    {
        var plan = await PlanAsync(new[] { item }).ConfigureAwait(false);
        return plan.Success ? ItemReplan<PlcStationUploadEffects>.Ok(plan.Items.Single())
            : ItemReplan<PlcStationUploadEffects>.Fail(plan.Error!.Category, plan.Error.Message);
    }

    public Task<WorkerCallResult> MutateAsync(PlcStationUploadWriteItem item)
    {
        var planned = _planned ?? throw new InvalidOperationException("plc_station_upload must be planned before it runs.");
        return _upload(new PlcOnlineRequestInfo
        {
            Address = planned.Address,
            Mode = planned.Path.Mode,
            PcInterfaceName = planned.Path.PcInterface.Name,
            PcInterfaceNumber = planned.Path.PcInterface.Number,
            Decisions = item.Decisions.ToList(),
            Passwords = item.Passwords.ToList(),
            PortalConfirmations = planned.PortalConfirmations,
            TrustedCertificates = planned.TrustedCertificates.ToList(),
            AllowInsecurePasswordTransfer = planned.AllowInsecurePasswordTransfer == true,
        });
    }

    public StructuredOperationItem Project(PlcStationUploadWriteItem item, WorkerCallResult result)
    {
        _outcome = ProjectOutcome(result, _planned ?? throw new InvalidOperationException("plc_station_upload must be planned before it is projected."),
            item.Decisions, item.Passwords);
        return new StructuredOperationItem(item.OperationId, item.Operation, _outcome.Status,
            _outcome.Value is null ? null : CanonicalJson.ToElement(_outcome.Value), _outcome.Failure, Omission: null, SkipReason: null, result.Warnings);
    }

    /// <summary>The typed outcome of one worker result; failures keep any decoded evidence.</summary>
    internal static StandaloneToolOutcome<PlcStationUploadResultInfo> ProjectOutcome(WorkerCallResult result, PlcStationUploadEffects planned,
        IReadOnlyList<PlcCallbackDecisionInfo> decisions, IReadOnlyList<PlcCallbackPasswordInfo> passwords)
    {
        PlcStationUploadResultInfo Decode(string payload, bool requireUploadResult)
            => PlcOnlinePayloadContract.DecodeUpload(payload, planned.Address, planned.Path, decisions, passwords, requireUploadResult,
                planned.PortalConfirmations, planned.TrustedCertificates, planned.AllowInsecurePasswordTransfer == true);

        if (!result.Success)
        {
            var category = result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
            var failure = new StructuredOperationFailure(category, result.Error ?? "The plc_station_upload worker operation failed.", EngineeringError: result.EngineeringError);
            if (result.PlcDownloadOutcome is null) return new(OperationBatchStatus.Failed, null, failure, null);
            try
            {
                return new(OperationBatchStatus.Failed, Decode(result.PlcDownloadOutcome, requireUploadResult: false), failure, null);
            }
            catch (JsonException)
            {
                return new(OperationBatchStatus.Failed, null, new StructuredOperationFailure(WorkerFailureCategories.ProtocolError,
                    $"The worker reported '{category}' with upload evidence that did not match the declared contract; the evidence was rejected.",
                    EngineeringError: result.EngineeringError), null);
            }
        }

        PlcStationUploadResultInfo value;
        try
        {
            value = Decode(result.Payload, requireUploadResult: true);
        }
        catch (JsonException)
        {
            return new(OperationBatchStatus.Failed, null,
                new StructuredOperationFailure(WorkerFailureCategories.ProtocolError, PlcOnlinePayloadContract.ProtocolFailureMessage), null);
        }

        if (value.Abort is { } abort)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                PlcCallbackRecorder.AbortMessage(abort)), null);
        if (string.Equals(value.State, "Error", StringComparison.Ordinal) || value.ErrorCount != 0 || value.UploadedStationName is null)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal reported upload state '{value.State}' with {value.ErrorCount} error(s)"
                + (value.UploadedStationName is null ? " and no uploaded station" : string.Empty) + "; see result.value.messages."), null);
        if (PlcOnlineConnectionWriteDomain.DeclinedConfirmations("plc_station_upload", value.PortalConfirmationPolicy, value.PortalConfirmationsObserved,
                value.PortalConfirmations.Count, value.PortalConfirmationsComplete) is { } declined)
            return new(OperationBatchStatus.Failed, value, declined, null);
        return new(OperationBatchStatus.Succeeded, value, null, null);
    }

    public Task<object?> VerifyAsync(StructuredOperationBatch batch) => Task.FromResult<object?>(null);

    public PlcStationUploadWriteResponse Compose(WriteReport<PlcStationUploadEffects, object> report)
    {
        var warnings = report.Warnings
            .Concat(report.Batch?.Operations.SelectMany(operation => operation.Warnings) ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToList();
        var outcome = report.Phase == WritePhases.Applied ? _outcome : null;
        if (report.Phase == WritePhases.Applied)
        {
            warnings.Add(outcome?.Status == OperationBatchStatus.Succeeded ? SaveNote : InspectBeforeRetry);
            if (outcome?.Value is { UnusedDecisions.Count: > 0 } value)
                warnings.Add($"No presented configuration used: {string.Join(", ", value.UnusedDecisions)}.");
            if (outcome?.Value is { } uploaded && PlcOnlineConnectionWriteDomain.AcceptedConfirmationsWarning("plc_station_upload",
                    uploaded.PortalConfirmationPolicy, uploaded.PortalConfirmationsObserved) is { } accepted)
                warnings.Add(accepted);
        }

        var response = new PlcStationUploadWriteResponse(ToolName, ContractVersion, report.Success, report.Error, warnings,
            report.Phase, report.Guards, report.Effects.FirstOrDefault()?.Effect, outcome);
        return PlcOnlineConnectionResponseBudget.Fit(response, r => r.Result, (r, o) => r with { Result = o }, r => r.Warnings.ToList(),
            (r, w) => r with { Warnings = w }, "plc_station_upload",
            "Inspect the project tree with browse_project_tree; do not repeat the upload to recover its result.");
    }

    private static WriteValidation Invalid(string message) => WriteValidation.Invalid(WorkerFailureCategories.ValidationError, message);
}
