namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// The worker's per-binding export count of one PLC block (plc_write planning only; no public operation). TIA Portal
/// V21 is reported by rickgaiser/TiaMcp (not verified on V21) to terminate when it overwrites a block with a textual
/// interface that was exported more than once since the project was opened or the block was last written.
/// </summary>
public static class PlcBlockExportStateMethods
{
    /// <summary>Observe: reads the worker's ledger and resolves the block; exports nothing.</summary>
    public const string GetExportState = "get_block_export_state";

    /// <summary>This worker opened the project (or created it): every export since then is counted.</summary>
    public const string OpenedByThisServer = "opened_by_this_server";

    /// <summary>This worker wrote the block since the binding began: the exports since that write are counted.</summary>
    public const string WrittenByThisServer = "written_by_this_server";

    /// <summary>The project was open before this worker bound it (or a VCI scope exported or imported blocks): the count is unknown.</summary>
    public const string Unknown = "unknown";
}

/// <summary>Payload of <see cref="PlcBlockExportStateMethods.GetExportState"/>; every member is written, nulls included.</summary>
public sealed class PlcBlockExportStateInfo
{
    /// <summary>The PLC software name the block belongs to.</summary>
    public string PlcName { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    /// <summary>Exports of the block by this worker since <see cref="Basis"/>; null when the count is unknown.</summary>
    public int? ExportCount { get; set; }

    /// <summary>One of the <see cref="PlcBlockExportStateMethods"/> basis values.</summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>
    /// Whether the .s7dcl of the last SimaticML bundle this worker exported for the block declares a textual interface
    /// (<c>S7_EditorMode := "[SCL,TextualInterface]"</c>); null when no such bundle was exported in this binding.
    /// </summary>
    public bool? TextualInterface { get; set; }
}
