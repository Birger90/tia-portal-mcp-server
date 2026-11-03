using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>Exactly one CLR result type per library operation; a payload that does not decode as it is a protocol_error.</summary>
public static class LibraryPayloadContract
{
    public const string ContractVersion = "1.0";

    internal static readonly IReadOnlyDictionary<string, Type> ResultTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        [LibraryMethods.ListLibraries] = typeof(LibraryListInfo),
        [LibraryMethods.ListGlobalLibraryInfos] = typeof(GlobalLibraryDescriptorListInfo),
        [LibraryMethods.GetLibraryTree] = typeof(LibraryTreeInfo),
        [LibraryMethods.GetLibraryType] = typeof(LibraryTypeDetailInfo),
        [LibraryMethods.FindTypeInstances] = typeof(LibraryTypeInstancesInfo),
        [LibraryMethods.GetInstanceInfo] = typeof(LibraryInstanceLinkInfo),
        [LibraryMethods.UpdateCheck] = typeof(LibraryUpdateCheckInfo),
        [LibraryMethods.CompareLibraries] = typeof(LibraryCompareInfo),
        [LibraryMethods.GetSupportedExportFormats] = typeof(LibraryExportFormatsInfo),
        [LibraryMethods.ListSecondaryProjects] = typeof(SecondaryProjectListInfo),
        [LibraryMethods.ListPortalSettings] = typeof(PortalSettingsInfo),
        [LibraryMethods.CompareTypes] = typeof(LibraryDetailedCompareInfo),
        [LibraryMethods.CompareTypeVersions] = typeof(LibraryDetailedCompareInfo),
        [LibraryMethods.CompareMasterCopies] = typeof(LibraryDetailedCompareInfo),
    };

    private static readonly MethodInfo NormalizeMethod = typeof(LibraryPayloadContract).GetMethod(nameof(Normalize), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static StructuredOperationItem ProjectRead(LibraryOperationRequest item, WorkerCallResult result)
        => ProjectRead(item, result, Console.Error.WriteLine);

    internal static StructuredOperationItem ProjectRead(LibraryOperationRequest item, WorkerCallResult result, Action<string> writeDiagnostic)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        if (!result.Success)
        {
            var failed = StructuredOperationItem.Failed(item, result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                result.Error ?? "The library read failed.", warnings);
            return result.EngineeringError is { } error ? failed with { Failure = failed.Failure! with { EngineeringError = error } } : failed;
        }
        try
        {
            if (!ResultTypes.TryGetValue(item.Operation, out var type)) throw new JsonException();
            var normalize = (Func<string, JsonElement>)NormalizeMethod.MakeGenericMethod(type).CreateDelegate(typeof(Func<string, JsonElement>));
            return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, normalize(result.Payload), null, null, null, warnings);
        }
        catch (JsonException)
        {
            StructuredPayloadProjection.TryWriteProtocolDiagnostic(item.Operation, writeDiagnostic);
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }
    }

    /// <summary>
    /// One write item's typed outcome; Siemens failures keep their classification and engineeringError. The outcome
    /// carries the item's TIA Portal confirmation evidence (plc_download's rules: the policy is the requested one, the
    /// list is capped and consistent with the count, every answer is the policy's); evidence that breaks them is a
    /// protocol_error. Declined confirmations fail the item with the outcome kept; accepted ones add a warning. A failed
    /// worker call may carry the outcome collected until the failure (<see cref="WorkerCallResult.PlcDownloadOutcome"/>).
    /// </summary>
    public static StructuredOperationItem ProjectWrite(LibraryOperationRequest item, WorkerCallResult result)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        var policy = LibraryCatalog.PortalConfirmationPolicy(item);
        // The pipeline copies engineeringError onto a failed write item (WriteRun.WithEngineeringError).
        if (!result.Success)
        {
            var category = result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
            var failed = StructuredOperationItem.Failed(item, category, result.Error ?? "The library write failed.", warnings);
            if (result.PlcDownloadOutcome is null) return failed;
            try
            {
                return failed with { Result = CanonicalJson.ToElement(DecodeWrite(item, result.PlcDownloadOutcome, policy)) };
            }
            catch (JsonException)
            {
                return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                    $"The worker reported '{category}' for '{item.Operation}' with evidence that did not match its declared result contract; the evidence was rejected.",
                    warnings);
            }
        }

        LibraryWriteResultInfo outcome;
        try
        {
            outcome = DecodeWrite(item, result.Payload, policy);
        }
        catch (JsonException)
        {
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                $"The worker outcome for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }

        var element = CanonicalJson.ToElement(outcome);
        if (DeclinedConfirmations(item.Operation, outcome) is { } declined)
            return new(item.OperationId, item.Operation, OperationBatchStatus.Failed, element, declined, null, null, warnings);
        return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null,
            AcceptedConfirmationsWarning(item.Operation, outcome) is { } accepted ? warnings.Append(accepted).ToArray() : warnings);
    }

    private static LibraryWriteResultInfo DecodeWrite(LibraryOperationRequest item, string payload, string policy)
    {
        var outcome = CanonicalJson.DeserializeWorkerPayload<LibraryWriteResultInfo>(payload);
        if (outcome is null || outcome.Operation != item.Operation || outcome.Created is null || outcome.Created.Any(c => c is null))
            throw new JsonException();
        PlcOnlinePayloadContract.ValidatePortalConfirmations(policy, outcome.PortalConfirmationPolicy, outcome.PortalConfirmations,
            outcome.PortalConfirmationsObserved, outcome.PortalConfirmationsComplete);
        return outcome;
    }

    /// <summary>plc_download's rule: dialogs answered No fail the item (the outcome is kept); null when none was declined.</summary>
    internal static StructuredOperationFailure? DeclinedConfirmations(string operation, LibraryWriteResultInfo outcome)
        => outcome.PortalConfirmationsObserved == 0 || outcome.PortalConfirmationPolicy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? null
            : new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal raised {outcome.PortalConfirmationsObserved} confirmation dialog(s) during {operation} and this tool answered each one No "
                + $"(see this item's result.portalConfirmations{(outcome.PortalConfirmationsComplete ? string.Empty : $"; only the first {outcome.PortalConfirmations.Count} are listed")}). "
                + "The Siemens call returned, but declining may have changed what it did (a declined save prompt during a close can discard changes), "
                + "so the item is not reported as successful. Inspect with library_read before retrying; to answer such dialogs Yes, retry with "
                + "portalConfirmations: \"accept\".");

    internal static string? AcceptedConfirmationsWarning(string operation, LibraryWriteResultInfo outcome)
        => outcome.PortalConfirmationsObserved > 0 && outcome.PortalConfirmationPolicy == PlcDownloadPolicy.PortalConfirmationPolicies.Accept
            ? $"TIA Portal raised {outcome.PortalConfirmationsObserved} confirmation dialog(s) during {operation} and each was answered Yes "
                + "(portalConfirmations: accept); see this item's result.portalConfirmations."
            : null;

    /// <summary>The planning evidence of one item, or null when it does not decode as <see cref="LibraryPlanInfo"/>.</summary>
    public static LibraryPlanInfo? DecodePlan(LibraryOperationRequest item, string payload)
    {
        try
        {
            var plan = CanonicalJson.DeserializeWorkerPayload<LibraryPlanInfo>(payload);
            return plan is null || plan.Operation != item.Operation || plan.Consequences is null || plan.Consequences.Any(c => c is null)
                || string.IsNullOrWhiteSpace(plan.Target)
                // The resolved setting belongs to set_portal_setting only, and it must be there (security review F2).
                || (plan.Setting is not null) != (item.Operation == LibraryMethods.SetPortalSetting)
                || plan.Setting is { } setting && (setting.Unreadable is null || setting.FolderPath is null) ? null : plan;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement Normalize<T>(string payload) => CanonicalJson.NormalizeWorkerPayload<T>(payload).Element;
}
