using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the project language and project text methods (issue #8); authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse ProjectLanguages(WorkerRequest request)
    {
        var info = request.ProjectLanguages ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the project language request.");
        return WithEngineeringRoot(request, project => request.Method switch
        {
            ProjectLanguageMethods.GetLanguages => Success(ProjectLanguagesService.Read(project)),
            ProjectLanguageMethods.SetLanguages => Success(WithNotifications(ProjectLanguagesService.Set(project, info), (r, n) => r.PortalNotifications = n)),
            ProjectLanguageMethods.ExportTexts => Success(WithNotifications(ProjectLanguagesService.Export(project, info), (r, n) => r.PortalNotifications = n)),
            _ => Success(WithNotifications(ProjectLanguagesService.Import(project, info), (r, n) => r.PortalNotifications = n)),
        });
    }

    /// <summary>K75: the TIA Portal notifications the session handled while this request ran, recorded in the item's result.</summary>
    private static T WithNotifications<T>(T result, Action<T, List<PortalNotificationInfo>> set)
    {
        set(result, _sharedSession.RequestNotifications().ToList());
        return result;
    }
}
