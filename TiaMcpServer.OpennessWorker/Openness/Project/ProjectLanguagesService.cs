using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

/// <summary>
/// Project languages and project texts (issue #8, not live-tested). Everything is reached from the documented
/// <c>ProjectBase.LanguageSettings</c>, <c>TextCategories</c>, <c>ExportProjectTexts</c> and <c>ImportProjectTexts</c>, bound at runtime
/// by the Siemens-free <see cref="ProjectLanguagesRun"/> (no stub member was added). The devices of a filtered text export are the
/// project's <c>Devices</c> and those in its device groups, matched by exact name (case-insensitive); an unknown name is
/// target_not_found before anything is written.
/// </summary>
internal static class ProjectLanguagesService
{
    private static bool Recoverable(Exception exception)
        => exception is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering)
            && engineering is not EngineeringObjectDisposedException;

    public static ProjectLanguagesInfo Read(ProjectBase project) => Bind(() => ProjectLanguagesRun.Read(Target(project), Recoverable));

    public static ProjectLanguagesWriteResultInfo Set(ProjectBase project, ProjectLanguagesRequestInfo request)
        => Bind(() => ProjectLanguagesRun.Set(Target(project), request, Recoverable));

    public static ProjectTextsExportResultInfo Export(ProjectBase project, ProjectLanguagesRequestInfo request)
        => Bind(() => ProjectLanguagesRun.Export(Target(project), request, DateTime.UtcNow, Recoverable));

    public static ProjectTextsImportResultInfo Import(ProjectBase project, ProjectLanguagesRequestInfo request)
        => Bind(() => ProjectLanguagesRun.Import(Target(project), request, Recoverable));

    private static ProjectLanguageTarget Target(ProjectBase project) => new(project, names => Devices(project, names));

    private static T Bind<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }

    private static IReadOnlyList<(string Name, object Device)> Devices(ProjectBase project, IReadOnlyList<string>? names)
    {
        var all = new List<(string Name, object Device)>();
        foreach (Device device in project.Devices) all.Add((device.Name, device));
        void Walk(DeviceUserGroupComposition groups)
        {
            foreach (DeviceUserGroup group in groups)
            {
                foreach (Device device in group.Devices) all.Add((device.Name, device));
                Walk(group.Groups);
            }
        }

        Walk(project.DeviceGroups);
        if (names is null) return all;
        return names.Select(name =>
        {
            var matches = all.Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count switch
            {
                1 => matches[0],
                0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The project has no device named '{name}'. Nothing was written."),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{matches.Count} devices are named '{name}'. Nothing was written."),
            };
        }).ToList();
    }
}
