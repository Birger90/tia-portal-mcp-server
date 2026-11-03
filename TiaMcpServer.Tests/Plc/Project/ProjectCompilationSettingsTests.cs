using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Project;

/// <summary>
/// K10: plc_write set_project_compilation_settings. The Siemens-free binding (property, then writable attribute, else
/// api_member_missing), the host catalog, planning, decoding and verification, and the plc-write-roundtrip FakeWorker. Not
/// live-tested: whether the V21 providers' flags are writable properties, attributes or neither is decided only live.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class ProjectCompilationSettingsTests
{
    private sealed class WritableProvider
    {
        public bool IsSimulationDuringBlockCompilationEnabled { get; set; }
    }

    private sealed class ReadOnlyProvider
    {
        public bool Value;

        public bool IsSimulationDuringBlockCompilationEnabled => Value;
    }

    private sealed class TextProvider
    {
        public string IsSimulationDuringBlockCompilationEnabled { get; set; } = "x";
    }

    private const string Member = ProjectCompilationSettingNames.SimulationMember;

    [Fact]
    public void Binding_PrefersTheWritableProperty_AndReadsItBack()
    {
        var provider = new WritableProvider();
        var assignment = ProjectCompilationSettingBinding.Bind(provider, Member, null);

        Assert.Equal(ProjectCompilationSettingBinding.Routes.Property, assignment.Route);
        Assert.False(assignment.Read());
        assignment.Apply(true);
        Assert.True(provider.IsSimulationDuringBlockCompilationEnabled);
        Assert.True(assignment.Read());
    }

    [Fact]
    public void Binding_FallsBackToAWritableAttribute_AndStillReadsThroughTheGetter()
    {
        var provider = new ReadOnlyProvider();
        var set = new List<(string, object)>();
        var assignment = ProjectCompilationSettingBinding.Bind(provider, Member, new ProjectCompilationSettingBinding.AttributeSurface(
            () => new[] { (Member, true) }, (name, value) => { set.Add((name, value)); provider.Value = (bool)value; }));

        Assert.Equal(ProjectCompilationSettingBinding.Routes.SetAttribute, assignment.Route);
        assignment.Apply(true);
        Assert.Equal((Member, (object)true), Assert.Single(set));
        Assert.True(assignment.Read());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Binding_WithoutAWritableRoute_FailsBeforeAnythingChanges(bool withSurface, bool listedReadOnly)
    {
        var provider = new ReadOnlyProvider();
        var surface = withSurface
            ? new ProjectCompilationSettingBinding.AttributeSurface(() => listedReadOnly ? new[] { (Member, false) } : Array.Empty<(string, bool)>(),
                (_, _) => throw new InvalidOperationException("must not be called"))
            : null;

        var missing = Assert.Throws<ProjectCompilationSettingMissingException>(() => ProjectCompilationSettingBinding.Bind(provider, Member, surface));

        Assert.Equal(Member, missing.Member);
        Assert.Contains("Nothing was changed", missing.Message);
        Assert.False(provider.Value);
    }

    [Fact]
    public void Binding_NeverWritesAPropertyOfAnotherType()
        => Assert.Throws<ProjectCompilationSettingMissingException>(() => ProjectCompilationSettingBinding.Bind(new TextProvider(), Member, null));

    [Fact]
    public void Catalog_NeedsAtLeastOneSetting_AndAcceptsBoth()
    {
        var empty = new PlcOperationRequest { OperationId = "c", Operation = ProjectCompilationSettingsMethods.Set };
        var invalid = PlcOperationCatalog.ValidateWrite(new[] { empty });
        Assert.False(invalid.IsValid);
        Assert.Contains("simulationDuringBlockCompilation", invalid.Error);

        var both = new PlcOperationRequest { OperationId = "c", Operation = ProjectCompilationSettingsMethods.Set, SimulationDuringBlockCompilation = true, VirtualPlcDuringBlockCompilation = false };
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { both }).IsValid, PlcOperationCatalog.ValidateWrite(new[] { both }).Error);
        var withPlc = new PlcOperationRequest { OperationId = "c", Operation = ProjectCompilationSettingsMethods.Set, SimulationDuringBlockCompilation = true, PlcName = "PLC_1" };
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { withPlc }).IsValid);
        var onRead = new PlcOperationRequest { OperationId = "r", Operation = "get_block_content", BlockPath = "PLC_1/Main", SimulationDuringBlockCompilation = true };
        Assert.False(PlcOperationCatalog.ValidateRead(new[] { onRead }).IsValid);
    }

    [Fact]
    public void Policy_IsAProjectMutation_DeniedInReadOnly_AndAStandaloneOnlyRow()
    {
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(ProjectCompilationSettingsMethods.Set));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, ProjectCompilationSettingsMethods.Set));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, ProjectCompilationSettingsMethods.Set));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.Full, ProjectCompilationSettingsMethods.Set));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, ProjectCompilationSettingsMethods.Set));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, ProjectCompilationSettingsMethods.Set));
        Assert.Contains(ProjectCapabilityCatalog.Describe(ProjectContainerKinds.StandaloneProject),
            row => row.Operation == ProjectCompilationSettingsMethods.Set && row.Applicability == ProjectCapabilityApplicabilities.StandaloneOnly);
    }

    [Fact]
    public void Decode_RequiresExactlyTheRequestedSettings_AndVerifyComparesTheReadBack()
    {
        var item = new PlcOperationRequest { OperationId = "c", Operation = ProjectCompilationSettingsMethods.Set, SimulationDuringBlockCompilation = true };
        var result = new ProjectCompilationSettingsResultInfo
        {
            Settings =
            {
                new ProjectCompilationSettingResultInfo
                {
                    Setting = ProjectCompilationSettingNames.SimulationField, Provider = ProjectCompilationSettingNames.SimulationProvider,
                    Member = ProjectCompilationSettingNames.SimulationMember, Requested = true, Before = false, After = true,
                    Route = ProjectCompilationSettingBinding.Routes.Property, Signature = "Boolean IsSimulationDuringBlockCompilationEnabled { set; }",
                },
            },
        };
        var element = PlcCompilationSettings.DecodeWrite(item, WorkerJson.SerializePayload(result));
        var verified = PlcCompilationSettings.Verify(item, null, new Dictionary<string, JsonElement> { ["c"] = element });
        Assert.True(verified.Success);
        Assert.Equal("simulationDuringBlockCompilation=true", verified.Actual);

        result.Settings[0].After = false;
        var differing = PlcCompilationSettings.Verify(item, null, new Dictionary<string, JsonElement> { ["c"] = PlcCompilationSettings.DecodeWrite(item, WorkerJson.SerializePayload(result)) });
        Assert.False(differing.Success);

        result.Settings[0].Requested = false;
        Assert.ThrowsAny<JsonException>(() => PlcCompilationSettings.DecodeWrite(item, WorkerJson.SerializePayload(result)));
        result.Settings[0].Requested = true;
        result.Settings[0].Route = "reflection";
        Assert.ThrowsAny<JsonException>(() => PlcCompilationSettings.DecodeWrite(item, WorkerJson.SerializePayload(result)));
    }

    [Fact]
    public async Task FakeWorker_SetsBothSettings_AndVerifiesTheReadBack()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.ReadWrite);
        var item = new PlcOperationRequest
        {
            OperationId = "sim", Operation = ProjectCompilationSettingsMethods.Set, SimulationDuringBlockCompilation = true, VirtualPlcDuringBlockCompilation = false,
        };

        var preview = await fixture.RunAsync(true, item);
        Assert.Equal("preview", preview.Phase);
        var effect = Assert.Single(preview.Effects).Effect!;
        Assert.Equal(PlcCompilationSettings.Kind, effect.Target.Kind);
        Assert.Equal(new[] { "simulationDuringBlockCompilation", "virtualPlcDuringBlockCompilation" }, effect.Changes.Select(c => c.Field));

        var applied = await fixture.RunAsync(false, item);
        Assert.True(applied.Success, JsonSerializer.Serialize(applied.Error));
        var outcome = applied.Batch!.Operations.Single();
        Assert.Equal("succeeded", outcome.Status);
        var settings = outcome.Result!.Value.GetProperty("settings");
        Assert.False(settings[0].GetProperty("before").GetBoolean());
        Assert.True(settings[0].GetProperty("after").GetBoolean());
        Assert.Equal("property", settings[0].GetProperty("route").GetString());
        var check = Assert.Single(applied.Verification!.Operations);
        Assert.True(check.Success);
        Assert.Equal(PlcCompilationSettings.Check, check.Check);
    }

    [Fact]
    public async Task FakeWorker_ReadOnlyProvider_FailsApiMemberMissing()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-compilation-readonly", McpAccessMode.Full);
        var applied = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "sim", Operation = ProjectCompilationSettingsMethods.Set, SimulationDuringBlockCompilation = true,
        });

        var failure = applied.Batch!.Operations.Single().Failure!;
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, failure.Category);
        Assert.Contains("Nothing was changed", failure.Message);
    }
}
