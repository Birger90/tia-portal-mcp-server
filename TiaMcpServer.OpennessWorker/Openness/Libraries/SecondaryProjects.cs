using Siemens.Engineering;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;
using SeProject = Siemens.Engineering.Project;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// Read-only secondary (reference) projects next to the bound project (issue #20, not live-tested). V21 documents
/// <c>ProjectComposition.Open(FileInfo, UmacDelegate, ProjectOpenMode)</c>, <c>OpenWithUpgrade(FileInfo, UmacDelegate, ProjectOpenMode)</c>,
/// <c>Retrieve(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)</c>, <c>RetrieveWithUpgrade(…, UmacDelegate, ProjectOpenMode)</c> with
/// <c>ProjectOpenMode.Secondary</c> ("Opens file as a secondary project"), <c>ProjectBase.IsPrimary</c> and <c>Project.Close()</c>. The
/// members are bound at runtime by <see cref="LibrarySignatureBinding"/> (none of the parameter types is on the stubs); the UMAC delegate
/// is passed as null (a UMAC-protected project is refused by TIA Portal; credentials are not in scope). A secondary project is never a
/// binding candidate (<c>TiaPortalSession.ReadOpenContexts</c> offers only <c>IsPrimary == true</c>; false and unreadable are skipped); content tools stay on the bound project.
/// </summary>
internal static class SecondaryProjects
{
    private const string ProjectOpenModeEnum = "Siemens.Engineering.ProjectOpenMode";
    private const string UmacDelegateType = "Siemens.Engineering.UmacDelegate";

    /// <summary><c>ProjectBase.IsPrimary</c> (runtime lookup); null when unreadable.</summary>
    public static bool? IsPrimary(object project)
    {
        try
        {
            var property = project.GetType().GetProperty("IsPrimary");
            return property?.GetValue(project) as bool?;
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is EngineeringException inner && OpennessExceptions.IsRecoverable(inner))
        {
            return null;
        }
    }

    /// <summary>Every project of <c>TiaPortal.Projects</c> whose <c>IsPrimary</c> reads false.</summary>
    public static IReadOnlyList<(SeProject Project, SecondaryProjectInfo Info)> List(TiaPortal portal, List<string> unreadable)
    {
        var result = new List<(SeProject, SecondaryProjectInfo)>();
        foreach (var project in portal.Projects)
        {
            var primary = IsPrimary(project);
            if (primary is null) unreadable.Add("ProjectBase.IsPrimary");
            if (primary != false) continue;
            result.Add((project, Describe(project, primary)));
        }

        return result;
    }

    public static SecondaryProjectListInfo Read(TiaPortal portal)
    {
        var info = new SecondaryProjectListInfo();
        info.Projects.AddRange(List(portal, info.Unreadable).Select(entry => entry.Info));
        info.Unreadable = info.Unreadable.Distinct(StringComparer.Ordinal).ToList();
        return info;
    }

    /// <summary>The open secondary project at <paramref name="path"/> (compared by full path, case-insensitive).</summary>
    public static SeProject Find(TiaPortal portal, string? path, string field)
    {
        if (LibraryPolicy.FilePathError(path) is { } error) throw LibraryResolver.Validation($"{field} {error}");
        var wanted = new FileInfo(path!).FullName;
        var matches = List(portal, new List<string>()).Where(entry => LibraryPolicy.SamePath(entry.Info.Path, wanted)).ToList();
        return matches.Count switch
        {
            1 => matches[0].Project,
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No secondary project with the path '{path}' is open (list_secondary_projects)."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{matches.Count} secondary projects have the path '{path}'."),
        };
    }

    /// <summary>The bound project's own path must not be opened again as a secondary project.</summary>
    public static void RequireNotBound(ProjectBase bound, string path)
    {
        var boundPath = (bound as SeProject)?.Path?.FullName;
        if (boundPath is not null && LibraryPolicy.SamePath(boundPath, new FileInfo(path).FullName))
            throw LibraryResolver.Validation($"'{path}' is the bound project; a secondary project is another project.");
    }

    public static bool IsOpen(TiaPortal portal, string path)
        => portal.Projects.Any(project => LibraryPolicy.SamePath(project.Path?.FullName, new FileInfo(path).FullName));

    public static SeProject Open(TiaPortal portal, string path, bool upgrade, List<string> calls)
    {
        var documented = upgrade ? "ProjectComposition.OpenWithUpgrade(FileInfo, UmacDelegate, ProjectOpenMode)" : "ProjectComposition.Open(FileInfo, UmacDelegate, ProjectOpenMode)";
        var returned = LibraryReflection.InvokeSignature(portal.Projects, upgrade ? "OpenWithUpgrade" : "Open", new[]
        {
            SignatureArgument.Of(new FileInfo(path)), SignatureArgument.Null(UmacDelegateType), SignatureArgument.Enum(ProjectOpenModeEnum, "Secondary"),
        }, documented);
        calls.Add(documented + " with ProjectOpenMode.Secondary");
        return Opened(returned, documented);
    }

    public static SeProject Retrieve(TiaPortal portal, string archive, string targetDirectory, bool upgrade, List<string> calls)
    {
        var documented = upgrade ? "ProjectComposition.RetrieveWithUpgrade(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)"
            : "ProjectComposition.Retrieve(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)";
        var returned = LibraryReflection.InvokeSignature(portal.Projects, upgrade ? "RetrieveWithUpgrade" : "Retrieve", new[]
        {
            SignatureArgument.Of(new FileInfo(archive)), SignatureArgument.Of(new DirectoryInfo(targetDirectory)), SignatureArgument.Null(UmacDelegateType),
            SignatureArgument.Enum(ProjectOpenModeEnum, "Secondary"),
        }, documented);
        calls.Add(documented + " with ProjectOpenMode.Secondary");
        return Opened(returned, documented);
    }

    /// <summary>Evidence: the returned project reads <c>IsPrimary == false</c>; true is postcondition_failed, unreadable is reported.</summary>
    private static SeProject Opened(object? returned, string documented)
    {
        if (returned is not SeProject project)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{documented} returned {(returned is null ? "nothing" : returned.GetType().FullName)}, not a project; inspect list_secondary_projects.");
        var primary = IsPrimary(project);
        if (primary == true)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{documented} returned a project whose IsPrimary reads true; it was not opened as a secondary project. Close it in TIA Portal.");
        // Unreadable: reported as unverified by the caller; it is never a binding candidate either (TiaPortalSession, security review U3).
        if (primary is null)
            Console.Error.WriteLine($"{documented}: the opened project's ProjectBase.IsPrimary could not be read; it is treated as secondary and never offered for binding.");
        return project;
    }

    public static SecondaryProjectInfo Describe(SeProject project, bool? primary = null) => new()
    {
        Path = Safe(() => project.Path?.FullName),
        Name = Safe(() => project.Name),
        IsPrimary = primary ?? IsPrimary(project),
    };

    private static string? Safe(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }
}
