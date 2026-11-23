using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.ChangeDetection;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Plc;

/// <summary>
/// The worker parameters and declared result types of get_plc_checksums (<see cref="PlcChecksumReadInfo"/>) and
/// get_fingerprints (<see cref="PlcFingerprintReadInfo"/>), decoded through the worker-payload reader (every member required,
/// explicit nulls). A payload that does not decode or contradicts the request is a <c>protocol_error</c> and is never echoed.
/// </summary>
public static class PlcChangeDetectionPayloadContract
{
    public static PlcChangeDetectionRequestInfo ToRequest(PlcOperationRequest op) => new()
    {
        PlcName = op.PlcName,
        BlockPath = op.BlockPath,
        TypePath = op.TypePath,
    };

    /// <exception cref="JsonException">The payload does not decode as the operation's declared type or contradicts the request.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => operation.Operation == PlcChangeDetectionMethods.CheckDownloadReadiness ? DecodeReadiness(operation, payload)
        : operation.Operation == PlcChangeDetectionMethods.GetPlcChecksums
            ? CanonicalJson.NormalizeWorkerPayload<PlcChecksumReadInfo>(payload, read =>
            {
                if (read.Operation != PlcChangeDetectionMethods.GetPlcChecksums || read.Plcs is null || read.Plcs.Count == 0 || string.IsNullOrWhiteSpace(read.Note))
                    throw new JsonException();
                StandalonePayloadContract.RejectNullElements(read.Plcs);
                foreach (var plc in read.Plcs)
                {
                    if (string.IsNullOrWhiteSpace(plc.PlcName)) throw new JsonException();
                    if (operation.PlcName is { } name && !PlcNameRules.Comparer.Equals(plc.PlcName, name) && !PlcNameRules.Comparer.Equals(plc.DeviceName, name))
                        throw new JsonException("A PLC the request did not name was returned.");
                    if (plc.Available != (plc.Unavailable is null) || plc.Available != (plc.Software is not null) || plc.Available != (plc.TextLists is not null))
                        throw new JsonException("The checksum availability contradicts the values.");
                    if (plc.Software is not null) CheckValue(plc.Software);
                    if (plc.TextLists is not null) CheckValue(plc.TextLists);
                }
            }).Element
            : CanonicalJson.NormalizeWorkerPayload<PlcFingerprintReadInfo>(payload, read =>
            {
                var kind = operation.BlockPath is not null ? PlcChangeDetectionNames.ObjectKindBlock : PlcChangeDetectionNames.ObjectKindType;
                if (read.Operation != PlcChangeDetectionMethods.GetFingerprints || read.ObjectKind != kind
                    || read.Path != (operation.BlockPath ?? operation.TypePath) || string.IsNullOrWhiteSpace(read.PlcName) || read.Fingerprints is null)
                    throw new JsonException("The fingerprint result does not describe the request.");
                StandalonePayloadContract.RejectNullElements(read.Fingerprints);
                foreach (var fingerprint in read.Fingerprints)
                {
                    CheckValue(fingerprint.Id);
                    CheckValue(fingerprint.Value);
                }
            }).Element;

    private static JsonElement DecodeReadiness(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcDownloadReadinessReadInfo>(payload, read =>
        {
            if (read.Operation != PlcChangeDetectionMethods.CheckDownloadReadiness || read.Plcs is null || read.Plcs.Count == 0 || string.IsNullOrWhiteSpace(read.Note))
                throw new JsonException();
            StandalonePayloadContract.RejectNullElements(read.Plcs);
            foreach (var plc in read.Plcs)
            {
                if (string.IsNullOrWhiteSpace(plc.PlcName) || plc.Checksums is null || plc.Reasons is null || plc.InconsistentBlocks is null || plc.Messages is null)
                    throw new JsonException();
                if (operation.PlcName is { } name && !PlcNameRules.Comparer.Equals(plc.PlcName, name) && !PlcNameRules.Comparer.Equals(plc.DeviceName, name))
                    throw new JsonException("A PLC the request did not name was returned.");
                if (plc.Verdict is not (PlcDownloadReadiness.Ready or PlcDownloadReadiness.NotReady or PlcDownloadReadiness.Unknown))
                    throw new JsonException("Unknown readiness verdict.");
                if (plc.BlockCount < 0 || plc.InconsistentCount < 0 || plc.UnreadableCount < 0 || plc.InconsistentBlocksOmitted < 0
                    || plc.InconsistentCount + plc.UnreadableCount > plc.BlockCount
                    || plc.InconsistentBlocks.Count + plc.InconsistentBlocksOmitted != plc.InconsistentCount
                    || plc.InconsistentBlocks.Count > PlcDownloadReadiness.MaxListedBlocks)
                    throw new JsonException("The readiness counts contradict each other.");
                if ((plc.Verdict == PlcDownloadReadiness.Ready) != (plc.Reasons.Count == 0))
                    throw new JsonException("The readiness reasons contradict the verdict.");
                if (plc.Checksums.Available != (plc.Checksums.Unavailable is null) || plc.Checksums.Available != (plc.Checksums.Software is not null))
                    throw new JsonException("The checksum availability contradicts the values.");
                if (plc.Checksums.Software is not null) CheckValue(plc.Checksums.Software);
                if (plc.Checksums.TextLists is not null) CheckValue(plc.Checksums.TextLists);
            }
        }).Element;

    private static void CheckValue(RuntimeValueInfo? value)
    {
        if (value is null || !RuntimeValueStates.All.Contains(value.State, StringComparer.Ordinal)) throw new JsonException("Unknown value state.");
        var hasText = value.State is RuntimeValueStates.Value or RuntimeValueStates.Empty;
        if (hasText != (value.Value is not null) || value.State == RuntimeValueStates.Value && value.Value!.Length == 0
            || value.State == RuntimeValueStates.Empty && value.Value!.Length != 0)
            throw new JsonException("The value contradicts its state.");
        if ((value.State is RuntimeValueStates.Unreadable or RuntimeValueStates.Missing) != (value.Error is not null))
            throw new JsonException("The value error contradicts its state.");
    }
}
