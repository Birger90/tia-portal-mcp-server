namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// Limits, defaults and validation shared by the host (before any worker start) and the PLCSIM
/// worker (before any API call). Validation is strict: required members, inapplicable members,
/// exact data-type and comparison names, and typed values are all checked; nothing is coerced.
/// </summary>
public static class PlcSimRequestRules
{
    public const int MaxInstanceNameChars = 256;
    public const int MaxTagNameChars = 1024;
    public const int MaxNameFilterChars = 256;
    public const int MaxTagsPerOperation = 100;
    public const int DefaultListTagsMax = 500;
    public const int MaxListTagsMax = 5000;

    public const int MaxSteps = 200;
    public const int MaxStepNameChars = 256;
    public const int DefaultMaxDurationMs = 120_000;
    public const int MaxMaxDurationMs = 600_000;
    public const int MaxWaitMs = 60_000;
    public const int DefaultExpectTimeoutMs = 2_000;
    public const int MaxExpectTimeoutMs = 60_000;
    public const int DefaultPollMs = 50;
    public const int MinPollMs = 10;
    public const int MaxPollMs = 60_000;

    public const string StepWrite = "write";
    public const string StepWait = "wait";
    public const string StepExpect = "expect";

    public static IReadOnlyList<string> StepKinds { get; } = Array.AsReadOnly(new[] { StepWrite, StepWait, StepExpect });

    public static void ValidateInstance(string? instance, ICollection<string> errors, string prefix)
    {
        if (string.IsNullOrWhiteSpace(instance))
            errors.Add($"{prefix}instance is required and must be nonblank.");
        else if (instance!.Length > MaxInstanceNameChars)
            errors.Add($"{prefix}instance must be at most {MaxInstanceNameChars} characters.");
    }

    public static void ValidateTags(IReadOnlyList<PlcSimTagRequestInfo?>? tags, bool write, ICollection<string> errors, string prefix)
    {
        if (tags is null || tags.Count == 0)
        {
            errors.Add($"{prefix}tags must contain at least one tag.");
            return;
        }

        if (tags.Count > MaxTagsPerOperation)
        {
            errors.Add($"{prefix}tags exceeds the maximum of {MaxTagsPerOperation} (received {tags.Count}).");
            return;
        }

        for (var index = 0; index < tags.Count; index++)
        {
            var tag = tags[index];
            var at = $"{prefix}tags[{index}]";
            if (tag is null)
            {
                errors.Add($"{at} must not be null.");
                continue;
            }

            ValidateTagName(tag.Name, errors, at + ".name");
            if (!ValidateDataType(tag.DataType, write, errors, at + ".dataType"))
                continue;
            if (write)
            {
                if (!PlcSimValueCodec.TryParse(tag.DataType!, tag.Value, out _, out var error))
                    errors.Add($"{at}.value: {error}.");
            }
            else if (tag.Value is not null)
            {
                errors.Add($"{at}.value is not valid for read_tags.");
            }
        }
    }

    /// <summary>Returns a copy with defaults applied; validation is unchanged by normalization.</summary>
    public static PlcSimTestRequestInfo NormalizeTest(PlcSimTestRequestInfo test, int? maxDurationMs = null)
        => new()
        {
            ContinueOnFailure = test.ContinueOnFailure,
            MaxDurationMs = maxDurationMs ?? test.MaxDurationMs ?? DefaultMaxDurationMs,
            Steps = test.Steps?.Select(step => step is null ? null! : NormalizeStep(step)).ToList()
        };

    public static PlcSimTestStepInfo NormalizeStep(PlcSimTestStepInfo step) => new()
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
        TimeoutMs = step.Kind == StepExpect ? step.TimeoutMs ?? DefaultExpectTimeoutMs : step.TimeoutMs,
        PollMs = step.Kind == StepExpect ? step.PollMs ?? DefaultPollMs : step.PollMs,
    };

    public static void ValidateTest(PlcSimTestRequestInfo? test, ICollection<string> errors)
    {
        if (test is null)
        {
            errors.Add("A test scenario is required.");
            return;
        }

        if (test.MaxDurationMs is { } duration && (duration < 1 || duration > MaxMaxDurationMs))
            errors.Add($"maxDurationMs must be 1 to {MaxMaxDurationMs}.");

        var steps = test.Steps;
        if (steps is null || steps.Count == 0)
        {
            errors.Add("steps must contain at least one step.");
            return;
        }

        if (steps.Count > MaxSteps)
        {
            errors.Add($"steps exceeds the maximum of {MaxSteps} (received {steps.Count}).");
            return;
        }

        for (var index = 0; index < steps.Count; index++)
            ValidateStep(steps[index], index, errors);
    }

    private static void ValidateStep(PlcSimTestStepInfo? step, int index, ICollection<string> errors)
    {
        var at = $"steps[{index}]";
        if (step is null)
        {
            errors.Add($"{at} must not be null.");
            return;
        }

        if (step.Name is not null && (string.IsNullOrWhiteSpace(step.Name) || step.Name.Length > MaxStepNameChars))
            errors.Add($"{at}.name must be nonblank and at most {MaxStepNameChars} characters when supplied.");

        if (step.Kind is null || !StepKinds.Contains(step.Kind, StringComparer.Ordinal))
        {
            errors.Add($"{at}.kind must be one of: {string.Join(", ", StepKinds)}.");
            return;
        }

        var allowed = step.Kind switch
        {
            StepWrite => new[] { "tag", "dataType", "value" },
            StepWait => new[] { "ms" },
            _ => new[] { "tag", "dataType", "comparison", "value", "min", "max", "timeoutMs", "pollMs" }
        };
        foreach (var (name, present) in new (string, bool)[]
        {
            ("tag", step.Tag is not null), ("dataType", step.DataType is not null), ("value", step.Value is not null),
            ("comparison", step.Comparison is not null), ("min", step.Min is not null), ("max", step.Max is not null),
            ("ms", step.Ms is not null), ("timeoutMs", step.TimeoutMs is not null), ("pollMs", step.PollMs is not null),
        })
        {
            if (present && !allowed.Contains(name, StringComparer.Ordinal))
                errors.Add($"{at}.{name} is not valid for a {step.Kind} step.");
        }

        switch (step.Kind)
        {
            case StepWait:
                if (step.Ms is not { } ms || ms < 1 || ms > MaxWaitMs)
                    errors.Add($"{at}.ms is required and must be 1 to {MaxWaitMs}.");
                return;
            case StepWrite:
                ValidateTagName(step.Tag, errors, at + ".tag");
                if (ValidateDataType(step.DataType, write: true, errors, at + ".dataType")
                    && !PlcSimValueCodec.TryParse(step.DataType!, step.Value, out _, out var writeError))
                    errors.Add($"{at}.value: {writeError}.");
                return;
            default:
                ValidateTagName(step.Tag, errors, at + ".tag");
                var typed = ValidateDataType(step.DataType, write: false, errors, at + ".dataType");
                if (step.TimeoutMs is { } timeout && (timeout < 0 || timeout > MaxExpectTimeoutMs))
                    errors.Add($"{at}.timeoutMs must be 0 to {MaxExpectTimeoutMs}.");
                if (step.PollMs is { } poll && (poll < MinPollMs || poll > MaxPollMs))
                    errors.Add($"{at}.pollMs must be {MinPollMs} to {MaxPollMs}.");
                if (step.Comparison is null || !PlcSimComparisons.IsKnown(step.Comparison))
                {
                    errors.Add($"{at}.comparison must be one of: {string.Join(", ", PlcSimComparisons.All)}.");
                    return;
                }

                if (!typed)
                    return;
                if (!PlcSimComparisons.IsSupported(step.DataType!, step.Comparison))
                {
                    errors.Add($"{at}.comparison '{step.Comparison}' is not supported for {step.DataType}; use equals or notEquals.");
                    return;
                }

                if (step.Comparison == PlcSimComparisons.Between)
                {
                    if (step.Value is not null)
                        errors.Add($"{at}.value is not valid with between; use min and max.");
                    var minOk = PlcSimValueCodec.TryParse(step.DataType!, step.Min, out var min, out var minError);
                    var maxOk = PlcSimValueCodec.TryParse(step.DataType!, step.Max, out var max, out var maxError);
                    if (!minOk) errors.Add($"{at}.min: {minError}.");
                    if (!maxOk) errors.Add($"{at}.max: {maxError}.");
                    if (minOk && maxOk && !PlcSimComparisons.Evaluate(step.DataType!, PlcSimComparisons.LessOrEqual, min!, max, null, null))
                        errors.Add($"{at}.min must not be greater than max.");
                }
                else
                {
                    if (step.Min is not null || step.Max is not null)
                        errors.Add($"{at}.min and max are valid only with between.");
                    if (!PlcSimValueCodec.TryParse(step.DataType!, step.Value, out _, out var valueError))
                        errors.Add($"{at}.value: {valueError}.");
                }

                return;
        }
    }

    private static void ValidateTagName(string? name, ICollection<string> errors, string at)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors.Add($"{at} is required and must be nonblank.");
        else if (name!.Length > MaxTagNameChars)
            errors.Add($"{at} must be at most {MaxTagNameChars} characters.");
    }

    private static bool ValidateDataType(string? dataType, bool write, ICollection<string> errors, string at)
    {
        if (!PlcSimDataTypes.IsKnown(dataType))
        {
            errors.Add($"{at} must be one of: {string.Join(", ", write ? PlcSimDataTypes.Writable : PlcSimDataTypes.All)}.");
            return false;
        }

        if (write && !PlcSimDataTypes.IsWritable(dataType))
        {
            errors.Add($"{at} '{dataType}' cannot be written: WriteString is not a verified PLCSIM Advanced API member. Writable types: {string.Join(", ", PlcSimDataTypes.Writable)}.");
            return false;
        }

        return true;
    }
}
