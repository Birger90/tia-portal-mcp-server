using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

/// <summary>
/// Issue #4: the lifecycle tool retrieve_project (ProjectComposition.Retrieve / RetrieveWithUpgrade). Validation, policy,
/// confirmation text, the binding rule (forceRebind like open_project), the K16-style acknowledge guard, the K30/K51 refusal
/// mapping and the bind to the retrieved project against the guarded-lifecycle FakeWorker. Not live-tested.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class RetrieveProjectTests
{
    [Fact]
    public void Policy_IsALifecycleOperation_StandaloneOnly_AndNeedsNoBinding()
    {
        Assert.Equal(OperationCapability.ProjectLifecycle, OperationPolicyCatalog.GetCapability("retrieve_project"));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "retrieve_project"));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, "retrieve_project"));
        Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity("retrieve_project"));
        Assert.False(ProjectCapabilityCatalog.RequiresBoundProject("retrieve_project"));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, "retrieve_project"));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "retrieve_project"));
    }

    [Fact]
    public void Confirmation_NamesTheArchiveTheDirectoryAndTheUpgrade()
    {
        var item = new LifecycleWriteItem("retrieve_project") { ArchivePath = "A.zap21", TargetDirectory = "Target", Upgrade = true };
        var effect = new LifecycleEffects(null, null, "Target", null, null, false, false, true, false, null, "A.zap21");
        var message = new LifecycleWriteDomain(null!, item).DescribeForConfirmation([item], [ItemPlan<LifecycleEffects>.Resolved(effect)]);
        Assert.Contains("retrieve_project", message);
        Assert.Contains("\"A.zap21\"", message);
        Assert.Contains("\"Target\"", message);
        Assert.Contains("with upgrade", message);
    }

    [Fact]
    public void Validation_NeedsAnExistingArchiveAndDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("tia-mcp-retrieve-").FullName;
        try
        {
            var archive = Path.Combine(directory, "A.zap21");
            WriteValidation Validate(string? archivePath, string? target)
                => new LifecycleWriteDomain(null!, new("retrieve_project") { ArchivePath = archivePath, TargetDirectory = target })
                    .Validate([new("retrieve_project") { ArchivePath = archivePath, TargetDirectory = target }], McpAccessMode.Full);
            Assert.False(Validate(archive, directory).IsValid);
            File.WriteAllText(archive, "archive");
            Assert.True(Validate(archive, directory).IsValid);
            Assert.False(Validate("A.zap21", directory).IsValid);
            Assert.False(Validate(archive, Path.Combine(directory, "missing")).IsValid);
            Assert.False(Validate(archive, null).IsValid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonElement Root(CallToolResult result) => JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement.Clone();

    [Fact]
    public async Task Retrieve_BindsToTheRetrievedProject_AndAPreviewRetrievesNothing()
    {
        using var fixture = new Fixture("retrieve");
        var item = fixture.Item(forceRebind: true);

        var preview = Root(await ProjectWriteTools.ExecuteAsync(fixture.Client, fixture.Execution, item, new(null), dryRun: true));
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var guard = Assert.Single(preview.GetProperty("guards").EnumerateArray(),
            g => g.GetProperty("id").GetString() == LifecycleWriteDomain.RetrieveRequiresNoOpenProject);
        Assert.Equal(WriteGuardSeverities.Acknowledge, guard.GetProperty("severity").GetString());
        Assert.Contains("Another project is already open.", guard.GetProperty("message").GetString());
        Assert.False(Directory.Exists(fixture.RetrievedDirectory));

        var applied = Root(await ProjectWriteTools.ExecuteAsync(fixture.Client, fixture.Execution, item, new(null), dryRun: false));
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal("retrieve_project", applied.GetProperty("result").GetProperty("value").GetProperty("operation").GetString());
        Assert.True(File.Exists(fixture.RetrievedPath));
        Assert.True(LifecyclePayloadContract.SamePath(fixture.RetrievedPath, fixture.Client.BindingSnapshot.ProjectPath));
        Assert.True(LifecyclePayloadContract.SamePath(fixture.RetrievedPath,
            applied.GetProperty("verification").GetProperty("value").GetProperty("path").GetString()));
    }

    [Fact]
    public async Task Retrieve_WhileBound_WithoutForceRebind_IsABindingConflict()
    {
        using var fixture = new Fixture("retrieve-bound");
        var applied = Root(await ProjectWriteTools.ExecuteAsync(fixture.Client, fixture.Execution, fixture.Item(forceRebind: false), new(null), dryRun: false));
        Assert.Equal(WorkerFailureCategories.BindingConflict, applied.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("forceRebind", applied.GetProperty("error").GetProperty("message").GetString());
        Assert.False(Directory.Exists(fixture.RetrievedDirectory));
    }

    [Fact]
    public async Task RetrieveRefusedBecauseAnotherProjectIsOpen_IsEngineeringRejected()
    {
        using var fixture = new Fixture("another-open");
        var root = Root(await ProjectWriteTools.ExecuteAsync(fixture.Client, fixture.Execution, fixture.Item(forceRebind: true), new(null), dryRun: false));
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        var failure = root.GetProperty("result").GetProperty("failure");
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.GetProperty("category").GetString());
        Assert.Contains("TIA Portal rejected retrieve_project", failure.GetProperty("message").GetString());
        Assert.Contains("Another project is already open.", failure.GetProperty("message").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _archive;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        public Fixture(string behavior)
        {
            _root = Path.Combine(Path.GetTempPath(), "guarded-lifecycle-" + behavior + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Source"));
            Directory.CreateDirectory(Path.Combine(_root, "Retrieved"));
            _source = Path.Combine(_root, "Source", "Source.ap21");
            _archive = Path.Combine(_root, "Archived.zap21");
            File.WriteAllText(_source, "fixture");
            File.WriteAllText(_archive, "fixture archive");
            _uiOpen = new FakeWorkerUiOpenProject(_source);
            Client = new(new ProjectSessionBinding(_source), logger: null, workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new(McpAccessMode.Full));
            Execution = new(new OpennessWriteBindingGate(Client), new Audit(), LifecycleWriteDomain.Catalog, TimeProvider.System);
        }

        public OpennessWorkerClient Client { get; }

        public WriteExecution Execution { get; }

        public string RetrievedDirectory => Path.Combine(_root, "Retrieved", "Archived");

        public string RetrievedPath => Path.Combine(RetrievedDirectory, "Archived.ap21");

        public LifecycleWriteItem Item(bool forceRebind) => new("retrieve_project")
        {
            ArchivePath = _archive, TargetDirectory = Path.Combine(_root, "Retrieved"), ForceRebind = forceRebind,
        };

        public void Dispose()
        {
            Client.Dispose();
            _uiOpen.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Audit : IWriteAuditSink
    {
        public void Append(WriteAuditRecord record)
        {
        }
    }
}
