using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.DeviceData;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Plc;

/// <summary>
/// plc_write download_to_folder, set_plc_master_secret_online and reset_plc_master_secret_online (issue #16, not live-tested).
/// Planned host-side by matching the PLC in the call's inventory; the provider and every member are resolved and bound by the
/// worker. download_to_folder is verified by the files the call wrote (re-hashed by the worker); the online master secret cannot be
/// read back (notVerified, success false, with an honest note). The online operations fire the acknowledge guard
/// <see cref="PlcGuardDefinitions.MasterSecretOnline"/>: read-write confirms every such call, full runs it under policy.
/// </summary>
internal static class PlcDeviceData
{
    public const string Kind = "DeviceData";

    public const string Check = "deviceData";

    public static (PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards) Resolve(PlcOperationRequest item, PlcTagInventoryPlcInfo plc)
    {
        var target = new PlcTargetIdentity(Kind, plc.PlcName, plc.DeviceName, null, null, null, null, null);
        var changes = new List<PlcFieldChange>();
        var guards = new List<FiredGuard>();
        switch (item.Operation)
        {
            case PlcDeviceDataMethods.DownloadToFolder:
                changes.Add(new("directoryPath", null, item.DirectoryPath));
                if (item.RhSystem == true) changes.Add(new("rhSystem", null, "true"));
                if (item.Overwrite == true)
                {
                    changes.Add(new("overwrite", null, "true"));
                    guards.Add(new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId,
                        $"download_to_folder may replace files in '{item.DirectoryPath}' (overwrite:true lets it download into a non-empty directory)."));
                }
                guards.Add(new FiredGuard(PlcGuardDefinitions.DownloadToFolderWritesFiles, item.OperationId,
                    $"download_to_folder writes the download data of PLC '{plc.PlcName}' into '{item.DirectoryPath}'; no device is touched."));
                break;
            case PlcDeviceDataMethods.SetMasterSecretOnline:
                changes.Add(new("passwordVariable", null, item.PasswordVariable));
                guards.Add(new FiredGuard(PlcGuardDefinitions.MasterSecretOnline, item.OperationId,
                    $"set_plc_master_secret_online sets the master secret of the ONLINE PLC '{plc.PlcName}' (a real controller or simulation, connected with plc_tia_online) from the password source '{item.PasswordVariable}'."));
                break;
            case PlcDeviceDataMethods.ResetMasterSecretOnline:
                guards.Add(new FiredGuard(PlcGuardDefinitions.MasterSecretOnline, item.OperationId,
                    $"reset_plc_master_secret_online deletes the master secret of the ONLINE PLC '{plc.PlcName}' (a real controller or simulation, connected with plc_tia_online); protected confidential data of the PLC may become unusable."));
                break;
            case PlcDeviceDataMethods.LoadSnapshotAsActualValues:
            case PlcDeviceDataMethods.LoadStartValuesAsActualValues:
                changes.Add(new("blockPath", null, item.BlockPath));
                guards.Add(new FiredGuard(PlcGuardDefinitions.LoadsActualValuesOnline, item.OperationId,
                    $"{item.Operation} overwrites the actual values of DB '{item.BlockPath}' in the ONLINE PLC '{plc.PlcName}' (a real controller or simulation, connected with plc_tia_online) with the DB's "
                    + (item.Operation == PlcDeviceDataMethods.LoadSnapshotAsActualValues ? "stored snapshot (plc_read get_db_snapshot shows it)." : "start values (the offline project's StartValue of each member).")));
                break;
        }

        return (new PlcWriteEffect(item.Operation, target, changes, item.DirectoryPath, null, null, null), guards);
    }

    public static PlcDeviceDataRequestInfo ToRequest(PlcOperationRequest op) => new()
    {
        Operation = op.Operation,
        PlcName = PlcDeviceDataMethods.IsActualValues(op.Operation) && op.BlockPath is not null ? Contracts.Block.BlockAddress.Parse(op.BlockPath).PlcName : op.PlcName, DirectoryPath = op.DirectoryPath, RhSystem = op.RhSystem == true,
        Overwrite = op.Operation == PlcDeviceDataMethods.DownloadToFolder && op.Overwrite == true,
        PasswordVariable = op.PasswordVariable,
        BlockPath = PlcDeviceDataMethods.IsActualValues(op.Operation) ? op.BlockPath : null,
        PortalConfirmations = op.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline,
    };

    /// <exception cref="JsonException">The payload does not describe the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest item, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcDeviceDataResultInfo>(payload, result =>
        {
            if (result.Operation != item.Operation || string.IsNullOrWhiteSpace(result.PlcName) || string.IsNullOrWhiteSpace(result.Call)
                || result.Files is null || result.ConfigurationsSeen is null || result.PortalConfirmations is null
                || result.PortalConfirmationsObserved < result.PortalConfirmations.Count || result.Overwrite && item.Overwrite != true)
                throw new JsonException("The device data result does not describe the request.");
            StandalonePayloadContract.RejectNullElements(result.Files);
            StandalonePayloadContract.RejectNullElements(result.PortalConfirmations);
            // A declined TIA Portal dialog fails the item in the worker; a returned result never carries one (F6).
            if (PlcDeviceDataPolicy.Declined(item.Operation, result.PortalConfirmations, result.PortalConfirmationsObserved) is not null)
                throw new JsonException("A device data result with a declined TIA Portal dialog.");
            if (item.Operation == PlcDeviceDataMethods.DownloadToFolder
                && (result.Evidence != "verified" || result.Files.Count == 0 || result.Files.Any(f => f.Sha256 is not { Length: 64 })))
                throw new JsonException("A folder download result without its files.");
            if (PlcDeviceDataMethods.IsOnline(item.Operation) && (result.Evidence != "notReadBack" || result.OnlineState != "Online"))
                throw new JsonException("An online result without its online state.");
            if (PlcDeviceDataMethods.IsActualValues(item.Operation) != (result.BlockPath is not null))
                throw new JsonException("An actual-values result without its DB, or another result with one.");
            if (item.Operation == PlcDeviceDataMethods.SetMasterSecretOnline && result.PasswordSource != item.PasswordVariable)
                throw new JsonException("The master secret result names another password source.");
        }).Element;

    public static WriteOperationVerification Verify(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        if (failure is not null)
            return new(item.OperationId, false, Check, null, null, PlcDeviceDataMethods.IsActualValues(item.Operation)
                ? $"Operation '{item.OperationId}' failed with {failure.Category}; actual values of DB '{item.BlockPath}' in the online PLC may or may not have changed. Inspect the PLC before retrying."
                : PlcDeviceDataMethods.IsOnline(item.Operation)
                ? $"Operation '{item.OperationId}' failed with {failure.Category}; the online PLC's master secret may or may not have changed. Inspect the PLC before retrying."
                : $"Operation '{item.OperationId}' failed with {failure.Category}; files may already have been written to '{item.DirectoryPath}'.");
        if (!results.TryGetValue(item.OperationId, out var element))
            return new(item.OperationId, false, Check, null, null, $"Operation '{item.OperationId}': the worker reported no evidence.");
        var result = CanonicalJson.Deserialize<PlcDeviceDataResultInfo>(element.GetRawText());
        return result.Evidence == "verified"
            ? new(item.OperationId, true, Check, null, null,
                $"Operation '{item.OperationId}': {result.Call} wrote {result.Files.Count} file(s) into '{result.DirectoryPath}', re-read and hashed by the worker.")
            : new(item.OperationId, false, Check, null, null,
                $"Operation '{item.OperationId}': {result.Call} returned on the online PLC (state {result.OnlineState}), but nothing documented reads it back: {result.Note}");
    }

    public static IReadOnlyList<string> ObjectKeys(PlcOperationRequest item, string scope)
        => item.Operation == PlcDeviceDataMethods.DownloadToFolder
            ? new[] { $"{scope}downloadfolder\n{item.DirectoryPath}" }
            : PlcDeviceDataMethods.IsActualValues(item.Operation) ? new[] { $"{scope}online\nactualvalues\n{item.BlockPath}" }
            : new[] { $"{scope}online\nmastersecret" };
}
