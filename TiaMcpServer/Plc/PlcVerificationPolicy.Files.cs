using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// K49/K50 (live V21 2026-11-02): the worker reads back what these items changed, but the verification said "trusted,
/// nothing was read back". export_opcua_xml and export_tag_table: the worker re-reads the placed file and compares its hash
/// with the exported bytes (check file). The four safety access writes: the worker reads
/// IsSafetyOfflineProgramPasswordSet / IsLoggedOnToSafetyOfflineProgram right after the call (check readBack).
/// </summary>
public sealed partial class PlcVerificationPolicy
{
    public const string FileCheck = "file";

    private bool HasFileOrSafetyReadBack(PlcOperationRequest item)
        => _results.ContainsKey(item.OperationId)
            && (item.Operation is PlcBlockEditMethods.ExportOpcUaXml or PlcBlockEditMethods.ExportTagTable
                || item.Operation is PlcSafetyMethods.Login or PlcSafetyMethods.Logoff or PlcSafetyMethods.SetPassword or PlcSafetyMethods.RevokePassword);

    private WriteOperationVerification FileOrSafetyReadBack(PlcOperationRequest item)
        => item.Operation is PlcBlockEditMethods.ExportOpcUaXml or PlcBlockEditMethods.ExportTagTable ? ExportedFileReadBack(item) : SafetyReadBack(item);

    /// <summary>The worker re-reads the file at its place after the move and fails the item when it does not hash to the
    /// exported bytes (<c>TechnologyObjectFiles.Place</c>), so a completed item reports that re-hash.</summary>
    private WriteOperationVerification ExportedFileReadBack(PlcOperationRequest item)
    {
        var result = _results[item.OperationId];
        var reported = Text(result, "fileSha256");
        var path = Text(result, "filePath") ?? item.FilePath;
        var success = !string.IsNullOrEmpty(reported);
        return new(item.OperationId, success, FileCheck, reported, reported, success
            ? $"Operation '{item.OperationId}': the worker re-read '{path}' after placing it; it hashes to the exported bytes (sha256 {reported})."
            : $"Operation '{item.OperationId}': the result carries no file hash. Inspect '{path}'.");
    }

    private WriteOperationVerification SafetyReadBack(PlcOperationRequest item)
    {
        var result = _results[item.OperationId];
        var (field, expected) = item.Operation switch
        {
            PlcSafetyMethods.Login => ("loggedOnAfter", true),
            PlcSafetyMethods.Logoff => ("loggedOnAfter", false),
            PlcSafetyMethods.SetPassword => ("passwordSetAfter", true),
            _ => ("passwordSetAfter", false),
        };
        bool? actual = result.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        var success = actual == expected;
        var shown = actual is null ? "unreadable" : actual.Value ? "true" : "false";
        return new(item.OperationId, success, ReadBackCheck, $"{field}={(expected ? "true" : "false")}", $"{field}={shown}", success
            ? $"Operation '{item.OperationId}': the worker read the safety access state back right after the call ({field} {shown})."
            : $"Operation '{item.OperationId}': the safety access state read back after the call is {field} {shown}. Inspect it with plc_read get_safety_administration.");
    }

    private static string? Text(JsonElement result, string name)
        => result.ValueKind == JsonValueKind.Object && result.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
