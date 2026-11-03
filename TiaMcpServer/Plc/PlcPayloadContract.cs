using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The only decoder of PLC read worker payloads. Each operation declares exactly one result type;
/// a payload that does not decode is a <c>protocol_error</c> and is never echoed back.
/// </summary>
public static class PlcPayloadContract
{
    public static StructuredOperationItem Project(PlcOperationRequest operation, WorkerCallResult workerResult)
        => Project(operation, workerResult, Console.Error.WriteLine);

    internal static StructuredOperationItem Project(
        PlcOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic)
        => Run(operation, workerResult, writeProtocolDiagnostic, (op, payload, warnings, _) => Decode(op, payload, warnings));

    /// <summary>Projects the result of one plc_write operation; each operation decodes exactly one declared type.</summary>
    public static StructuredOperationItem ProjectWrite(PlcOperationRequest operation, WorkerCallResult workerResult)
        => ProjectWrite(operation, workerResult, Console.Error.WriteLine);

    internal static StructuredOperationItem ProjectWrite(
        PlcOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic)
        => PlcAlarmMethods.IsWriteMethod(operation.Operation)
            ? PlcAlarms.PlcAlarmPayloadContract.ProjectWrite(operation, workerResult, writeProtocolDiagnostic)
            : Run(operation, workerResult, writeProtocolDiagnostic, DecodeWrite);

    private static StructuredOperationItem Run(
        PlcOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic,
        Func<PlcOperationRequest, string, IReadOnlyList<string>, WorkerCallResult, JsonElement> decode)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();
        if (!workerResult.Success)
        {
            var failed = StructuredOperationItem.Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                // K79: a failed safety printout says where documentLayout names come from (no V21 member lists them).
                (workerResult.Error ?? $"PLC operation '{operation.Operation}' failed.")
                    + (operation.Operation == Contracts.Plc.Settings.PlcSafetyPrintoutMethods.GenerateSafetyPrintout
                        && workerResult.FailureCategory is not WorkerFailureCategories.ValidationError and not WorkerFailureCategories.PortalLost
                        ? " " + Contracts.Plc.Settings.PlcSafetyPrintoutNames.LayoutHint : string.Empty),
                warnings,
                workerResult.BlockImportOutcome);
            var technologyEvidence = PlcTechnologyObjectPayloadContract.HasFailureEvidence(operation.Operation);
            var compareEvidence = PlcOnlineMethods.IsOnlineCompare(operation.Operation);
            var routeEvidence = PlcMainSafetyBlockRouteContract.Carries(operation.Operation);
            if (workerResult.PlcDownloadOutcome is not { } evidence
                || !(technologyEvidence || compareEvidence || routeEvidence || PlcBlockEditPayloadContract.HasFailureEvidence(operation.Operation)))
                return failed;
            // A failed move or copy reports where the original is and any recovery file as the item's result; a failed
            // technology object connect, disconnect or parameter write reports its before/after state; a compare_to_online
            // that refused an online legitimation reports the presented configurations (TLS evidence included).
            try
            {
                return failed with
                {
                    // A failed main safety block route reports what it did (temporary block, compile, main block state) as the item's result.
                    Result = technologyEvidence ? PlcTechnologyObjectPayloadContract.DecodeWrite(operation, evidence)
                        : compareEvidence ? PlcOnline.PlcOnlinePayloadContract.NormalizeCompareFailureEvidence(evidence, PlcOperationCatalog.TrustedCertificates(operation), operation.Operation)
                        : routeEvidence ? PlcMainSafetyBlockRouteContract.DecodeElement(evidence)
                        : PlcBlockEditPayloadContract.DecodeFailureEvidence(evidence),
                };
            }
            catch (JsonException)
            {
                StructuredPayloadProjection.TryWriteProtocolDiagnostic(operation.Operation, writeProtocolDiagnostic);
                return failed with
                {
                    Failure = failed.Failure! with
                    {
                        Message = failed.Failure.Message + " The worker's failure evidence did not match its declared contract and was rejected.",
                    },
                };
            }
        }

        try
        {
            return new StructuredOperationItem(
                operation.OperationId,
                operation.Operation,
                OperationBatchStatus.Succeeded,
                decode(operation, workerResult.Payload, warnings, workerResult),
                Failure: null,
                Omission: null,
                SkipReason: null,
                warnings);
        }
        catch (JsonException)
        {
            StructuredPayloadProjection.TryWriteProtocolDiagnostic(operation.Operation, writeProtocolDiagnostic);
            return StructuredOperationItem.Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }
    }

    private static JsonElement Decode(PlcOperationRequest operation, string payload, IReadOnlyList<string> warnings)
        => operation.Operation switch
        {
            "get_block_content" or "get_type_content" => CanonicalJson.ToElement(Content(operation, payload, warnings)),
            "list_tag_tables" => Inventory(payload),
            "list_download_targets" => PlcDownload.PlcDownloadPayloadContract.NormalizeInventory(payload),
            PlcBlockEditMethods.GetBlockInterface or PlcBlockEditMethods.GetTypeInterface or PlcBlockEditMethods.GetDbValues or PlcBlockEditMethods.GetDbSnapshot
                => PlcBlockEditPayloadContract.DecodeRead(operation.Operation, payload),
            var compare when PlcOnlineMethods.IsCompare(compare)
                => PlcOnline.PlcOnlinePayloadContract.NormalizeCompare(payload, operation.Operation, PlcOperationCatalog.TrustedCertificates(operation)),
            PlcOnlineMethods.GetOnlineState => PlcOnline.PlcOnlinePayloadContract.NormalizeOnlineState(payload),
            PlcOnlineMethods.ListStationUploadInterfaces => PlcOnline.PlcOnlinePayloadContract.NormalizeUploadInterfaces(payload),
            PlcOnlineMethods.ListWatchAndForceTables => PlcOnline.PlcOnlinePayloadContract.NormalizeWatchTables(payload),
            PlcOnlineMethods.ReadWatchTable => PlcOnline.PlcOnlinePayloadContract.NormalizeWatchTable(payload),
            PlcOnlineMethods.ReadForceTable => PlcOnline.PlcOnlinePayloadContract.NormalizeForceTable(payload),
            PlcOnlineMethods.GetWatchTableContent or PlcOnlineMethods.GetForceTableContent => CanonicalJson.ToElement(Content(operation, payload, warnings)),
            var method when PlcSourceFolderOperations.IsWorkerRead(method) => PlcSourceFolderOperations.DecodeRead(operation, payload),
            var method when PlcAlarmMethods.IsReadMethod(method) => PlcAlarms.PlcAlarmPayloadContract.DecodeRead(method, payload),
            var technology when TechnologyObjectMethods.IsRead(technology) => PlcTechnologyObjectPayloadContract.DecodeRead(operation, payload, warnings),
            var security when Contracts.Plc.Security.PlcSecurityMethods.IsRead(security) => PlcSecurityPayloadContract.DecodeRead(operation, payload),
            var safety when Contracts.Plc.Safety.PlcSafetyMethods.IsRead(safety) => PlcSafetyContract.DecodeRead(operation, payload),
            var change when Contracts.Plc.ChangeDetection.PlcChangeDetectionMethods.IsRead(change) => PlcChangeDetectionPayloadContract.DecodeRead(operation, payload),
            var protection when Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.IsRead(protection) => PlcBlockProtection.DecodeRead(operation, payload),
            var languages when Contracts.Project.ProjectLanguageMethods.IsRead(languages) => PlcProjectLanguages.DecodeRead(payload),
            var settings when Contracts.Plc.Settings.PlcSettingsCatalog.IsRead(settings) => PlcSettings.DecodeRead(operation, payload),
            var opcUa when Contracts.Plc.OpcUa.PlcOpcUaMethods.IsRead(opcUa) => PlcOpcUa.DecodeRead(operation, payload),
            _ => throw new JsonException($"No declared result contract for PLC operation '{operation.Operation}'."),
        };

    private static readonly IReadOnlySet<string> TagOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_tag_table", "delete_tag_table", "create_tag", "update_tag", "delete_tag",
        "create_user_constant", "update_user_constant", "delete_user_constant",
    };

    private static readonly IReadOnlySet<string> BlockOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_block", "delete_block", "create_block_group", "delete_block_group",
    };

    /// <exception cref="JsonException">The payload does not decode as the operation's one declared type.</exception>
    private static JsonElement DecodeWrite(
        PlcOperationRequest operation,
        string payload,
        IReadOnlyList<string> warnings,
        WorkerCallResult workerResult)
    {
        var name = operation.Operation;
        if (TagOperations.Contains(name))
        {
            return CanonicalJson.NormalizeWorkerPayload<TagMutationResultInfo>(payload).Element;
        }

        if (BlockOperations.Contains(name))
        {
            return CanonicalJson.NormalizeWorkerPayload<BlockMutationResultInfo>(payload).Element;
        }

        if (PlcBlockEditPayloadContract.HandlesWrite(name))
        {
            return PlcBlockEditPayloadContract.DecodeWrite(operation, payload, workerResult);
        }

        if (PlcSourceFolderMethods.IsWrite(name))
        {
            return PlcSourceFolderOperations.DecodeWrite(operation, payload);
        }

        if (TechnologyObjectMethods.IsWrite(name))
        {
            return PlcTechnologyObjectPayloadContract.DecodeWrite(operation, payload);
        }

        if (Contracts.Plc.Security.PlcSecurityMethods.IsWrite(name))
        {
            return PlcSecurityPayloadContract.DecodeWrite(operation, payload);
        }

        if (Contracts.Plc.Safety.PlcSafetyMethods.IsWrite(name))
        {
            return PlcSafetyContract.DecodeWrite(operation, payload);
        }

        if (name == Contracts.Project.ProjectCompilationSettingsMethods.Set)
        {
            return PlcCompilationSettings.DecodeWrite(operation, payload);
        }

        if (Contracts.Plc.Settings.PlcSettingsCatalog.IsWrite(name))
        {
            return PlcSettings.DecodeWrite(operation, payload);
        }

        if (name == Contracts.Plc.Loadable.PlcLoadableMethods.GenerateLoadable)
        {
            return PlcLoadable.DecodeWrite(operation, payload);
        }

        if (Contracts.Plc.BlockProtection.PlcBlockProtectionMethods.IsWrite(name))
        {
            return PlcBlockProtection.DecodeWrite(operation, payload);
        }

        if (Contracts.Project.ProjectLanguageMethods.IsWrite(name))
        {
            return PlcProjectLanguages.DecodeWrite(operation, payload);
        }

        if (Contracts.Plc.DeviceData.PlcDeviceDataMethods.IsWorkerMethod(name))
        {
            return PlcDeviceData.DecodeWrite(operation, payload);
        }

        if (Contracts.Plc.OpcUa.PlcOpcUaMethods.IsWrite(name))
        {
            return PlcOpcUa.DecodeWrite(operation, payload);
        }

        if (PlcOnlineMethods.ContentWriteMethods.Contains(name))
        {
            return PlcOnline.PlcOnlinePayloadContract.NormalizeWatchTableMutation(payload, name);
        }

        switch (name)
        {
            case PlcTagMoveMethods.MoveTag:
                return CanonicalJson.NormalizeWorkerPayload<PlcTagMoveResultInfo>(payload, moved =>
                {
                    if (!PlcNameRules.Comparer.Equals(moved.TagName, operation.Name)
                        || !PlcNameRules.Comparer.Equals(moved.TargetTableName, operation.TargetTableName)
                        || !PlcNameRules.Comparer.Equals(moved.SourceTableName, operation.TableName))
                        throw new JsonException("The moved tag contradicts the request.");
                    StandalonePayloadContract.RejectNullElements(moved.Steps);
                    StandalonePayloadContract.RejectNullElements(moved.NotCopied);
                }).Element;
            case "update_type_content":
                return CanonicalJson.NormalizeWorkerPayload<PlcTypeImportResultInfo>(payload).Element;
            case "update_block_logic":
                // The worker's text ("Import succeeded.") is not forwarded; the typed outcome is the result.
                var outcome = workerResult.BlockImportOutcome ?? throw new JsonException();
                return CanonicalJson.ToElement(new PlcBlockImportResult(
                    PlcFormatNames.Normalize(name, operation.Format), outcome,
                    PlcMainSafetyBlockRouteContract.DecodeOrNull(workerResult.PlcDownloadOutcome)));
            default:
                throw new JsonException($"No declared result contract for PLC write operation '{name}'.");
        }
    }

    /// <exception cref="JsonException">A list or list element of the inventory is null.</exception>
    private static JsonElement Inventory(string payload)
    {
        var (inventory, _, element) = CanonicalJson.NormalizeWorkerPayload<PlcTagInventoryInfo>(payload);
        if (inventory.Messages is null || inventory.Plcs is null) throw new JsonException();
        StandalonePayloadContract.RejectNullElements(inventory.Messages);
        StandalonePayloadContract.RejectNullElements(inventory.Plcs);
        foreach (var plc in inventory.Plcs)
        {
            if (plc.Tables is null) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(plc.Tables);
            foreach (var table in plc.Tables)
            {
                if (table.Tags is null || table.UserConstants is null) throw new JsonException();
                StandalonePayloadContract.RejectNullElements(table.Tags);
                StandalonePayloadContract.RejectNullElements(table.UserConstants);
            }
        }

        return element;
    }

    private static PlcContentResult Content(PlcOperationRequest operation, string payload, IReadOnlyList<string> warnings)
    {
        var format = PlcFormatNames.Normalize(operation.Operation, operation.Format);
        var hash = operation.WithDependencies == true ? null : ContentHashes.Compute(format, payload);
        // V21 pitfalls (roadmap row 22): flag SIMATIC SD import hazards and a textual interface; the content is unchanged.
        if (operation.Operation == "get_block_content" && format is SourceFormatNames.Xml or SourceFormatNames.Sd)
            warnings = warnings.Concat(PlcV21PitfallReadWarnings.For(operation.BlockPath, payload)).ToArray();
        // An sd export of a fail-safe block says so (header S7_Safety := "TRUE"); every write of it follows the safety rule.
        var failSafe = operation.Operation == "get_block_content" && format == SourceFormatNames.Sd
            && Contracts.Plc.Safety.PlcSafetyPolicy.DeclaresSimaticSdSafety(payload) ? true : (bool?)null;
        return new PlcContentResult(format, payload, hash, warnings, failSafe);
    }
}
