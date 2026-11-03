using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Safety.Pipeline;

public sealed class TrustedWriteVerifier<TItem>(IReadOnlyList<TItem> items, IWriteVerificationPolicy<TItem> policy)
    where TItem : IOperationBatchItem
{
    public async Task<WriteVerification> VerifyAsync(StructuredOperationBatch batch)
    {
        var outcomes = batch.Operations.Where(o => o.Status is OperationBatchStatus.Succeeded or OperationBatchStatus.Failed)
            .ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var attempted = items.Where(i => outcomes.ContainsKey(i.OperationId)).ToArray();
        var results = new List<WriteOperationVerification>();
        for (var index = 0; index < attempted.Length; index++)
        {
            var item = attempted[index];
            var failure = outcomes[item.OperationId].Failure;
            if (failure is null && SupersededBy(attempted, outcomes, index) is { } later)
            {
                // A failed later item is never "verified there" (K58: its read-back only reports the state read afterwards).
                var evidence = outcomes[later.OperationId].Failure is not null
                    ? policy.HasReadBack(later)
                        ? $"the later operation failed and may have changed the object; its entry reports the state read afterwards, not a verified final state. Inspect it with {policy.ReadTool} before retrying."
                        : $"the later operation failed and may have changed the object; its final state is not verified. Inspect it with {policy.ReadTool} before retrying."
                    : policy.SupersessionEvidence(item, later) ?? (policy.HasReadBack(later)
                        ? "the final state is verified there."
                        : $"the later operation completed without rejection but is not read back, so the object's final state is not verified; inspect it with {policy.ReadTool}.");
                results.Add(new(item.OperationId, true, WriteVerificationChecks.Superseded, null, null,
                    $"Operation '{item.OperationId}': superseded by later operation '{later.OperationId}' on the same object; {evidence}"));
            }
            else if (policy.HasReadBack(item))
            {
                // A failed write may still have committed: read back its observed state either way.
                results.Add(NeverVerifiedWhenFailed(item, failure, await policy.ReadBackAsync(item, failure).ConfigureAwait(false)));
            }
            else if (failure is null)
            {
                results.Add(new(item.OperationId, true, WriteVerificationChecks.Trusted, null, null,
                    policy.WorkerCheckEvidence(item) is { } evidence
                        ? $"Operation '{item.OperationId}': the Openness call completed without rejection; {evidence} No separate verification read followed."
                        : $"Operation '{item.OperationId}': the Openness call completed without rejection; nothing was read back."));
            }
        }

        return new WriteVerification(results.All(r => r.Success), results, null);
    }

    /// <summary>
    /// K55/K58: a failed item's read-back only reports the state read afterwards; it is never success true, whatever the
    /// domain's read-back returned (a read-back that ignores the failure, e.g. a tag re-read of a partially applied item,
    /// would otherwise report "verified" for an item that failed).
    /// </summary>
    internal static WriteOperationVerification NeverVerifiedWhenFailed(TItem item, StructuredOperationFailure? failure, WriteOperationVerification entry)
    {
        if (failure is null || !entry.Success) return entry;
        var prefix = $"Operation '{item.OperationId}' failed with {failure.Category}";
        return entry with
        {
            Success = false,
            Message = entry.Message?.Contains(prefix, StringComparison.Ordinal) == true ? entry.Message
                : $"{prefix}; the state read afterwards is reported, the operation is not verified." + (entry.Message is null ? string.Empty : " " + entry.Message),
        };
    }

    /// <summary>The latest later item that may have changed one of this item's objects, or null.</summary>
    private TItem? SupersededBy(IReadOnlyList<TItem> attempted,
        IReadOnlyDictionary<string, StructuredOperationItem> outcomes, int index)
    {
        var own = policy.ObjectKeys(attempted[index], asLater: false);
        return attempted.Skip(index + 1)
            // A categorized refusal can still carry a partial outcome after setters have run.
            .Where(later => outcomes[later.OperationId].Result is not null
                || outcomes[later.OperationId].Failure is not { } failure || !WorkerFailureCategories.IsPreconditionRefusal(failure.Category))
            .LastOrDefault(later => policy.ObjectKeys(later, asLater: true).Any(k => own.Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal))));
    }

}
