using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.PlcAlarms;
using TiaMcpServer.OpennessWorker.Worker;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the PLC alarm, alarm class, text list, supervision and ProDiag methods; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcAlarm(WorkerRequest request)
    {
        var info = request.PlcAlarm ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the PLC alarm request.");
        if (request.Method == PlcAlarmMethods.Plan)
            return WithEngineeringRoot(request, project => Success(PlcAlarmService.Plan(project, info)));

        // Like plc_download and library_write: until the item's evidence is collected every TIA Portal confirmation is
        // answered by its policy instead of the session-wide Yes and recorded. A workbook read always answers No (and fails
        // when it had to); a write answers by its portalConfirmations. The services close the scope before they copy the
        // evidence (it then lingers with the same answer until the next request starts); this using is the safety net.
        if (PlcAlarmMethods.TemporaryExportMethods.Contains(request.Method))
        {
            var (readLog, readScope) = PlcAlarmConfirmationScope.Open(_sharedSession, request.Method, accept: false, PlcDownloadService.ReadConfirmationText);
            using (readScope)
            {
                return WithEngineeringRoot(request, project => Success(PlcAlarmService.ReadWorkbook(project, request.Method, info, readLog)));
            }
        }

        if (!PlcAlarmMethods.IsWriteMethod(request.Method))
        {
            return WithEngineeringRoot(request, project => request.Method switch
            {
                PlcAlarmMethods.ListTextLists => Success(PlcAlarmService.ListTextLists(project, info)),
                PlcAlarmMethods.GetProDiagFb => Success(PlcProDiagService.GetFb(project, info)),
                PlcAlarmMethods.GetProDiagAssignment => Success(PlcProDiagService.GetAssignment(project, info)),
                _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported PLC alarm method '{request.Method}'."),
            });
        }

        var accept = string.Equals(info.PortalConfirmations, PlcDownloadPolicy.PortalConfirmationPolicies.Accept, StringComparison.Ordinal);
        var (log, scope) = PlcAlarmConfirmationScope.Open(_sharedSession, request.Method, accept, PlcDownloadService.ReadConfirmationText);
        using (scope)
        {
            // The active context: cell updates and imports take TiaPortal.ExclusiveAccess on the Portal of the bound project.
            return WithActiveContext(request, session =>
                Success(PlcAlarmService.Write(session.TiaPortal!, session.EngineeringRoot!, request.Method, info, log, () => _sharedSession.PortalDisposalRevision)));
        }
    }
}

/// <summary>
/// The TIA Portal confirmation scope of one alarm item, like <c>LibraryConfirmationScope</c>: the item's policy answers every
/// dialog (No by default and always for reads, Yes for accept in writable modes) and the log records it.
/// </summary>
internal static class PlcAlarmConfirmationScope
{
    public static (PlcDownloadPortalConfirmationLog Log, IDisposable Scope) Open(Session session, string method, bool accept, Func<object, string?> readText)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        var log = new PlcDownloadPortalConfirmationLog();
        var scope = session.AnswerConfirmations(accept,
            (args, answer) => log.Record(args.GetType().FullName, readText(args),
                answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No),
            owner: method);
        log.AttachScope(scope);
        return (log, scope);
    }
}
