using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

/// <summary>
/// Owner decision 2026-11-09 (idea from rickgaiser/TiaMcp 38e63db, AGPL-3.0; reimplemented): when TIA Portal closed and
/// reopened the bound project (a Multiuser check-in or refresh, another Openness client, a restarted Portal), a plain read that
/// lost the binding re-binds to the <b>same</b> already-open project and runs <b>once</b> more. It never opens a project (the
/// re-bind is <c>select_portal_project</c>, which only adopts an open one), never re-binds to another path, and never repeats a
/// write, lifecycle, online, simulation or session-selection call: those still invalidate the binding without replay.
/// </summary>
internal static class ReadReattachPolicy
{
    public const string ReattachedWarning =
        "TIA Portal had closed and reopened the project: the server re-bound to the same open project '{0}' and ran this read once more.";

    public const string ReattachFailedWarning =
        "The binding was lost during this read and re-binding to '{0}' failed ({1}); call bind_project once the project is open again.";

    /// <summary>
    /// True when <paramref name="result"/> of <paramref name="method"/> should be followed by one re-bind and one retry: the
    /// method is an Observe or TemporaryExport content read of the bound project (not a status or identity probe), the call ran
    /// outside any pinned or serialized operation, the binding was verified before and is no longer verified after a request
    /// that reached the worker and failed with <c>portal_lost</c>. An identity divergence (<c>binding_conflict</c>: for example a
    /// local session replaced by another owner at the same path) still needs an explicit <c>bind_project</c>.
    /// </summary>
    public static bool ShouldReattach(string method, ProjectBindingSnapshot before, ProjectBindingSnapshot after, WorkerCallResult result, bool insideOperation)
        => !insideOperation
            && !result.Success
            && result.FailureCategory == WorkerFailureCategories.PortalLost
            && result.DispatchState == WorkerDispatchState.Sent
            && ProjectCapabilityCatalog.RequiresBoundProject(method)
            && WorkerTransportFailureGuidance.IsSafeRead(method)
            && OperationPolicyCatalog.GetCapability(method) is OperationCapability.Observe or OperationCapability.TemporaryExport
            && before.IsVerified
            && !string.IsNullOrWhiteSpace(before.ProjectPath)
            && !after.IsVerified;

    /// <summary>True when the re-bind ended verified on exactly the path that was bound before (case-insensitive, canonical).</summary>
    public static bool SameProject(ProjectBindingSnapshot before, ProjectBindingSnapshot after)
        => after.IsVerified && string.Equals(before.ProjectPath, after.ProjectPath, StringComparison.OrdinalIgnoreCase);
}
