using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Loadable;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// plc_write generate_loadable (issue #26, not live-tested): <c>LoadableProvider.GenerateLoadable(FileInfo, IEnumerable&lt;PlcBlock&gt;
/// | IEnumerable&lt;PlcUnit&gt;, TargetOption)</c> of one PLC, written to a caller file. Planned host-side without a planning read: the
/// PLC is matched in the call's tag inventory (plcName, else the PLC the block paths name, else the only PLC); the blocks or units,
/// the provider, the overload and the TargetOption member are resolved and bound by the worker before the call, which fails closed
/// (target_not_found, unsupported_capability, api_member_missing). The file follows the alarm exports' caller-file rules; the
/// worker re-reads it at its place and reports its SHA-256 (verification check <c>file</c>).
/// </summary>
internal static class PlcLoadable
{
    public const string Kind = "Loadable";

    public static (PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards) Resolve(PlcOperationRequest item, PlcTagInventoryPlcInfo plc)
    {
        var target = new PlcTargetIdentity(Kind, plc.PlcName, plc.DeviceName, null, null, null, null, null);
        var objects = item.BlockPaths ?? item.UnitNames ?? Array.Empty<string>();
        var changes = new[]
        {
            new PlcFieldChange(item.BlockPaths is not null ? "blockPaths" : "unitNames", null, string.Join(", ", objects)),
            new PlcFieldChange("target", null, item.Target),
        };
        var guards = item.Overwrite == true
            ? new[] { new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId, $"generate_loadable replaces '{item.FilePath}' if it exists (overwrite:true).") }
            : Array.Empty<FiredGuard>();
        return (new PlcWriteEffect(item.Operation, target, changes, item.FilePath, null, null, null), guards);
    }

    /// <summary>The PLC selector of the item: plcName, else the PLC of the first block path, else none (the only PLC).</summary>
    public static string? Selector(PlcOperationRequest item)
        => item.PlcName ?? (item.BlockPaths is { Count: > 0 } paths ? Contracts.Block.BlockAddress.Parse(paths[0]).PlcName : null);

    public static PlcLoadableRequestInfo ToRequest(PlcOperationRequest item) => new()
    {
        PlcName = item.PlcName,
        BlockPaths = item.BlockPaths?.ToList(),
        UnitNames = item.UnitNames?.ToList(),
        Target = item.Target ?? string.Empty,
        FilePath = item.FilePath ?? string.Empty,
        Overwrite = item.Overwrite == true,
    };

    /// <exception cref="JsonException">The payload does not describe the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest item, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcLoadableResultInfo>(payload, result =>
        {
            var overload = item.BlockPaths is not null ? PlcLoadableNames.OverloadBlocks : PlcLoadableNames.OverloadUnits;
            var requested = item.BlockPaths ?? item.UnitNames ?? Array.Empty<string>();
            if (result.Operation != PlcLoadableMethods.GenerateLoadable || result.Overload != overload || result.Target != item.Target
                || result.FilePath != item.FilePath || string.IsNullOrWhiteSpace(result.PlcName) || string.IsNullOrWhiteSpace(result.Signature)
                || result.Objects is null || result.Objects.Count != requested.Count || result.Objects.Any(string.IsNullOrWhiteSpace)
                || result.Bytes < 0 || result.Sha256 is null || result.Sha256.Length != 64 || result.Messages is null || result.Messages.Any(m => m is null)
                || result.Overwritten && item.Overwrite != true)
                throw new JsonException("The generate_loadable result does not describe the request.");
        }).Element;

    /// <summary>The worker re-read the file at its place; the verification reports that hash.</summary>
    public static WriteOperationVerification Verify(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        if (failure is not null)
            return new(item.OperationId, true, PlcVerificationPolicy.FileCheck, null, null,
                $"Operation '{item.OperationId}' failed with {failure.Category}; inspect '{item.FilePath}' (a failed call leaves no file of this server at the target).");
        if (!results.TryGetValue(item.OperationId, out var element))
            return new(item.OperationId, false, PlcVerificationPolicy.FileCheck, null, null, $"Operation '{item.OperationId}': the worker reported no file.");
        var result = CanonicalJson.Deserialize<PlcLoadableResultInfo>(element.GetRawText());
        return new(item.OperationId, true, PlcVerificationPolicy.FileCheck, result.Sha256, result.Sha256,
            $"Operation '{item.OperationId}': the worker re-read '{result.FilePath}' after placing it ({result.Bytes} bytes, sha256 {result.Sha256}).");
    }
}
