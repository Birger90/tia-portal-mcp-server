using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

/// <summary>
/// retrieve_project (issue #4): binds to the project the worker actually retrieved (its ResolvedProjectPath), exactly like
/// create_project; an existing binding was already refused without forceRebind by the lifecycle binding strategy.
/// </summary>
public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> RetrieveProjectAsync(string archivePath, string targetDirectory, bool upgrade)
        => ExecuteSerializedBindingOperationAsync(() => RetrieveProjectCoreAsync(archivePath, targetDirectory, upgrade));

    private async Task<WorkerCallResult> RetrieveProjectCoreAsync(string archivePath, string targetDirectory, bool upgrade)
    {
        var currentBinding = _projectSessionBinding.CaptureSnapshot();
        var bindingBeforeCall = _bindingOperationContext.Value?.PinnedBinding ?? currentBinding;
        if (_bindingOperationContext.Value?.PinnedBinding is not null && !bindingBeforeCall.SameBinding(currentBinding))
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                "The worker/Portal/project binding changed after preview. No operation was performed; request a fresh preview.");

        var result = await InvokeWorkerAsync(new WorkerRequest
        {
            Method = "retrieve_project",
            ExpectedSessionIdentity = bindingBeforeCall.ToWorkerIdentity(),
            RetrieveArchivePath = archivePath,
            TargetDirectory = targetDirectory,
            RetrieveWithUpgrade = upgrade,
        }).ConfigureAwait(false);
        if (!result.Success) return result;

        // A retrieved project is a fresh binding target, like a created one.
        result = ApplyBindingTransition(BindingTransition.BindResolvedPath, result, bindingBeforeCall, bindForceRebind: true);
        if (!result.Success) return result;
        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }
}
