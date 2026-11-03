using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

using Project = Siemens.Engineering.Project;

/// <summary>
/// retrieve_project (GitHub issue #4, not live-tested): <c>ProjectComposition.Retrieve(FileInfo, DirectoryInfo)</c> or
/// <c>RetrieveWithUpgrade(FileInfo, DirectoryInfo)</c> (Siemens.Engineering.Base.xml, V21: "Retrieves an archived project" /
/// "Retrieves a project from an archive and upgrades it to the current version", both returning
/// <c>Siemens.Engineering.Project</c>). Neither is in the compile stubs, so the two-argument overload is bound on the live
/// composition by exact name and parameter types (<see cref="LibraryRuntimeBinding"/>; missing: api_member_missing) before the
/// call. The retrieved project is tracked as worker-opened exactly like a created one; the host binds to the path it reports.
/// </summary>
public static partial class ProjectLifecycleService
{
    public static ProjectLifecycleResultInfo RetrieveProject(TiaPortalSession session, string archivePath, string targetDirectory, bool upgrade)
    {
        RequireAbsoluteFile(archivePath, "ArchivePath");
        RequireAbsoluteDirectory(targetDirectory, "TargetDirectory", mustExist: true);

        if (session.ActiveContext is not null)
            session.RequireStandaloneOwner();
        session.EnsureConnected(requestedProjectPath: null);
        if (session.TiaPortal is null)
            throw new InvalidOperationException("No TIA Portal session is connected. Please start TIA Portal and try again.");

        var projects = session.TiaPortal.Projects;
        var name = upgrade ? "RetrieveWithUpgrade" : "Retrieve";
        var method = LibraryRuntimeBinding.Find(projects.GetType(), name, new[] { typeof(FileInfo), typeof(DirectoryInfo) }, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"{error ?? $"This TIA Portal installation exposes no ProjectComposition.{name}(FileInfo, DirectoryInfo)."} Nothing was retrieved.");
        var returned = RuntimeServiceValues.Call(method, projects, new object?[] { new FileInfo(archivePath), new DirectoryInfo(targetDirectory) });
        var project = returned as Project ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            $"ProjectComposition.{name} returned {(returned is null ? "no project" : $"a {returned.GetType().FullName}")}, not the documented "
            + "Siemens.Engineering.Project; the archive may have been retrieved. Inspect the target directory and get_project_status.");

        session.TrackWorkerOpenedProject(project);
        return Result("retrieve_project", project);
    }
}
