using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcSim;

/// <summary>
/// One declared worker result type per PLCSIM operation, decoded with the required-member
/// worker-payload reader plus a semantic validator. A payload that does not decode is a
/// <c>protocol_error</c> item and is never echoed.
/// </summary>
public static class PlcSimPayloadContract
{
    internal static readonly IReadOnlyDictionary<string, Type> ResultTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["probe_api"] = typeof(PlcSimProbeResultInfo),
        ["list_instances"] = typeof(PlcSimInstanceListInfo),
        ["get_instance"] = typeof(PlcSimInstanceInfo),
        ["list_tags"] = typeof(PlcSimTagListInfo),
        ["read_tags"] = typeof(PlcSimTagReadListInfo),
        ["power_on"] = typeof(PlcSimControlResultInfo),
        ["power_off"] = typeof(PlcSimControlResultInfo),
        ["run"] = typeof(PlcSimControlResultInfo),
        ["stop"] = typeof(PlcSimControlResultInfo),
        ["memory_reset"] = typeof(PlcSimControlResultInfo),
        ["update_tag_list"] = typeof(PlcSimControlResultInfo),
        ["write_tags"] = typeof(PlcSimTagWriteListInfo),
        ["set_scale_factor"] = typeof(PlcSimScaleFactorResultInfo),
    };

    public static StructuredOperationItem Project(PlcSimOperationRequest item, WorkerCallResult result)
        => Project(item, result, Console.Error.WriteLine);

    internal static StructuredOperationItem Project(PlcSimOperationRequest item, WorkerCallResult result, Action<string> writeDiagnostic)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        if (!result.Success)
            return StructuredOperationItem.Failed(item, result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                result.Error ?? "The PLCSIM operation failed.", warnings);
        try
        {
            return item.Operation switch
            {
                "probe_api" => Succeeded(item, Decode<PlcSimProbeResultInfo>(result.Payload, null), warnings),
                "list_instances" => Succeeded(item, Decode<PlcSimInstanceListInfo>(result.Payload, ValidateInstances), warnings),
                "get_instance" => Succeeded(item, Decode<PlcSimInstanceInfo>(result.Payload, value => RequireInstance(item, value.RequestedName)), warnings),
                "list_tags" => ProjectTagList(item, result.Payload, warnings),
                "read_tags" => Succeeded(item, Decode<PlcSimTagReadListInfo>(result.Payload, value => ValidateReads(item, value)), warnings),
                "write_tags" => ProjectWrites(item, result.Payload, warnings),
                "set_scale_factor" => Succeeded(item, Decode<PlcSimScaleFactorResultInfo>(result.Payload, value => RequireInstance(item, value.Instance)), warnings),
                "power_on" or "power_off" or "run" or "stop" or "memory_reset" or "update_tag_list"
                    => ProjectControl(item, result.Payload, warnings),
                _ => throw new JsonException()
            };
        }
        catch (JsonException)
        {
            StructuredPayloadProjection.TryWriteProtocolDiagnostic(PlcSimCatalog.WorkerMethod(item.Operation), writeDiagnostic);
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }
    }

    /// <summary>Decodes a plcsim_run_test payload; throws <see cref="JsonException"/> when it violates its contract.</summary>
    public static PlcSimTestResultInfo DecodeTest(string payload, string instance, int stepCount)
        => CanonicalJson.NormalizeWorkerPayload<PlcSimTestResultInfo>(payload, value =>
        {
            if (!string.Equals(value.Instance, instance, StringComparison.Ordinal) || value.Steps.Count != stepCount)
                throw new JsonException();
            var known = new[] { "passed", "failed", "error", "skipped" };
            if (value.Steps.Any(step => step is null || !known.Contains(step.Status) || step.Error is { } error && !WorkerFailureCategories.IsKnown(error.Category)))
                throw new JsonException();
            for (var index = 0; index < value.Steps.Count; index++)
                if (value.Steps[index].Index != index)
                    throw new JsonException();
            var summary = value.Summary;
            if (summary.Total != stepCount
                || summary.Passed != value.Steps.Count(step => step.Status == "passed")
                || summary.Failed != value.Steps.Count(step => step.Status == "failed")
                || summary.Errors != value.Steps.Count(step => step.Status == "error")
                || summary.Skipped != value.Steps.Count(step => step.Status == "skipped")
                || value.Passed != (summary.Failed == 0 && summary.Errors == 0 && summary.Skipped == 0 && !value.Aborted))
                throw new JsonException();
        }).Value;

    private static StructuredOperationItem Succeeded(PlcSimOperationRequest item, JsonElement result, IReadOnlyList<string> warnings)
        => new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, result, null, null, null, warnings);

    private static JsonElement Decode<T>(string payload, Action<T>? validate)
        => CanonicalJson.NormalizeWorkerPayload(payload, validate).Element;

    private static void RequireInstance(PlcSimOperationRequest item, string instance)
    {
        if (!string.Equals(item.Instance, instance, StringComparison.Ordinal))
            throw new JsonException();
    }

    private static void ValidateInstances(PlcSimInstanceListInfo value)
    {
        if (value.Count != value.Instances.Count)
            throw new JsonException();
    }

    private static void ValidateReads(PlcSimOperationRequest item, PlcSimTagReadListInfo value)
    {
        RequireInstance(item, value.Instance);
        if (value.Tags.Count != item.Tags!.Count
            || value.ReadCount != value.Tags.Count(tag => tag.Status == "read")
            || value.FailedCount != value.Tags.Count(tag => tag.Status == "failed")
            || value.ReadCount + value.FailedCount != value.Tags.Count)
            throw new JsonException();
        for (var index = 0; index < value.Tags.Count; index++)
        {
            var tag = value.Tags[index];
            var requested = item.Tags[index];
            if (tag.Name != requested.Name || tag.DataType != requested.DataType
                || (tag.Status == "read") != (tag.Value is not null && tag.Error is null)
                || tag.Status == "failed" && (tag.Error is null || !WorkerFailureCategories.IsKnown(tag.Error.Category)))
                throw new JsonException();
        }
    }

    private static StructuredOperationItem ProjectTagList(PlcSimOperationRequest item, string payload, IReadOnlyList<string> warnings)
    {
        var value = CanonicalJson.NormalizeWorkerPayload<PlcSimTagListInfo>(payload, list =>
        {
            RequireInstance(item, list.Instance);
            if (list.Tags.Count != list.ReturnedCount || list.ReturnedCount > list.MatchedCount || list.MatchedCount > list.TotalCount
                || list.ReturnedCount > list.Max || list.Truncated != (list.ReturnedCount < list.MatchedCount) || list.BudgetTrimmed
                || list.NameFilter != item.NameFilter)
                throw new JsonException();
        }).Value;

        // Keep as many leading tags as fit the per-value budget instead of omitting the whole result.
        var limit = StructuredOperationBatchPayloadBudget.MaxItemChars;
        if (CanonicalJson.Serialize(value).Length > limit)
        {
            var all = value.Tags;
            value.Tags = new List<JsonElement>();
            var used = CanonicalJson.Serialize(value).Length + 64;
            var kept = new List<JsonElement>();
            foreach (var tag in all)
            {
                var size = CanonicalJson.Serialize(tag).Length + 1;
                if (used + size > limit)
                    break;
                used += size;
                kept.Add(tag);
            }

            value.Tags = kept;
            value.ReturnedCount = kept.Count;
            value.Truncated = kept.Count < value.MatchedCount;
            value.BudgetTrimmed = true;
            warnings = warnings.Concat(new[]
            {
                $"list_tags returned {kept.Count} of {value.MatchedCount} matching tags to fit the {limit}-character value budget. Narrow nameFilter, lower max, or raise --max-item-chars."
            }).ToArray();
        }

        return Succeeded(item, CanonicalJson.ToElement(value), warnings);
    }

    /// <summary>
    /// A control result whose API return signalled an error (<see cref="PlcSimReturnChecks.Failed"/>: a
    /// negative <c>ERuntimeErrorCode</c>, or <c>false</c>) is a failed item with category
    /// <c>plcsim_runtime_error</c> that keeps its typed result (the operating state before/after). A
    /// warning code (positive) succeeds.
    /// </summary>
    private static StructuredOperationItem ProjectControl(PlcSimOperationRequest item, string payload, IReadOnlyList<string> warnings)
    {
        var (value, _, element) = CanonicalJson.NormalizeWorkerPayload<PlcSimControlResultInfo>(payload, control =>
        {
            RequireInstance(item, control.Instance);
            if (!string.Equals(control.Operation, item.Operation, StringComparison.Ordinal) || string.IsNullOrEmpty(control.InvokedSignature)
                || !PlcSimReturnChecks.IsKnown(control.ReturnCheck)
                || control.ReturnCheck != PlcSimReturnChecks.NotInterpreted && control.ReturnValue is null
                || control.ReturnCheck == PlcSimReturnChecks.NotInterpreted && control.ReturnCode is not null)
                throw new JsonException();
        });

        if (value.ReturnCheck != PlcSimReturnChecks.Failed)
            return Succeeded(item, element, warnings);
        return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Failed, element,
            new StructuredOperationFailure(WorkerFailureCategories.PlcSimRuntimeError,
                $"{value.InvokedSignature} returned {value.ReturnCode ?? value.ReturnValue!.Value.GetRawText()} ({value.ReturnType}): "
                + $"S7-PLCSIM Advanced reported that {item.Operation} failed. Check operatingStateAfter; nothing is rolled back."),
            null, null, warnings);
    }

    private static StructuredOperationItem ProjectWrites(PlcSimOperationRequest item, string payload, IReadOnlyList<string> warnings)
    {
        var (value, _, element) = CanonicalJson.NormalizeWorkerPayload<PlcSimTagWriteListInfo>(payload, list =>
        {
            RequireInstance(item, list.Instance);
            if (list.Tags.Count != item.Tags!.Count
                || list.WrittenCount != list.Tags.Count(tag => tag.Status == "written")
                || list.FailedCount != list.Tags.Count(tag => tag.Status == "failed")
                || list.SkippedCount != list.Tags.Count(tag => tag.Status == "skipped")
                || list.WrittenCount + list.FailedCount + list.SkippedCount != list.Tags.Count
                || list.FailedCount > 1)
                throw new JsonException();
            var failedAt = list.Tags.FindIndex(tag => tag.Status == "failed");
            for (var index = 0; index < list.Tags.Count; index++)
            {
                var tag = list.Tags[index];
                if (tag.Name != item.Tags[index].Name || tag.DataType != item.Tags[index].DataType
                    || (failedAt < 0 || index < failedAt) && tag.Status != "written"
                    || failedAt >= 0 && index > failedAt && tag.Status != "skipped"
                    || tag.Status == "failed" && (tag.Error is null || !WorkerFailureCategories.IsKnown(tag.Error.Category)))
                    throw new JsonException();
            }
        });

        if (value.FailedCount == 0)
            return Succeeded(item, element, warnings);

        var failed = value.Tags.First(tag => tag.Status == "failed");
        // A failed item keeps its typed result: which tags were written before the failure matters.
        return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Failed, element,
            new StructuredOperationFailure(failed.Error!.Category,
                $"write_tags wrote {value.WrittenCount} of {value.Tags.Count} tags before '{failed.Name}' failed: {failed.Error.Message} "
                + "Later tags were skipped; earlier writes are not rolled back."),
            null, null, warnings);
    }
}
