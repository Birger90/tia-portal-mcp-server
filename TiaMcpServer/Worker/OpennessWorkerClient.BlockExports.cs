using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Worker;

/// <summary>The worker's block export ledger (plc_write planning for the V21 textual-interface pitfall).</summary>
public partial class OpennessWorkerClient
{
    /// <summary>Reads the export count of one block; exports nothing. Bound-project gates as every content call.</summary>
    public Task<WorkerCallResult> GetBlockExportStateAsync(string blockPath)
        => SendBoundProjectRequestAsync(PlcBlockExportStateMethods.GetExportState, null, request => request.BlockPath = blockPath, "{}");
}
