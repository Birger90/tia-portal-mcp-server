using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.PlcSimWorker.Runtime;

namespace TiaMcpServer.PlcSimWorker.Operations;

/// <summary>
/// Runs one validated, normalized <c>plcsim_test</c> scenario inside the worker, so polling and
/// waits never cross the host/worker pipe per step. Timing is wall-clock against the PLCSIM
/// default operating mode: the runner does not synchronize with the virtual CPU's cycle or
/// virtual time, so expectations need tolerant timeouts.
/// </summary>
internal static class PlcSimTestRunner
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Error = "error";
    public const string Skipped = "skipped";

    public static PlcSimTestResultInfo Run(IPlcSimInstance instance, PlcSimTestRequestInfo test, IPlcSimClock clock, ICollection<string> warnings)
    {
        var steps = test.Steps!;
        var maxDuration = test.MaxDurationMs ?? PlcSimRequestRules.DefaultMaxDurationMs;
        var started = clock.ElapsedMilliseconds;
        long Now() => clock.ElapsedMilliseconds - started;
        long Remaining() => maxDuration - Now();

        var result = new PlcSimTestResultInfo
        {
            Instance = instance.Name,
            ContinueOnFailure = test.ContinueOnFailure,
            MaxDurationMs = maxDuration,
        };
        var stopReason = (string?)null;

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var outcome = new PlcSimTestStepResultInfo
            {
                Index = index,
                Name = step.Name,
                Kind = step.Kind!,
                Tag = step.Tag,
                DataType = step.DataType,
                StartOffsetMs = Now(),
            };
            result.Steps.Add(outcome);

            if (stopReason is not null)
            {
                outcome.Status = Skipped;
                outcome.Message = stopReason;
                continue;
            }

            if (Remaining() <= 0)
            {
                Abort(result, outcome, $"maxDurationMs ({maxDuration}) elapsed before this step started.");
            }
            else
            {
                switch (step.Kind)
                {
                    case PlcSimRequestRules.StepWait:
                        RunWait(step, outcome, clock, Remaining, result, maxDuration);
                        break;
                    case PlcSimRequestRules.StepWrite:
                        RunWrite(instance, step, outcome, warnings);
                        break;
                    default:
                        RunExpect(instance, step, outcome, clock, Now, Remaining, result, maxDuration);
                        break;
                }
            }

            outcome.ElapsedMs = Now() - outcome.StartOffsetMs;
            if (result.Aborted)
                stopReason = $"Skipped: the scenario was aborted ({result.AbortReason}).";
            else if (outcome.Status is Failed or Error && !test.ContinueOnFailure)
                stopReason = $"Skipped: step {index} ended '{outcome.Status}' and continueOnFailure is false.";
        }

        result.DurationMs = Now();
        var summary = result.Summary;
        summary.Total = result.Steps.Count;
        summary.Passed = result.Steps.Count(step => step.Status == Passed);
        summary.Failed = result.Steps.Count(step => step.Status == Failed);
        summary.Errors = result.Steps.Count(step => step.Status == Error);
        summary.Skipped = result.Steps.Count(step => step.Status == Skipped);
        summary.Expects = result.Steps.Count(step => step.Kind == PlcSimRequestRules.StepExpect);
        summary.ExpectsPassed = result.Steps.Count(step => step.Kind == PlcSimRequestRules.StepExpect && step.Status == Passed);
        result.Passed = summary.Failed == 0 && summary.Errors == 0 && summary.Skipped == 0 && !result.Aborted;
        if (summary.Expects == 0)
            warnings.Add("The scenario has no expect step, so 'passed' only means every write and wait completed.");
        return result;
    }

    private static void Abort(PlcSimTestResultInfo result, PlcSimTestStepResultInfo outcome, string reason)
    {
        result.Aborted = true;
        result.AbortReason = reason;
        outcome.Status = Error;
        outcome.Message = reason;
    }

    private static void RunWait(PlcSimTestStepInfo step, PlcSimTestStepResultInfo outcome, IPlcSimClock clock, Func<long> remaining,
        PlcSimTestResultInfo result, int maxDuration)
    {
        var ms = step.Ms!.Value;
        outcome.Attempts = 1;
        if (ms > remaining())
        {
            clock.Sleep((int)Math.Max(0, remaining()));
            Abort(result, outcome, $"maxDurationMs ({maxDuration}) elapsed during a {ms} ms wait.");
            return;
        }

        clock.Sleep(ms);
        outcome.Status = Passed;
        outcome.Message = $"Waited {ms} ms.";
    }

    /// <summary>A write whose API return signals failure (<see cref="PlcSimInvocation.Failed"/>) ends the step in <c>error</c>.</summary>
    private static void RunWrite(IPlcSimInstance instance, PlcSimTestStepInfo step, PlcSimTestStepResultInfo outcome, ICollection<string> warnings)
    {
        outcome.Attempts = 1;
        PlcSimValueCodec.TryParse(step.DataType!, step.Value, out var typed, out _);
        try
        {
            var invocation = instance.Write(PlcSimTagNames.ToApiName(step.Tag!), step.DataType!, typed!);
            var action = $"plcsim_test step {outcome.Index} write '{step.Tag}'";
            if (invocation.Failed)
                throw invocation.Failure(action);
            if (invocation.ReturnWarning(action) is { } returnWarning)
                warnings.Add(returnWarning);
            outcome.Status = Passed;
            outcome.Message = $"Wrote {PlcSimValueCodec.ToJson(typed).GetRawText()}.";
        }
        catch (PlcSimOperationException exception)
        {
            outcome.Status = Error;
            outcome.Message = exception.Message;
            outcome.Error = exception.Detail;
        }
    }

    private static void RunExpect(IPlcSimInstance instance, PlcSimTestStepInfo step, PlcSimTestStepResultInfo outcome, IPlcSimClock clock,
        Func<long> now, Func<long> remaining, PlcSimTestResultInfo result, int maxDuration)
    {
        var dataType = step.DataType!;
        var comparison = step.Comparison!;
        object? expected = null, min = null, max = null;
        if (comparison == PlcSimComparisons.Between)
        {
            PlcSimValueCodec.TryParse(dataType, step.Min, out min, out _);
            PlcSimValueCodec.TryParse(dataType, step.Max, out max, out _);
        }
        else
        {
            PlcSimValueCodec.TryParse(dataType, step.Value, out expected, out _);
        }

        var expectation = PlcSimComparisons.Describe(comparison,
            expected is null ? null : PlcSimValueCodec.ToJson(expected).GetRawText(),
            min is null ? null : PlcSimValueCodec.ToJson(min).GetRawText(),
            max is null ? null : PlcSimValueCodec.ToJson(max).GetRawText());
        var timeout = step.TimeoutMs ?? PlcSimRequestRules.DefaultExpectTimeoutMs;
        var poll = step.PollMs ?? PlcSimRequestRules.DefaultPollMs;
        var stepStart = now();

        while (true)
        {
            if (remaining() <= 0)
            {
                Abort(result, outcome, $"maxDurationMs ({maxDuration}) elapsed while waiting for {step.Tag} {expectation}.");
                return;
            }

            outcome.Attempts++;
            object observed;
            try
            {
                observed = instance.Read(PlcSimTagNames.ToApiName(step.Tag!), dataType);
            }
            catch (PlcSimOperationException exception)
            {
                outcome.Status = Error;
                outcome.Message = exception.Message;
                outcome.Error = exception.Detail;
                return;
            }

            outcome.LastObserved = PlcSimValueCodec.ToJson(observed);
            var waited = now() - stepStart;
            if (PlcSimComparisons.Evaluate(dataType, comparison, observed, expected, min, max))
            {
                outcome.Status = Passed;
                outcome.Message = $"Observed {outcome.LastObserved.Value.GetRawText()} ({expectation}) after {outcome.Attempts} read(s), {waited} ms.";
                return;
            }

            if (waited >= timeout)
            {
                outcome.Status = Failed;
                outcome.Message = $"Expected {step.Tag} {expectation}; last observed {outcome.LastObserved.Value.GetRawText()} after {outcome.Attempts} read(s) in {waited} ms (timeoutMs {timeout}).";
                return;
            }

            var sleep = Math.Min(poll, Math.Min(timeout - waited, remaining()));
            if (sleep > 0)
                clock.Sleep((int)sleep);
        }
    }
}
