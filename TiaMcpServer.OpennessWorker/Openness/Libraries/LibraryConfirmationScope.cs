using Siemens.Engineering;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// The TIA Portal confirmation scope of one <c>library_write</c> item, separate from the Siemens library types so
/// tests can run it against the session doubles. Like plc_download: the item's <c>portalConfirmations</c> policy
/// answers every dialog (No by default, Yes for accept in writable modes) and the log records it; the service closes
/// the scope before it copies the evidence, and the scope then lingers with the same answer until the next worker
/// request starts (<c>PlcOnlineConfirmationScope.BeginRequest</c>), so a late dialog is never answered
/// by the session-wide Yes and is reported as a warning on that next response.
/// </summary>
internal static class LibraryConfirmationScope
{
    /// <summary>True when the item's scope may answer Yes (the policy is accept; read-only still answers No).</summary>
    public static bool AcceptsPortalConfirmations(LibraryRequestInfo? request)
        => string.Equals(request?.PortalConfirmations, PlcDownloadPolicy.PortalConfirmationPolicies.Accept, StringComparison.Ordinal);

    /// <summary>Opens the scope of one item and attaches it to a new log; dispose the scope as the safety net.</summary>
    public static (PlcDownloadPortalConfirmationLog Log, IDisposable Scope) Open(Session session, string method, LibraryRequestInfo? request,
        Func<object, string?> readText)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (readText is null) throw new ArgumentNullException(nameof(readText));
        var log = new PlcDownloadPortalConfirmationLog();
        var scope = session.AnswerConfirmations(AcceptsPortalConfirmations(request),
            (args, answer) => log.Record(args.GetType().FullName, readText(args),
                answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No),
            owner: method);
        log.AttachScope(scope);
        return (log, scope);
    }
}
