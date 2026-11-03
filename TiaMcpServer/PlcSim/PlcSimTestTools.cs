using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.PlcSim;

[McpServerToolType]
public class PlcSimTestTools
{
    internal const string ToolName = "plcsim_test";

    /// <summary>Head-room kept between maxDurationMs and the worker request timeout for setup and IPC.</summary>
    public const int WorkerTimeoutMarginMs = 30_000;

    [McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcSimTestResponse))]
    [Description("Run a test scenario against one S7-PLCSIM Advanced instance inside the PLCSIM worker, so steps do not cross IPC: up to 200 ordered steps of kind write {tag, dataType, value}, wait {ms <= 60000} or expect {tag, dataType, comparison equals|notEquals|greaterThan|greaterOrEqual|lessThan|lessOrEqual|between, value (or min and max for between), timeoutMs default 2000 <= 60000, pollMs default 50 >= 10}, each with an optional name. expect polls until the comparison holds or timeoutMs elapses. dataType is bool, int8, int16, int32, int64, float, double (uint8, uint16, uint32, uint64 and string for expect only); bool/string allow only equals/notEquals; floating-point equality is exact, so use between for tolerances. Timing is wall-clock against PLCSIM's default operating mode with no virtual-time synchronization: use tolerant timeouts. continueOnFailure (default false) stops at the first failed or error step and skips the rest. maxDurationMs (default 120000, at most 600000) bounds the whole scenario and is clamped with a warning to stay 30 s below the worker request timeout (--worker-timeout-minutes). Per step: status passed|failed|error|skipped, lastObserved, attempts, elapsedMs, message; result.passed means every expect passed and no step errored. dryRun true returns the normalized plan without touching the runtime. Writes are not rolled back. Each call is audited. instance is the exact case-sensitive Control Panel name.")]
    public static async Task<CallToolResult> PlcSimTest(
        PlcSimWorkerClient plcSimClient,
        PlcSimAudit audit,
        [Description("Exact, case-sensitive PLCSIM instance name.")] string instance,
        [Description("Ordered scenario steps (1 to 200).")] PlcSimTestStep[] steps,
        [Description("Continue after a failed or error step. Default false.")] bool continueOnFailure = false,
        [Description("Whole-scenario limit in ms, 1 to 600000, default 120000; clamped below the worker request timeout.")] int? maxDurationMs = null,
        [Description("True returns the normalized plan without touching the runtime; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var startedAt = audit.Now;
        var startTimestamp = audit.Timestamp;
        var mode = plcSimClient.AccessPolicy.Mode;
        var requested = new { instance, steps, continueOnFailure, maxDurationMs, dryRun };

        CallToolResult Finish(PlcSimTestResponse response, bool isError, bool actual)
        {
            var text = CanonicalJson.Serialize(response);
            var status = response.Result is null
                ? response.Phase == PlcSimPhases.Preview ? OperationBatchStatus.Skipped : OperationBatchStatus.Failed
                : response.Result.Passed ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed;
            // A failed assertion is not a failure category: the audit item records the counts only.
            var failureMessage = response.Error?.Message ?? (response.Result is { Passed: false } failed
                ? $"{failed.Summary.Failed} failed, {failed.Summary.Errors} error, {failed.Summary.Skipped} skipped of {failed.Summary.Total} steps."
                : null);
            audit.Append(ToolName, mode, actual, requested, response.Phase, text,
                new[] { PlcSimAudit.Item("scenario", "run_test", instance, status, response.Error?.Category, failureMessage,
                    response.Warnings, actual ? audit.ElapsedMs(startTimestamp) : null) },
                startedAt, startTimestamp);
            return StructuredToolResult.CreateCanonical(text, isError);
        }

        var errors = new List<string>();
        PlcSimRequestRules.ValidateInstance(instance, errors, string.Empty);
        var test = new PlcSimTestRequestInfo
        {
            ContinueOnFailure = continueOnFailure,
            MaxDurationMs = maxDurationMs,
            Steps = steps?.Select(step => step is null ? null! : ToInfo(step)).ToList()
        };
        PlcSimRequestRules.ValidateTest(test, errors);
        if (errors.Count != 0)
            return Finish(Rejection(dryRun, WorkerFailureCategories.ValidationError, string.Join("\n", errors)), true, false);
        if (!OperationPolicyCatalog.IsAllowed(mode, PlcSimMethods.RunTest))
            return Finish(Rejection(dryRun, WorkerFailureCategories.AccessDenied,
                $"Operation 'plcsim_test' is not permitted in {McpAccessModeNames.ToName(mode)} mode."), true, false);

        var warnings = new List<string>();
        var timeoutMs = (int)Math.Min(int.MaxValue, plcSimClient.RequestTimeout.TotalMilliseconds);
        var requestedDuration = maxDurationMs ?? PlcSimRequestRules.DefaultMaxDurationMs;
        var effective = Math.Max(1, Math.Min(requestedDuration, timeoutMs - WorkerTimeoutMarginMs));
        if (effective < requestedDuration)
            warnings.Add($"maxDurationMs {requestedDuration} was clamped to {effective}: the worker request timeout is {timeoutMs} ms "
                + $"and {WorkerTimeoutMarginMs} ms stay reserved for setup and IPC. Raise --worker-timeout-minutes to run longer scenarios.");
        var normalized = PlcSimRequestRules.NormalizeTest(test, effective);

        if (dryRun)
        {
            var plan = new PlcSimTestPlan(instance, continueOnFailure, effective, timeoutMs, normalized.Steps!, ApiMembers(normalized));
            return Finish(new PlcSimTestResponse(ToolName, PlcSimContractVersion.Current, true, PlcSimPhases.Preview, true, null,
                warnings, plan, null), false, false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var worker = await plcSimClient.InvokeAsync(PlcSimMethods.RunTest,
            new PlcSimRequestInfo { Instance = instance, Test = normalized }).ConfigureAwait(false);
        warnings.AddRange(worker.Warnings);
        if (!worker.Success)
            return Finish(new PlcSimTestResponse(ToolName, PlcSimContractVersion.Current, false, PlcSimPhases.Error, false,
                new OperationBatchReadError(worker.FailureCategory!, worker.Error ?? "The PLCSIM scenario failed."), warnings, null, null), true, true);

        PlcSimTestResultInfo result;
        try
        {
            result = PlcSimPayloadContract.DecodeTest(worker.Payload, instance, normalized.Steps!.Count);
        }
        catch (System.Text.Json.JsonException)
        {
            return Finish(new PlcSimTestResponse(ToolName, PlcSimContractVersion.Current, false, PlcSimPhases.Error, false,
                new OperationBatchReadError(WorkerFailureCategories.ProtocolError,
                    "The worker plcsim_run_test payload did not match its declared result contract and was rejected. Inspect the instance before retrying; writes may have been applied."),
                warnings, null, null), true, true);
        }

        var response = Fit(new PlcSimTestResponse(ToolName, PlcSimContractVersion.Current, result.Passed, PlcSimPhases.Executed, false, null,
            warnings, null, result));
        return Finish(response, response.Error is not null, true);
    }

    internal static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(Rejection(false, category, message), isError: true);

    private static PlcSimTestResponse Rejection(bool dryRun, string category, string message)
        => new(ToolName, PlcSimContractVersion.Current, false, PlcSimPhases.Error, dryRun,
            new OperationBatchReadError(category, message), Array.Empty<string>(), null, null);

    private static PlcSimTestStepInfo ToInfo(PlcSimTestStep step) => new()
    {
        Kind = step.Kind,
        Name = step.Name,
        Tag = step.Tag,
        DataType = step.DataType,
        Value = step.Value,
        Comparison = step.Comparison,
        Min = step.Min,
        Max = step.Max,
        Ms = step.Ms,
        TimeoutMs = step.TimeoutMs,
        PollMs = step.PollMs,
    };

    private static IReadOnlyList<string> ApiMembers(PlcSimTestRequestInfo test)
    {
        var members = new List<string> { "SimulationRuntimeManager.CreateInterface(String)", "IInstance.Name" };
        foreach (var step in test.Steps!)
        {
            if (step.Kind == PlcSimRequestRules.StepWrite)
                members.Add("IInstance." + PlcSimDataTypes.WriteMethod(step.DataType!));
            else if (step.Kind == PlcSimRequestRules.StepExpect)
                members.Add("IInstance." + PlcSimDataTypes.ReadMethod(step.DataType!));
        }

        return members.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Keeps the document within the response budget by dropping per-step detail in a fixed order:
    /// messages of passed and skipped steps, then their observations, then every message beyond 120
    /// characters. Statuses, counts and errors are never dropped; if even that does not fit, the
    /// call reports <c>result_metadata_too_large</c> with the summary.
    /// </summary>
    internal static PlcSimTestResponse Fit(PlcSimTestResponse response, int? maxDocumentChars = null)
    {
        var limit = maxDocumentChars ?? StructuredOperationBatchPayloadBudget.MaxDocumentChars;
        bool Fits(PlcSimTestResponse candidate) => CanonicalJson.Serialize(candidate).Length <= limit;
        if (Fits(response) || response.Result is not { } result)
            return response;

        var omitted = 0;
        var warnings = response.Warnings.ToList();
        var marker = warnings.Count;
        warnings.Add(string.Empty);
        PlcSimTestResponse Current()
        {
            warnings[marker] = $"{omitted} step details (messages or observations) were omitted to fit the {limit}-character response budget.";
            return response with { Warnings = warnings.ToArray() };
        }

        var stages = new Func<PlcSimTestStepResultInfo, bool>[]
        {
            step => Clear(step, step.Status is PlcSimStatus.Passed or PlcSimStatus.Skipped, message: true),
            step => Clear(step, step.Status == PlcSimStatus.Passed, message: false),
            step => Shorten(step),
        };
        foreach (var stage in stages)
        {
            foreach (var step in result.Steps)
            {
                if (stage(step))
                    omitted++;
            }

            if (Fits(Current()))
                return Current();
        }

        return response with
        {
            Success = false,
            Phase = PlcSimPhases.Error,
            Result = null,
            Error = new OperationBatchReadError(WorkerFailureCategories.ResultMetadataTooLarge,
                $"The scenario ran (passed: {result.Passed}; {result.Summary.Passed} passed, {result.Summary.Failed} failed, {result.Summary.Errors} error, {result.Summary.Skipped} skipped of {result.Summary.Total}), "
                + "but its step results do not fit the response budget. Use shorter tag names and fewer steps per call, or raise --max-document-chars."),
        };
    }

    private static bool Clear(PlcSimTestStepResultInfo step, bool applies, bool message)
    {
        if (!applies)
            return false;
        if (message && step.Message is not null)
        {
            step.Message = null;
            return true;
        }

        if (!message && step.LastObserved is not null)
        {
            step.LastObserved = null;
            return true;
        }

        return false;
    }

    private static bool Shorten(PlcSimTestStepResultInfo step)
    {
        if (step.Message is not { Length: > 120 } text)
            return false;
        step.Message = text.Substring(0, 120) + " [message shortened]";
        return true;
    }

    private static class PlcSimStatus
    {
        public const string Passed = "passed";
        public const string Skipped = "skipped";
    }
}
