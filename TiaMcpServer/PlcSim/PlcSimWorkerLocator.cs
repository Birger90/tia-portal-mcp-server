using TiaMcpServer.Diagnostics;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcSim;

/// <summary>
/// Resolves the PLCSIM worker executable: the packaged <c>plcsim-worker/</c> folder beside the host
/// first; otherwise a development build output, but only under the nearest ancestor of the host
/// directory that is the repository root (contains <see cref="RepositoryMarker"/>), Release before
/// Debug. Unlike the walk of <see cref="OpennessWorkerLocator"/>, no other parent directory is probed.
/// </summary>
public static class PlcSimWorkerLocator
{
    public const string WorkerExecutableName = "TiaMcpServer.PlcSimWorker.exe";
    public const string WorkerFolderName = "plcsim-worker";

    /// <summary>The file that marks the repository root for the development fallback.</summary>
    public const string RepositoryMarker = "TiaMcpServer.slnx";

    public static WorkerLocationResult Locate(string baseDirectory, IFileSystemService fileSystem)
    {
        var candidates = new List<WorkerCandidate>();
        var packagedPath = Path.Combine(baseDirectory, WorkerFolderName, WorkerExecutableName);
        var packagedExists = fileSystem.FileExists(packagedPath);
        candidates.Add(new WorkerCandidate(packagedPath, packagedExists));
        if (packagedExists)
            return new WorkerLocationResult(true, packagedPath, candidates);

        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!fileSystem.FileExists(Path.Combine(directory.FullName, RepositoryMarker)))
                continue;
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var candidatePath = Path.Combine(directory.FullName, "TiaMcpServer.PlcSimWorker", "bin", configuration, "net48", WorkerExecutableName);
                var exists = fileSystem.FileExists(candidatePath);
                candidates.Add(new WorkerCandidate(candidatePath, exists));
                if (exists)
                    return new WorkerLocationResult(true, candidatePath, candidates);
            }

            break; // Only the nearest repository root.
        }

        return new WorkerLocationResult(false, null, candidates);
    }

    public static string LocateOrThrow(string baseDirectory, IFileSystemService fileSystem)
    {
        var result = Locate(baseDirectory, fileSystem);
        if (result.Found)
            return result.SelectedPath!;
        throw new FileNotFoundException(
            "The PLCSIM worker executable was not found. Build the solution and ensure the plcsim-worker folder is beside the MCP server executable. Inspected: "
            + string.Join("; ", result.Candidates.Select(candidate => candidate.Path)),
            result.Candidates.FirstOrDefault()?.Path ?? WorkerExecutableName);
    }
}
