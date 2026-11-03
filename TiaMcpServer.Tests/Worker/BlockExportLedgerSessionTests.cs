using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Project;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>Roadmap row 22: the session resets the block export ledger when its engineering root changes.</summary>
[Collection("Portal session boundary")]
public sealed class BlockExportLedgerSessionTests
{
    [Fact]
    public void WorkerOpenedRoot_StartsKnown_SameRootKeepsCounts_OtherRootIsUnknown()
    {
        var ledger = BlockExportLedger.Shared;
        var path = Path.Combine(Path.GetTempPath(), "ledger-" + Guid.NewGuid().ToString("N") + ".ap21");
        using var session = new TiaPortalSession();
        var opened = new Siemens.Engineering.Project { Path = new FileInfo(path) };

        session.AdoptContext(ActiveProjectContext.ForStandalone(opened, true), path);
        ledger.RecordExport("PLC_1", "Main");
        Assert.Equal(1, ledger.Read("PLC_1", "Main").ExportCount);

        session.AdoptContext(ActiveProjectContext.ForStandalone(opened, false), path);
        Assert.Equal(1, ledger.Read("PLC_1", "Main").ExportCount);

        session.AdoptContext(ActiveProjectContext.ForStandalone(new Siemens.Engineering.Project { Path = new FileInfo(path) }, false), path);
        Assert.Null(ledger.Read("PLC_1", "Main").ExportCount);
    }
}
