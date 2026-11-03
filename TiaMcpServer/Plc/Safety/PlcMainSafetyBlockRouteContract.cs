using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;

namespace TiaMcpServer.Plc;

/// <summary>
/// The host decoder of the main safety block route evidence (<see cref="PlcMainSafetyBlockRouteInfo"/>; live workaround,
/// TIA Portal V21, 2026-11-02). The worker sends it in <c>WorkerResponse.PlcDownloadOutcome</c> (the evidence channel) for
/// update_block_logic, the SD network edits (mutated as update_block_logic format sd) and create_block_from_document, on
/// success (the item result's <c>mainSafetyBlockRoute</c>) and on failure (the failed item's <c>result</c>). Decoded with the
/// strict required-member reader; anything that does not match is a protocol error and is never echoed.
/// </summary>
internal static class PlcMainSafetyBlockRouteContract
{
    /// <summary>The operations whose worker call can take the route.</summary>
    public static bool Carries(string operation)
        => operation is "update_block_logic" or SimaticSdMethods.CreateBlockFromDocument
            || PlcBlockEditMethods.NetworkEdits.Contains(operation, StringComparer.Ordinal);

    /// <exception cref="JsonException">The evidence does not decode as <see cref="PlcMainSafetyBlockRouteInfo"/>.</exception>
    public static PlcMainSafetyBlockRouteInfo? DecodeOrNull(string? evidence) => evidence is null ? null : Decode(evidence);

    /// <exception cref="JsonException">The evidence does not decode as <see cref="PlcMainSafetyBlockRouteInfo"/>.</exception>
    public static PlcMainSafetyBlockRouteInfo Decode(string evidence)
        => CanonicalJson.NormalizeWorkerPayload<PlcMainSafetyBlockRouteInfo>(evidence, Check).Value;

    /// <exception cref="JsonException">The evidence does not decode as <see cref="PlcMainSafetyBlockRouteInfo"/>.</exception>
    public static JsonElement DecodeElement(string evidence)
        => CanonicalJson.NormalizeWorkerPayload<PlcMainSafetyBlockRouteInfo>(evidence, Check).Element;

    /// <summary>Validates <paramref name="info"/> (used on the create_block_from_document result, which carries it in the payload).</summary>
    public static void Check(PlcMainSafetyBlockRouteInfo info)
    {
        if (info.Route != PlcMainSafetyBlockSdRoute.Route) throw new JsonException("Unknown route.");
        if (info.TemporaryBlockName is null || !info.TemporaryBlockName.StartsWith(PlcMainSafetyBlockSdRoute.TemporaryPrefix, StringComparison.Ordinal))
            throw new JsonException("The temporary block name does not carry the route's prefix.");
        if (info.TemporaryBlockState is not (PlcMainSafetyBlockSdRoute.TemporaryStates.NotCreated or PlcMainSafetyBlockSdRoute.TemporaryStates.Deleted
            or PlcMainSafetyBlockSdRoute.TemporaryStates.Left))
            throw new JsonException("Unknown temporary block state.");
        if (info.MainImport is not (PlcMainSafetyBlockSdRoute.MainImports.NotStarted or PlcMainSafetyBlockSdRoute.MainImports.SimaticMl
            or PlcMainSafetyBlockSdRoute.MainImports.Failed))
            throw new JsonException("Unknown main import state.");
        if (info.IdentitySource is not (null or PlcMainSafetyBlockSdRoute.IdentitySources.SimaticMlExport or PlcMainSafetyBlockSdRoute.IdentitySources.LiveAttributes))
            throw new JsonException("Unknown identity source.");
        if (info.RuntimeGroups is null || info.Steps is null || info.Notes is null) throw new JsonException();
        StandalonePayloadContract.RejectNullElements(info.RuntimeGroups);
        StandalonePayloadContract.RejectNullElements(info.Steps);
        StandalonePayloadContract.RejectNullElements(info.Notes);
        if (info.TemporaryCompile is { } compile)
        {
            if (compile.Messages is null) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(compile.Messages);
        }
    }
}
