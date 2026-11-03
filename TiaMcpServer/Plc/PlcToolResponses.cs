using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>
/// Result of a get_block_content / get_type_content read. ContentHash is null for withDependencies reads. FailSafe is
/// present (true) only on a get_block_content sd read whose .s7dcl header declares S7_Safety := "TRUE".
/// </summary>
public sealed record PlcContentResult(
    string Format,
    string Content,
    string? ContentHash,
    IReadOnlyList<string> Warnings,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? FailSafe = null);

/// <summary>
/// Result of a completed update_block_logic: the normalized write format and the worker's typed import evidence.
/// <see cref="MainSafetyBlockRoute"/> is present only when the SD document targeted the main safety block of an F-runtime
/// group and went through a temporary F-block and a SimaticML import (route sdViaXmlForMainSafetyBlock, live 2026-11-02);
/// <see cref="ImportOutcome"/> is then the outcome of that SimaticML import onto the main safety block.
/// </summary>
public sealed record PlcBlockImportResult(
    string Format,
    BlockImportOutcomeInfo ImportOutcome,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    TiaMcpServer.Contracts.Plc.Safety.PlcMainSafetyBlockRouteInfo? MainSafetyBlockRoute = null);
