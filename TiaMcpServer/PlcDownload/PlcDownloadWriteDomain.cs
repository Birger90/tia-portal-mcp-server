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

namespace TiaMcpServer.PlcDownload;

/// <summary>
/// plc_download as a single-item guarded write. Planning reads only the offline
/// <c>list_download_targets</c> inventory; the mutation downloads exactly the planned path, to PLCSIM
/// or to real hardware. The human controls it through the access mode: like lifecycle, every actual
/// read-write call is confirmed by the user (<see cref="ConfirmsEveryCall"/>), full runs under
/// policy, and a dry run stops at <c>preview</c> without touching the target. There is no block
/// guard. There is no verification read: download completion is never claimed.
/// </summary>
public sealed class PlcDownloadWriteDomain
    : IWriteDomain<PlcDownloadWriteItem, PlcDownloadEffects, object, PlcDownloadWriteResponse>
{
    public const string Contract = "2.0";

    private const int MaxListedDecisions = 20;

    /// <summary>
    /// Appended to every portal_lost download failure (K27, live 2026-11-02: after such a failure the TIA Portal process was
    /// gone). Nothing is restarted automatically.
    /// </summary>
    internal const string PortalLostAdvice =
        "TIA Portal may have terminated (observed live on 2026-11-02 after a portal_lost download). Check with bind_project "
        + "action list_portals whether a TIA Portal process is still listed; if not, start TIA Portal, re-open the project "
        + "(open_project) and bind it again. This server does not restart TIA Portal or re-open the project by itself.";

    private const string InspectBeforeRetry =
        "The download may already have changed the PLC or simulator (stopped it or loaded part of the project). "
        + "Inspect it before retrying; no rollback or replay was attempted.";

    private readonly Func<string?, Task<WorkerCallResult>> _listTargets;
    private readonly Func<PlcDownloadRequestInfo, Task<WorkerCallResult>> _download;
    private readonly Func<string, string?> _readEnvironment;
    private PlcDownloadEffects? _planned;
    private StandaloneToolOutcome<PlcDownloadResultInfo>? _outcome;

    public PlcDownloadWriteDomain(OpennessWorkerClient workerClient)
        : this(workerClient.ListDownloadTargetsAsync, workerClient.PlcDownloadAsync)
    {
    }

    /// <summary>Worker-independent seam for unit tests of planning, guards and projection.</summary>
    /// <param name="readEnvironment">
    /// Reads an environment variable of the server process (which the worker inherits); used only to
    /// check that a <c>passwords</c> variable is set. Defaults to the process environment.
    /// </param>
    internal PlcDownloadWriteDomain(
        Func<string?, Task<WorkerCallResult>> listTargets,
        Func<PlcDownloadRequestInfo, Task<WorkerCallResult>> download,
        Func<string, string?>? readEnvironment = null)
    {
        _listTargets = listTargets ?? throw new ArgumentNullException(nameof(listTargets));
        _download = download ?? throw new ArgumentNullException(nameof(download));
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    public string ToolName => PlcDownloadMethods.Download;

    public string ContractVersion => Contract;

    public bool ConfirmsEveryCall => true;

    public string DescribeForConfirmation(IReadOnlyList<PlcDownloadWriteItem> items, IReadOnlyList<ItemPlan<PlcDownloadEffects>> plans)
    {
        // Names come from the project or the caller: quoted and escaped. Decision types, selections and
        // password variables are validated identifiers; Line() keeps them on their line regardless.
        static string Q(string? value) => ConfirmationText.Quote(value);
        var effect = plans.Single().Effect!;
        var text = new System.Text.StringBuilder();
        text.Append(effect.SimulationTarget
                ? "Confirm plc_download to the PLCSIM simulation interface: "
                : "Confirm plc_download to a REAL controller (NOT the PLCSIM simulation interface; the machine it controls may stop or move): ")
            .Append($"download {ConfirmationText.Line(effect.Scope)} ({ConfirmationText.Line(effect.DownloadOptions)}); the CPU may be stopped and is ")
            .Append(effect.StartAfterDownload ? "restarted afterwards" : "left in STOP afterwards")
            .Append($". PLC {Q(effect.PlcName)} (device {Q(effect.DeviceName)}) over mode {Q(effect.Path.Mode)}, PC interface {Q(effect.Path.PcInterface.Name)} ")
            .Append($"#{effect.Path.PcInterface.Number}, target interface {Q(effect.Path.TargetInterface)}.");
        if (effect.RhTarget is not null)
            text.Append($" R/H system: the download goes to the {ConfirmationText.Line(effect.RhTarget)} CPU (RHDownloadProvider.DownloadTo{(effect.RhTarget == "primary" ? "Primary" : "Backup")}).");
        text.Append(" Built-in answers: StopModules=StopAll, ConsistentBlocksDownload=ConsistentDownload, AlarmTextLibrariesDownload=ConsistentDownload, ")
            .Append($"OverwriteSystemData=Overwrite, TargetForSoftware={PlcDownloadPolicy.TargetForSoftwareDefault(effect.Path.PcInterface.Name)}");
        text.Append(effect.Decisions.Count == 0
            ? "; no caller decisions."
            : "; caller decisions (override the built-in answers): " + Listed(effect.Decisions.Select(decision =>
                ConfirmationText.Line($"{decision.Type}={(decision.Check ? "true (confirm)" : decision.Selection)}"))) + ".");
        if (effect.Passwords.Count > 0)
            text.Append(" Passwords are read from environment variables: ")
                .Append(Listed(effect.Passwords.Select(password => ConfirmationText.Line($"{password.Type} from {password.Variable}"))))
                .Append('.');
        text.Append(TrustSentence(effect.TrustedCertificates, "download"));
        text.Append(" Any other download configuration aborts the download. Every TIA Portal confirmation dialog raised until the download call returns is answered ")
            .Append(effect.PortalConfirmations == PlcDownloadPolicy.PortalConfirmationPolicies.Accept ? "Yes" : "No")
            .Append(" (a later one, until the next request, gets the same answer and is reported as late).");
        return text.ToString();
    }

    /// <summary>
    /// The prompt sentence for the call's <c>trustedCertificates</c> (shared by plc_download, plc_tia_online and
    /// plc_station_upload): every fingerprint is listed (they are validated hexadecimal, at most 16), or the statement
    /// that a certificate check refuses the connection.
    /// </summary>
    internal static string TrustSentence(IReadOnlyList<string> trustedCertificates, string action)
        => trustedCertificates.Count == 0
            ? $" A TLS certificate check of the device (TlsVerificationConfiguration) aborts the {action}: no certificate is trusted (no trustedCertificates)."
            : $" A TLS certificate check of the device is answered Trusted only when every certificate it presents has one of these SHA-256 fingerprints "
                + $"(trustedCertificates), otherwise NonTrusted and the {action} aborts: "
                + ConfirmationText.Line(string.Join(", ", trustedCertificates)) + ".";

    private static string Listed(IEnumerable<string> entries)
    {
        var all = entries.ToList();
        var shown = string.Join(", ", all.Take(MaxListedDecisions));
        return all.Count > MaxListedDecisions ? $"{shown} and {all.Count - MaxListedDecisions} more (see effects)" : shown;
    }

    public WriteValidation Validate(IReadOnlyList<PlcDownloadWriteItem> items, McpAccessMode accessMode)
    {
        if (!OperationPolicyCatalog.IsAllowed(accessMode, PlcDownloadMethods.Download))
            return WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "plc_download requires read-write or full access mode.");
        if (items.Count != 1 || items[0] is null || items[0].Operation != PlcDownloadMethods.Download)
            return Invalid("Exactly one host-supplied plc_download operation is required.");
        var item = items[0];
        if (item.PlcName is not null && Bad(item.PlcName)) return Invalid("plcName must be nonblank and at most 256 characters when supplied.");
        if (Bad(item.Mode)) return Invalid("mode must be nonblank and at most 256 characters.");
        if (Bad(item.PcInterfaceName)) return Invalid("pcInterface.name must be nonblank and at most 256 characters.");
        if (item.PcInterfaceNumber < 0) return Invalid("pcInterface.number must not be negative.");
        if (item.TargetInterface is not null && Bad(item.TargetInterface))
            return Invalid("targetInterface must be nonblank and at most 256 characters when supplied.");
        if (!PlcDownloadPolicy.TryGetDownloadOptionNames(item.Scope, out _))
            return Invalid($"scope must be one of: {string.Join(", ", PlcDownloadPolicy.Scopes.All)}.");
        if (item.RhTarget is not null and not ("primary" or "backup"))
            return Invalid("rhTarget must be primary or backup when supplied.");
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(item.PortalConfirmations))
            return Invalid($"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        foreach (var password in item.Passwords)
        {
            if (password.Variable == PlcDownloadPasswordMap.UnsetVariable)
                return Invalid(UnsetPasswordVariable(password.Type));
        }

        var decisions = PlcDownloadPolicy.ValidateDecisions(item.Decisions, item.Passwords);
        if (decisions is not null) return Invalid(decisions);
        if (PlcTlsTrustPolicy.NormalizeList(item.TrustedCertificates, out _) is { } trusted) return Invalid(trusted);
        // The worker inherits this process's environment: a missing variable is refused before anything is downloaded.
        // A credential is read only by the worker, at the call (its absence aborts with password_unavailable).
        foreach (var password in item.Passwords)
        {
            if (Contracts.Shared.PasswordSources.KindOf(password.Variable) == Contracts.Shared.PasswordSourceKind.Environment
                && string.IsNullOrEmpty(_readEnvironment(password.Variable)))
                return Invalid(UnsetPasswordVariable(password.Type));
        }

        return WriteValidation.Valid();
    }

    /// <summary>
    /// The refusal for an unset password variable. It never repeats the given name: a "name" whose variable
    /// is not set may be a password pasted by mistake (see <see cref="PlcDownloadPasswordMap.UnsetVariable"/>).
    /// </summary>
    internal static string UnsetPasswordVariable(string configurationType)
        => $"passwords.{configurationType}: the environment variable named for this configuration is not set or is empty in the server's "
            + "environment. Set it in the MCP client configuration (env) and restart the server; never pass the password itself.";

    public async Task<WritePlan<PlcDownloadEffects>> PlanAsync(IReadOnlyList<PlcDownloadWriteItem> items)
    {
        var item = items.Single();
        var listed = await _listTargets(item.PlcName).ConfigureAwait(false);
        if (!listed.Success)
            return WritePlan<PlcDownloadEffects>.Fail(listed.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                listed.Error ?? "The download targets could not be listed.");
        PlcDownloadTargetInventoryInfo inventory;
        try
        {
            inventory = PlcDownloadPayloadContract.DecodeInventory(listed.Payload);
        }
        catch (JsonException)
        {
            return WritePlan<PlcDownloadEffects>.Fail(WorkerFailureCategories.ProtocolError, PlcDownloadPayloadContract.ProtocolFailureMessage);
        }

        var resolution = PlcDownloadPathResolver.Resolve(inventory, item);
        if (!resolution.Success)
            return WritePlan<PlcDownloadEffects>.Fail(resolution.Error!.Category, resolution.Error.Message);
        _planned = resolution.Effects!;
        return WritePlan<PlcDownloadEffects>.Ok(new[]
        {
            ItemPlan<PlcDownloadEffects>.Resolved(_planned, new[]
            {
                new CheckedPrecondition("download_path_offline",
                    $"{_planned.Path.Mode} / {_planned.Path.PcInterface.Name} #{_planned.Path.PcInterface.Number} / {_planned.Path.TargetInterface}",
                    "resolved from list_download_targets", Satisfied: true),
            }),
        });
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<PlcDownloadWriteItem> items, IReadOnlyList<ItemPlan<PlcDownloadEffects>> plans)
    {
        var effect = plans.Single().Effect!;
        var operationId = items.Single().OperationId;
        var guards = new List<FiredGuard>();
        if (!effect.SimulationTarget)
            guards.Add(new(PlcDownloadGuardDefinitions.ToHardware, operationId,
                $"PC interface '{effect.Path.PcInterface.Name}' #{effect.Path.PcInterface.Number} is not '{PlcDownloadPolicy.SimulationPcInterfaceName}': "
                + $"the download goes to the real controller of PLC '{effect.PlcName}' (device '{effect.DeviceName}') through target interface "
                + $"'{effect.Path.TargetInterface}', and the machine it controls may stop or move. TargetForSoftware is answered "
                + $"{PlcDownloadPolicy.TargetForSoftwareDefault(effect.Path.PcInterface.Name)} unless the caller decides otherwise."));
        var stop = effect.Decisions.FirstOrDefault(decision => decision.Type == "StopModules");
        guards.Add(new(PlcDownloadGuardDefinitions.StopsPlc, operationId,
            $"Downloading to PLC '{effect.PlcName}' may stop its CPU (StopModules is answered "
            + (stop is null ? "StopAll" : $"{(stop.Check ? "true" : stop.Selection)} by the caller") + ") and "
            + (effect.StartAfterDownload
                ? "restarts it afterwards (StartModules is answered StartModule)."
                : "leaves it in STOP afterwards (StartModules is answered NoAction).")));
        if (effect.Decisions.FirstOrDefault(decision => decision.Type == Contracts.Plc.Safety.PlcSafetyApiCatalog.SafetyProgramDecisionKey) is { } safety)
            guards.Add(new(PlcDownloadGuardDefinitions.IncludesSafetyProgram, operationId,
                $"The caller answers TIA Portal's F-program download configuration (SafetyProgram: {(safety.Check ? "true" : safety.Selection)}), so the download to "
                + (effect.SimulationTarget
                    ? $"the PLCSIM instance of PLC '{effect.PlcName}'"
                    : $"the real controller of PLC '{effect.PlcName}' through PC interface '{effect.Path.PcInterface.Name}' (an F-CPU running a machine)")
                + $" includes its safety program. {Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice}"));
        return guards;
    }

    public async Task<ItemReplan<PlcDownloadEffects>> ReplanAsync(PlcDownloadWriteItem item)
    {
        var plan = await PlanAsync(new[] { item }).ConfigureAwait(false);
        return plan.Success ? ItemReplan<PlcDownloadEffects>.Ok(plan.Items.Single())
            : ItemReplan<PlcDownloadEffects>.Fail(plan.Error!.Category, plan.Error.Message);
    }

    public Task<WorkerCallResult> MutateAsync(PlcDownloadWriteItem item)
    {
        var planned = _planned ?? throw new InvalidOperationException("plc_download must be planned before it runs.");
        return _download(new PlcDownloadRequestInfo
        {
            PlcName = planned.PlcName,
            DeviceName = planned.DeviceName,
            Mode = planned.Path.Mode,
            PcInterfaceName = planned.Path.PcInterface.Name,
            PcInterfaceNumber = planned.Path.PcInterface.Number,
            PcInterfaceNumberExplicit = item.PcInterfaceNumberExplicit,
            TargetInterface = planned.Path.TargetInterface,
            Scope = planned.Scope,
            StartAfterDownload = planned.StartAfterDownload,
            RhTarget = planned.RhTarget,
            Decisions = planned.Decisions.Select(decision => new PlcDownloadDecisionInfo
            {
                Type = decision.Type, Selection = decision.Selection, Check = decision.Check,
            }).ToList(),
            Passwords = planned.Passwords.Select(password => new PlcDownloadPasswordInfo { Type = password.Type, Variable = password.Variable }).ToList(),
            PortalConfirmations = planned.PortalConfirmations,
            TrustedCertificates = planned.TrustedCertificates.ToList(),
        });
    }

    public StructuredOperationItem Project(PlcDownloadWriteItem item, WorkerCallResult result)
    {
        _outcome = ProjectOutcome(result, _planned ?? throw new InvalidOperationException("plc_download must be planned before it is projected."));
        return new StructuredOperationItem(item.OperationId, item.Operation, _outcome.Status,
            _outcome.Value is null ? null : CanonicalJson.ToElement(_outcome.Value),
            _outcome.Failure, Omission: null, SkipReason: null, result.Warnings);
    }

    /// <summary>The typed outcome of one worker download result; failures keep any decoded evidence.</summary>
    internal static StandaloneToolOutcome<PlcDownloadResultInfo> ProjectOutcome(WorkerCallResult result, PlcDownloadEffects planned)
    {
        if (!result.Success)
        {
            var category = result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
            var message = result.Error ?? "The plc_download worker operation failed.";
            if (category == WorkerFailureCategories.PortalLost) message += " " + PortalLostAdvice;
            var failure = new StructuredOperationFailure(category, message, EngineeringError: result.EngineeringError);
            if (result.PlcDownloadOutcome is null) return new(OperationBatchStatus.Failed, null, failure, null);
            try
            {
                var evidence = PlcDownloadPayloadContract.DecodeResult(result.PlcDownloadOutcome, planned, requireDownloadResult: false);
                return new(OperationBatchStatus.Failed, evidence, failure, null);
            }
            catch (JsonException)
            {
                return new(OperationBatchStatus.Failed, null, new StructuredOperationFailure(WorkerFailureCategories.ProtocolError,
                    $"The worker reported '{category}' with download evidence that did not match the declared contract; the evidence was rejected.",
                    EngineeringError: result.EngineeringError), null);
            }
        }

        PlcDownloadResultInfo value;
        try
        {
            value = PlcDownloadPayloadContract.DecodeResult(result.Payload, planned, requireDownloadResult: true);
        }
        catch (JsonException)
        {
            return new(OperationBatchStatus.Failed, null,
                new StructuredOperationFailure(WorkerFailureCategories.ProtocolError, PlcDownloadPayloadContract.ProtocolFailureMessage), null);
        }

        if (value.Abort is { } abort)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                PlcDownloadPolicy.AbortMessage(abort)), null);
        // Declined confirmations fail the call; accepted ones (portalConfirmations: accept) are evidence and a warning only.
        if (value.PortalConfirmationsObserved > 0 && value.PortalConfirmationPolicy != PlcDownloadPolicy.PortalConfirmationPolicies.Accept)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal raised {value.PortalConfirmationsObserved} confirmation dialog(s) during the download and this tool answered each one No "
                + $"(see result.value.portalConfirmations{(value.PortalConfirmationsComplete ? string.Empty : $"; only the first {value.PortalConfirmations.Count} are listed")}). "
                + $"The download itself reported state '{value.State}' with {value.ErrorCount} error(s), but declining may have changed what was "
                + "downloaded, so the call is not reported as successful. Inspect the PLC or simulator; to answer them Yes, retry with "
                + "portalConfirmations: \"accept\"."), null);
        if (string.Equals(value.State, "Error", StringComparison.Ordinal) || value.ErrorCount != 0)
            return new(OperationBatchStatus.Failed, value, new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal reported download state '{value.State}' with {value.ErrorCount} error(s) and {value.WarningCount} warning(s); see result.value.messages."), null);
        return new(OperationBatchStatus.Succeeded, value, null, null);
    }

    public Task<object?> VerifyAsync(StructuredOperationBatch batch) => Task.FromResult<object?>(null);

    public PlcDownloadWriteResponse Compose(WriteReport<PlcDownloadEffects, object> report)
    {
        var warnings = report.Warnings
            .Concat(report.Batch?.Operations.SelectMany(operation => operation.Warnings) ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToList();
        var outcome = report.Phase == WritePhases.Applied ? _outcome : null;
        if (report.Phase == WritePhases.Applied)
        {
            warnings.Add(PlcDownloadPolicy.CompletionNote);
            if (outcome?.Value is { PortalConfirmationsObserved: > 0 } accepted
                && accepted.PortalConfirmationPolicy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept)
                warnings.Add($"TIA Portal raised {accepted.PortalConfirmationsObserved} confirmation dialog(s) during the download and each was "
                    + "answered Yes (portalConfirmations: accept); see result.value.portalConfirmations.");
            if (outcome?.Status != OperationBatchStatus.Succeeded) warnings.Add(InspectBeforeRetry);
        }

        var effect = report.Effects.FirstOrDefault()?.Effect;
        var response = new PlcDownloadWriteResponse(ToolName, ContractVersion, report.Success, report.Error, warnings,
            report.Phase, report.Guards, effect, BoundOutcome(outcome, StructuredOperationBatchPayloadBudget.MaxItemChars,
                StructuredOperationBatchPayloadBudget.ItemLimitReason));
        if (Length(response) > StructuredOperationBatchPayloadBudget.MaxDocumentChars && response.Result?.Value is not null)
            response = response with
            {
                Result = Omit(response.Result, StructuredOperationBatchPayloadBudget.DocumentLimitReason,
                    StructuredOperationBatchPayloadBudget.MaxDocumentChars),
            };
        var omittedWarnings = 0;
        while (Length(response) > StructuredOperationBatchPayloadBudget.MaxDocumentChars && warnings.Count > 0)
        {
            var largest = warnings.Select((warning, index) => (warning.Length, index)).MaxBy(entry => entry.Length);
            warnings.RemoveAt(largest.index);
            omittedWarnings++;
        }

        if (omittedWarnings > 0) warnings.Add($"{omittedWarnings} warning entries were omitted to fit the plc_download response budget.");
        return response;
    }

    /// <summary>
    /// Sheds download messages first (state, counts, configurations and abort are the evidence that
    /// matters), then omits the whole value with guidance.
    /// </summary>
    internal static StandaloneToolOutcome<PlcDownloadResultInfo>? BoundOutcome(
        StandaloneToolOutcome<PlcDownloadResultInfo>? outcome, int limit, string reason)
    {
        if (outcome?.Value is not { } value || CanonicalJson.Serialize(value).Length <= limit) return outcome;
        var original = CanonicalJson.Serialize(value).Length;
        var shed = CanonicalJson.Deserialize<PlcDownloadResultInfo>(CanonicalJson.Serialize(value));
        var total = shed.Messages.Count;
        while (shed.Messages.Count > 0 && CanonicalJson.Serialize(shed).Length > limit)
            shed.Messages.RemoveRange(shed.Messages.Count / 2, shed.Messages.Count - shed.Messages.Count / 2);
        if (shed.Messages.Count < total)
        {
            shed.MessagesComplete = false;
            shed.MessageOmission = $"{total - shed.Messages.Count} of {total} top-level download messages were omitted to fit the {limit}-character response value budget.";
        }

        return CanonicalJson.Serialize(shed).Length <= limit
            ? outcome with { Value = shed }
            : outcome with
            {
                Status = OperationBatchStatus.Omitted, Value = null,
                Omission = Guidance(reason, limit, original),
            };
    }

    private static StandaloneToolOutcome<PlcDownloadResultInfo> Omit(StandaloneToolOutcome<PlcDownloadResultInfo> outcome, string reason, int limit)
        => outcome with
        {
            Status = OperationBatchStatus.Omitted, Value = null,
            Omission = Guidance(reason, limit, CanonicalJson.Serialize(outcome.Value).Length),
        };

    private static StructuredOperationOmission Guidance(string reason, int limit, int original)
        => new(reason, limit, original, "plc_read",
            "The complete download result was omitted. Inspect the PLC or simulator state and use plc_read list_download_targets; "
            + "do not replay the download to recover its result.");

    private static int Length(PlcDownloadWriteResponse response) => CanonicalJson.Serialize(response).Length;

    private static bool Bad(string? value) => string.IsNullOrWhiteSpace(value) || value!.Length > PlcDownloadPolicy.MaxNameLength;

    private static WriteValidation Invalid(string message) => WriteValidation.Invalid(WorkerFailureCategories.ValidationError, message);
}
