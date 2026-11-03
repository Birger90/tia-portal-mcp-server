using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>Host and worker authorization, capability delivery and evidence survival for the download operations.</summary>
public sealed class PlcDownloadAuthorizationTests
{
    [Theory]
    [InlineData(McpAccessMode.ReadOnly, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, true)]
    public void PlcDownload_IsDeniedInReadOnlyByHostAndWorker(McpAccessMode mode, bool allowed)
    {
        var host = new OperationAccessPolicy(mode).Authorize(PlcDownloadMethods.Download);
        var worker = WorkerOperationAuthorization.Authorize(mode, PlcDownloadMethods.Download);
        Assert.Equal(allowed, host is null);
        Assert.Equal(allowed, worker is null);
        if (!allowed)
        {
            Assert.Equal(WorkerFailureCategories.AccessDenied, host!.FailureCategory);
            Assert.Equal(WorkerFailureCategories.AccessDenied, worker!.FailureCategory);
        }

        // The offline listing is an observation in every mode.
        Assert.Null(new OperationAccessPolicy(mode).Authorize(PlcDownloadMethods.ListTargets));
        Assert.Null(WorkerOperationAuthorization.Authorize(mode, PlcDownloadMethods.ListTargets));
    }

    [Theory]
    [InlineData(PlcDownloadMethods.Download)]
    [InlineData(PlcDownloadMethods.ListTargets)]
    public void DownloadOperations_AreStandaloneOnlyAndNeedTheBoundProject(string method)
    {
        Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(method));
        Assert.Equal(method, ProjectCapabilityCatalog.OperationFor(method));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, method));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
    }

    [Theory]
    [InlineData(true, WorkerFailureCategories.PortalLost)]
    [InlineData(false, WorkerFailureCategories.WorkerOperationFailed)]
    public void DownloadEvidence_SurvivesPortalLossAndOrdinaryFailures(bool disposed, string category)
    {
        var revision = 0L;
        WorkerResponse? response = null;
        try
        {
            response = WorkerExecution.Execute(() =>
            {
                if (disposed) revision++;
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "aborted",
                    plcDownloadOutcome: """{"evidence":true}""");
            }, () => revision, mutates: true);
        }
        catch (WorkerOperationException exception)
        {
            // Without disposal WorkerExecution rethrows so the dispatch loop maps the carried evidence.
            Assert.False(disposed);
            Assert.Equal("""{"evidence":true}""", exception.PlcDownloadOutcome);
            Assert.Equal(category, exception.FailureCategory);
            return;
        }

        Assert.True(disposed);
        Assert.Equal(category, response!.FailureCategory);
        Assert.Equal("""{"evidence":true}""", response.PlcDownloadOutcome);
    }

    [Fact]
    public void DownloadOutcome_IsAConditionalEnvelopeMember()
    {
        var empty = TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(new WorkerResponse());
        Assert.DoesNotContain("plcDownloadOutcome", empty, StringComparison.Ordinal);
        var carried = TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(new WorkerResponse { PlcDownloadOutcome = "{}" });
        Assert.Contains("\"plcDownloadOutcome\":\"{}\"", carried, StringComparison.Ordinal);
    }
}
