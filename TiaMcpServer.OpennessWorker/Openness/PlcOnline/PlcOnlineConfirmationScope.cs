using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// The TIA Portal confirmation scope of the Openness online methods, separate from the Siemens online
/// types so tests can run it against the session doubles. go online/offline and station upload answer by
/// the request's <c>portalConfirmations</c> policy like plc_download; compare_to_online always declines (a
/// read never accepts a dialog). The services close the scope (<see cref="PlcDownloadPortalConfirmationLog.CloseInto(System.Action{System.Collections.Generic.List{PlcDownloadPortalConfirmationInfo}, int, bool})"/>)
/// before they copy the evidence; the scope then lingers with the same answer until the next request
/// starts (<see cref="BeginRequest"/>), so a late dialog is never answered by the session-wide Yes.
/// </summary>
internal static class PlcOnlineConfirmationScope
{
    /// <summary>Work left active by a call (go_online's fail-closed legitimation handler), ended when the next request starts.</summary>
    internal static DeferredUntilNextRequest NextRequest { get; } = new();

    public static bool AnswersPortalConfirmations(string? method)
        => method is PlcOnlineMethods.GoOnline or PlcOnlineMethods.GoOffline or PlcOnlineMethods.StationUpload or PlcOnlineMethods.CompareToOnline
            or PlcOnlineMethods.CompareHardwareToOnline;

    /// <summary>True when the scope of <paramref name="method"/> may answer Yes (the policy is accept; read-only still answers No).</summary>
    public static bool AcceptsPortalConfirmations(string? method, PlcOnlineRequestInfo? request)
        => !PlcOnlineMethods.IsOnlineCompare(method)
            && string.Equals(request?.PortalConfirmations, PlcDownloadPolicy.PortalConfirmationPolicies.Accept, StringComparison.Ordinal);

    /// <summary>Opens the scope of one call and attaches it to a new log; dispose the scope as the safety net.</summary>
    public static (PlcDownloadPortalConfirmationLog Log, IDisposable Scope) Open(Session session, string method, PlcOnlineRequestInfo? request,
        Func<object, string?> readText)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (readText is null) throw new ArgumentNullException(nameof(readText));
        var log = new PlcDownloadPortalConfirmationLog();
        var scope = session.AnswerConfirmations(AcceptsPortalConfirmations(method, request),
            (args, answer) => log.Record(args.GetType().FullName, readText(args),
                answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No),
            owner: method);
        log.AttachScope(scope);
        return (log, scope);
    }

    /// <summary>
    /// A scope that answers every TIA Portal confirmation No whatever the access mode (scan_accessible_devices: a
    /// read never accepts a dialog), attached to a new log; dispose the scope as the safety net.
    /// </summary>
    public static (PlcDownloadPortalConfirmationLog Log, IDisposable Scope) OpenDeclining(Session session, string method, Func<object, string?> readText)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (readText is null) throw new ArgumentNullException(nameof(readText));
        var log = new PlcDownloadPortalConfirmationLog();
        var scope = session.AnswerConfirmations(accept: false,
            (args, answer) => log.Record(args.GetType().FullName, readText(args),
                answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No),
            owner: method);
        log.AttachScope(scope);
        return (log, scope);
    }

    /// <summary>
    /// scan_accessible_devices: what to throw when <paramref name="exception"/> escaped a step of the scan
    /// (<paramref name="step"/>, e.g. <c>GetAccessibleDevices()</c>) while the fixed-No scope declined a dialog. The
    /// declined dialogs are part of that failure, so the item fails <c>worker_operation_failed</c> naming both; a Siemens
    /// exception keeps its <c>engineeringError</c>, so a Portal disposal during the request still wins as
    /// <c>portal_lost</c>. Null (rethrow the original) when no dialog was declined, when the exception is already typed
    /// (<see cref="WorkerOperationException"/>) or when it is a non-recoverable Portal loss; the scope is closed
    /// only when it is inspected.
    /// </summary>
    public static WorkerOperationException? ScanFailure(Exception exception, PlcDownloadPortalConfirmationLog? confirmations, string step)
    {
        if (exception is WorkerOperationException || !OpennessExceptions.IsRecoverable(exception)) return null;
        if (PlcAccessibleDeviceProjection.DeclinedConfirmations(confirmations) is not { } declined) return null;
        const string category = WorkerFailureCategories.WorkerOperationFailed;
        return exception is EngineeringException engineering
            ? new WorkerOperationException(category, $"{declined} {step} also failed: {WorkerExecution.FailureMessage(engineering, category)}",
                engineeringError: WorkerExecution.Describe(engineering))
            : new WorkerOperationException(category, $"{declined} {step} also failed: {exception.Message}");
    }

    /// <summary>
    /// Called before every worker request: ends the confirmation scope and the deferred work the previous
    /// call left active and returns their warning lines (late dialogs answered, late prompts refused).
    /// </summary>
    public static IReadOnlyList<string> BeginRequest(Session session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        return session.EndLingeringConfirmations().Concat(NextRequest.CloseAll()).ToList();
    }
}
