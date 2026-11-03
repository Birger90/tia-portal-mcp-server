using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

internal sealed class PortalInventoryEntry
{
    public PortalInventoryEntry(TiaPortalProcess process, TiaPortalProcessCandidate candidate)
    {
        Process = process;
        Candidate = candidate;
    }

    public TiaPortalProcess Process { get; }
    public TiaPortalProcessCandidate Candidate { get; }
}

internal static class TiaPortalProcessInventory
{
    public static IReadOnlyList<PortalInventoryEntry> Read()
        => TiaPortal.GetProcesses().Select(process => new PortalInventoryEntry(process,
            new TiaPortalProcessCandidate(process.Id, TryReadAdvertisedProjectPath(process), TryReadHasUserInterface(process),
                TryReadOtherClientCount(process))))
            .ToList();

    /// <summary>
    /// Attached Openness sessions other than this worker's, read like the open project and the mode without
    /// <c>Attach()</c> (no Openness access prompt; the process handle is never disposed). Null when unreadable.
    /// </summary>
    private static int? TryReadOtherClientCount(TiaPortalProcess process)
    {
        try
        {
            var workerProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            return process.AttachedSessions.Count(session => session.ProcessId != workerProcessId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read the attached clients of TIA Portal PID {process.Id}: {ex.Message}");
            return null;
        }
    }

    private static string? TryReadAdvertisedProjectPath(TiaPortalProcess process)
    {
        try { return process.ProjectPath?.FullName; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read advertised project path for TIA Portal PID {process.Id}: {ex.Message}");
            return null;
        }
    }

    internal static bool TryReadHasUserInterface(TiaPortalProcess process)
    {
        try { return process.Mode == TiaPortalMode.WithUserInterface; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read mode for TIA Portal PID {process.Id}: {ex.Message}");
            return false;
        }
    }
}
