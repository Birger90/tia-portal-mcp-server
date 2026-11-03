using Siemens.Engineering;
using TiaMcpServer.OpennessWorker.Openness.Project;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// Roadmap row 21: the Portal listing behind <c>bind_project list_portals</c> reads <c>GetProcesses()</c> and each
/// process's id, path, mode and attached sessions without <c>Attach()</c> (which raises the Openness access dialog).
/// </summary>
[Collection("Portal session boundary")]
public sealed class TiaPortalProcessListingTests
{
    [Fact]
    public void Listing_CountsOtherAttachedClients_WithoutAttaching()
    {
        var previous = TiaPortal.Processes.ToArray();
        try
        {
            TiaPortal.Processes.Clear();
            var shared = new TiaPortalProcess { Id = 101 };
            shared.AttachedSessions.Add(new Siemens.Engineering.TiaPortalSession { ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id });
            shared.AttachedSessions.Add(new Siemens.Engineering.TiaPortalSession { ProcessId = 9001 });
            var alone = new TiaPortalProcess { Id = 102, Mode = TiaPortalMode.WithoutUserInterface };
            TiaPortal.Processes.AddRange(new[] { alone, shared });

            var infos = TiaPortalTargetSelector.ToProcessInfos(
                TiaPortalProcessInventory.Read().Select(entry => entry.Candidate).ToList(), attachedPid: null);

            Assert.Equal(new int?[] { 1, 0 }, infos.Select(info => info.OtherClientCount));
            Assert.Equal(new[] { 101, 102 }, infos.Select(info => info.ProcessId));
            Assert.False(infos[1].HasUserInterface);
            Assert.All(TiaPortal.Processes, process => Assert.Equal(0, process.AttachCalls));
        }
        finally
        {
            TiaPortal.Processes.Clear();
            TiaPortal.Processes.AddRange(previous);
        }
    }
}
