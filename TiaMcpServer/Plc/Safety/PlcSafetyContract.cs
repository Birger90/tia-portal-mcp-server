using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Contracts.Plc.Safety;

namespace TiaMcpServer.Plc;

/// <summary>The fail-safe payload contracts: one declared CLR type per operation, decoded through the worker-payload reader.</summary>
internal static class PlcSafetyContract
{
    public static PlcSafetyRequestInfo ToRequest(PlcOperationRequest operation) => new()
    {
        PlcName = operation.PlcName,
        PasswordVariable = PlcSafetyMethods.TakesPassword(operation.Operation) ? operation.PasswordVariable : null,
        RuntimeGroupName = operation.RuntimeGroupName,
        MainSafetyBlockPath = operation.MainSafetyBlockPath,
        MainSafetyInstanceDbPath = operation.MainSafetyInstanceDbPath,
        Offset = operation.Offset,
        Limit = operation.Limit,
    };

    /// <exception cref="JsonException">The payload does not decode as the operation's declared type.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => operation.Operation switch
        {
            PlcSafetyMethods.GetAdministration => CanonicalJson.NormalizeWorkerPayload<PlcSafetyAdministrationInfo>(payload, CheckAdministration).Element,
            PlcSafetyMethods.ListBlocks => CanonicalJson.NormalizeWorkerPayload<PlcSafetyBlockListInfo>(payload).Element,
            _ => throw new JsonException($"No declared result contract for PLC operation '{operation.Operation}'."),
        };

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcSafetyWriteResultInfo"/> or describes another operation.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest operation, string payload)
    {
        var normalized = CanonicalJson.NormalizeWorkerPayload<PlcSafetyWriteResultInfo>(payload);
        if (!string.Equals(normalized.Value.Operation, operation.Operation, StringComparison.Ordinal)) throw new JsonException("The fail-safe result names another operation.");
        // The result carries a variable NAME at most, and only the one the request named.
        if (normalized.Value.PasswordVariable is { } variable && !string.Equals(variable, operation.PasswordVariable, StringComparison.Ordinal))
            throw new JsonException("The fail-safe result names another password variable.");
        return normalized.Element;
    }

    /// <summary>Decodes the administration of one PLC from a get_safety_administration payload (planning).</summary>
    public static PlcSafetyAdministrationInfo Administration(string payload)
    {
        return CanonicalJson.NormalizeWorkerPayload<PlcSafetyAdministrationInfo>(payload, CheckAdministration).Value;
    }

    private static void CheckAdministration(PlcSafetyAdministrationInfo info)
    {
        foreach (var value in info.Plcs.SelectMany(p => p.Access.Concat(p.Settings).Concat(p.BlockNumbers).Concat(p.RuntimeGroups.SelectMany(g => g.Attributes)))
                     .Concat(info.GlobalSettings.Values))
            if (!PlcSafetyValueStates.IsKnown(value.State)) throw new JsonException("Unknown value state.");
    }
}
