using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;
using static TiaMcpServer.Tests.PlcDownload.PlcDownloadTestData;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// plc_download through the real guarded pipeline and production guard catalog, with a fake gate,
/// a recording audit sink and scripted worker delegates: no worker process is involved.
/// </summary>
public sealed class PlcDownloadWriteDomainTests
{
    private sealed class Script
    {
        public Queue<PlcDownloadTargetInventoryInfo> Inventories { get; } = new();
        public PlcDownloadTargetInventoryInfo Inventory { get; set; } = SinglePlc();
        public WorkerCallResult? ListFailure { get; set; }
        public Func<PlcDownloadRequestInfo, WorkerCallResult>? Download { get; set; }
        public List<string?> Listed { get; } = new();
        public List<PlcDownloadRequestInfo> Downloads { get; } = new();
        public Dictionary<string, string?> Environment { get; } = new();

        public PlcDownloadWriteDomain Domain() => new(
            plcName =>
            {
                Listed.Add(plcName);
                if (ListFailure is not null) return Task.FromResult(ListFailure);
                var inventory = Inventories.Count > 0 ? Inventories.Dequeue() : Inventory;
                return Task.FromResult(WorkerCallResult.Ok(WorkerJson.SerializePayload(inventory)));
            },
            request =>
            {
                Downloads.Add(request);
                return Task.FromResult(Download?.Invoke(request) ?? Succeeded(request));
            },
            name => Environment.TryGetValue(name, out var value) ? value : null);
    }

    private static WorkerCallResult Succeeded(PlcDownloadRequestInfo request)
        => WorkerCallResult.Ok(WorkerJson.SerializePayload(Result(EffectsOf(request))));

    private static PlcDownloadEffects EffectsOf(PlcDownloadRequestInfo request)
        => new(request.PlcName, request.DeviceName, new PlcDownloadPathInfo
        {
            Mode = request.Mode,
            PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = request.PcInterfaceName, Number = request.PcInterfaceNumber },
            TargetInterface = request.TargetInterface,
        }, false, request.Scope, PlcDownloadPolicy.TryGetDownloadOptionNames(request.Scope, out var options) ? options : "", request.StartAfterDownload,
            PlcDownloadPolicy.IsSimulationInterface(request.PcInterfaceName), request.Decisions, request.Passwords, request.PortalConfirmations,
            request.TrustedCertificates);

    private sealed record Run(JsonElement Document, CallToolResult Raw, RecordingAuditSink Audit, int Prompts, string? PromptMessage);

    private static async Task<Run> RunAsync(Script script, PlcDownloadWriteItem item, McpAccessMode mode, bool dryRun = false,
        string answer = "accept", bool supportsElicitation = true)
    {
        var gate = new FakeWriteBindingGate { AccessMode = mode };
        var audit = new RecordingAuditSink(gate);
        var execution = new WriteExecution(gate, audit, WriteGuardRegistration.ProductionCatalog(), TimeProvider.System);
        var prompts = 0;
        string? message = null;
        var confirmation = new UserConfirmation(supportsElicitation, (request, _) =>
        {
            prompts++;
            message = request.Message;
            return ValueTask.FromResult(new ElicitResult
            {
                Action = answer,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
            });
        }, TimeSpan.FromSeconds(10));
        var raw = await execution.RunAsync(script.Domain(), new WriteCall<PlcDownloadWriteItem>(new[] { item }, dryRun),
            new WriteConfirmationContext(confirmation));
        Assert.Equal(raw.Content.OfType<TextContentBlock>().Single().Text, CanonicalJson.Serialize(raw.StructuredContent!.Value));
        return new Run(raw.StructuredContent!.Value, raw, audit, prompts, message);
    }

    private static string[] GuardIds(JsonElement document)
        => document.GetProperty("guards").EnumerateArray().Select(guard => guard.GetProperty("id").GetString()!).ToArray();

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, false)]
    [InlineData(McpAccessMode.Full, true)]
    public async Task DryRun_PreviewsPathAndGuards_WithoutDownloadOrPrompt(McpAccessMode mode, bool acknowledged)
    {
        var script = new Script();
        var run = await RunAsync(script, Item(), mode, dryRun: true);

        Assert.Equal("preview", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.False(run.Raw.IsError == true);
        Assert.Equal(JsonValueKind.Null, run.Document.GetProperty("result").ValueKind);
        var effects = run.Document.GetProperty("effects");
        Assert.Equal("PLCSIM", effects.GetProperty("path").GetProperty("pcInterface").GetProperty("name").GetString());
        Assert.Equal("1 X1", effects.GetProperty("path").GetProperty("targetInterface").GetString());
        Assert.True(effects.GetProperty("targetInterfaceSelectedAutomatically").GetBoolean());
        var guard = Assert.Single(run.Document.GetProperty("guards").EnumerateArray());
        Assert.Equal(PlcDownloadGuardDefinitions.StopsPlc, guard.GetProperty("id").GetString());
        Assert.Equal("acknowledge", guard.GetProperty("severity").GetString());
        Assert.Equal(acknowledged, guard.GetProperty("acknowledged").GetBoolean());
        Assert.Empty(script.Downloads);
        Assert.Equal(0, run.Prompts);
        var record = Assert.Single(run.Audit.Records);
        Assert.Equal("preview", record.Phase);
        Assert.Equal(new WriteAuditConfirmation("none", "not_requested"), record.Confirmation);
        Assert.Null(Assert.Single(record.Guards).SatisfiedBy);
    }

    [Fact]
    public async Task Hardware_InReadWrite_PromptsOnce_ThenDownloadsThePlannedHardwarePath()
    {
        var script = new Script();
        var run = await RunAsync(script, Item(pc: Hardware, target: "1 X2", numberExplicit: true), McpAccessMode.ReadWrite);

        Assert.Equal(1, run.Prompts);
        Assert.Contains("REAL controller", run.PromptMessage);
        Assert.Contains("TargetForSoftware=CPU", run.PromptMessage);
        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("success").GetBoolean(), run.Document.GetRawText());
        var request = Assert.Single(script.Downloads);
        Assert.Equal((Hardware, 1, "1 X2"), (request.PcInterfaceName, request.PcInterfaceNumber, request.TargetInterface));
        Assert.Equal(new[] { PlcDownloadGuardDefinitions.ToHardware, PlcDownloadGuardDefinitions.StopsPlc }, GuardIds(run.Document));
        Assert.All(run.Document.GetProperty("guards").EnumerateArray(), guard =>
        {
            Assert.Equal("acknowledge", guard.GetProperty("severity").GetString());
            Assert.True(guard.GetProperty("acknowledged").GetBoolean());
        });
        var record = Assert.Single(run.Audit.Records);
        Assert.Equal(new WriteAuditConfirmation("user", "confirmed"), record.Confirmation);
        Assert.All(record.Guards, guard => Assert.Equal(GuardSatisfactions.User, guard.SatisfiedBy));
    }

    [Fact]
    public async Task Hardware_InFull_DownloadsUnderPolicy_WithoutPrompt()
    {
        var script = new Script();
        var run = await RunAsync(script, Item(pc: Hardware, target: "1 X1", numberExplicit: true), McpAccessMode.Full);

        Assert.Equal(0, run.Prompts);
        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("success").GetBoolean(), run.Document.GetRawText());
        Assert.Equal(Hardware, Assert.Single(script.Downloads).PcInterfaceName);
        Assert.False(run.Document.GetProperty("effects").GetProperty("simulationTarget").GetBoolean());
        var record = Assert.Single(run.Audit.Records);
        Assert.Equal(new WriteAuditConfirmation("policy", "not_requested"), record.Confirmation);
        Assert.Equal(2, record.Guards.Count);
        Assert.All(record.Guards, guard => Assert.Equal(GuardSatisfactions.Policy, guard.SatisfiedBy));
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task Hardware_DeclinedInReadWrite_OrDryRun_NeverDownloads(McpAccessMode mode)
    {
        var script = new Script();
        var run = await RunAsync(script, Item(pc: Hardware, target: "1 X1", numberExplicit: true), mode, dryRun: mode == McpAccessMode.Full,
            answer: "decline");

        Assert.Empty(script.Downloads);
        if (mode == McpAccessMode.ReadWrite)
        {
            Assert.Equal(1, run.Prompts);
            Assert.Equal(WorkerFailureCategories.AccessDenied, run.Document.GetProperty("error").GetProperty("category").GetString());
        }
        else
        {
            Assert.Equal(0, run.Prompts);
            Assert.Equal("preview", run.Document.GetProperty("phase").GetString());
        }
    }

    [Fact]
    public async Task Hardware_DryRunReportsTheAcknowledgeGuard()
    {
        var run = await RunAsync(new Script(), Item(pc: Hardware, target: "1 X2", numberExplicit: true), McpAccessMode.ReadWrite, dryRun: true);
        Assert.Equal("preview", run.Document.GetProperty("phase").GetString());
        Assert.False(run.Document.GetProperty("effects").GetProperty("simulationTarget").GetBoolean());
        Assert.Equal(new[] { PlcDownloadGuardDefinitions.ToHardware, PlcDownloadGuardDefinitions.StopsPlc }, GuardIds(run.Document));
        var hardware = run.Document.GetProperty("guards")[0];
        Assert.Equal("acknowledge", hardware.GetProperty("severity").GetString());
        Assert.False(hardware.GetProperty("acknowledged").GetBoolean());
        Assert.Contains("real controller", hardware.GetProperty("message").GetString());
        Assert.Equal(0, run.Prompts);
    }

    [Fact]
    public async Task CallerDecisionsPasswordsAndPortalPolicy_ReachTheWorker_AndThePrompt_ButNoSecret()
    {
        const string secret = "Pl3ase-n0t-in-the-audit";
        var script = new Script();
        script.Environment["PLC_PW"] = secret;
        var item = Item(decisions: new[] { Select("ResetModule", "DeleteAll"), Check("UpgradeTargetDevice") },
            passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }, portalConfirmations: PlcDownloadPolicy.PortalConfirmationPolicies.Accept);

        var run = await RunAsync(script, item, McpAccessMode.ReadWrite);

        Assert.True(run.Document.GetProperty("success").GetBoolean(), run.Document.GetRawText());
        Assert.Contains("ResetModule=DeleteAll", run.PromptMessage);
        Assert.Contains("UpgradeTargetDevice=true", run.PromptMessage);
        Assert.Contains("ModuleWriteAccessPassword from PLC_PW", run.PromptMessage);
        Assert.Contains("answered Yes", run.PromptMessage);
        var request = Assert.Single(script.Downloads);
        Assert.Equal(new[] { ("ResetModule", "DeleteAll", false), ("UpgradeTargetDevice", (string?)null, true) },
            request.Decisions.Select(decision => (decision.Type, decision.Selection, decision.Check)));
        Assert.Equal(("ModuleWriteAccessPassword", "PLC_PW"), (request.Passwords.Single().Type, request.Passwords.Single().Variable));
        Assert.Equal(PlcDownloadPolicy.PortalConfirmationPolicies.Accept, request.PortalConfirmations);
        var effects = run.Document.GetProperty("effects");
        Assert.Equal("accept", effects.GetProperty("portalConfirmations").GetString());
        Assert.Equal("PLC_PW", effects.GetProperty("passwords")[0].GetProperty("variable").GetString());
        Assert.True(effects.GetProperty("decisions")[1].GetProperty("check").GetBoolean());
        Assert.DoesNotContain(secret, run.Document.GetRawText());
        Assert.DoesNotContain(secret, run.PromptMessage);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(run.Audit.Records));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task PasswordVariableMissingFromTheServerEnvironment_IsRefusedBeforeAnyWorkerCall(string? value)
    {
        var script = new Script();
        script.Environment["PLC_PW"] = value;
        var run = await RunAsync(script, Item(passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }), McpAccessMode.Full);

        Assert.Equal("error", run.Document.GetProperty("phase").GetString());
        var error = run.Document.GetProperty("error");
        Assert.Equal(WorkerFailureCategories.ValidationError, error.GetProperty("category").GetString());
        Assert.Contains("the environment variable named for this configuration is not set", error.GetProperty("message").GetString());
        Assert.DoesNotContain("PLC_PW", error.GetProperty("message").GetString());
        Assert.Empty(script.Listed);
        Assert.Empty(script.Downloads);
    }

    [Fact]
    public async Task AcceptedPortalConfirmations_SucceedWithAWarning()
    {
        var script = new Script
        {
            Download = request =>
            {
                var result = Result(EffectsOf(request));
                result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "Siemens.Engineering.ConfirmationEventArgs", Answer = "Yes" });
                result.PortalConfirmationsObserved = 1;
                return WorkerCallResult.Ok(WorkerJson.SerializePayload(result));
            },
        };
        var run = await RunAsync(script, Item(portalConfirmations: PlcDownloadPolicy.PortalConfirmationPolicies.Accept), McpAccessMode.Full);

        Assert.True(run.Document.GetProperty("success").GetBoolean(), run.Document.GetRawText());
        Assert.Contains(run.Document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("each was answered Yes"));
    }

    [Fact]
    public async Task ReadWrite_AcceptedConfirmation_DownloadsExactlyThePlannedPath()
    {
        var script = new Script();
        var run = await RunAsync(script, Item(scope: PlcDownloadPolicy.Scopes.SoftwareOnlyChanges, start: false), McpAccessMode.ReadWrite);

        Assert.Equal(1, run.Prompts);
        Assert.Contains("plc_download", run.PromptMessage);
        Assert.Contains("PC interface \"PLCSIM\" #1", run.PromptMessage);
        Assert.Contains("left in STOP", run.PromptMessage);
        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("success").GetBoolean(), run.Document.GetRawText());
        var request = Assert.Single(script.Downloads);
        Assert.Equal(("PLC_1", "Station_1", "PN/IE", "PLCSIM", 1, "1 X1", PlcDownloadPolicy.Scopes.SoftwareOnlyChanges, false),
            (request.PlcName, request.DeviceName, request.Mode, request.PcInterfaceName, request.PcInterfaceNumber,
                request.TargetInterface, request.Scope, request.StartAfterDownload));
        // Planned once, then re-planned after the prompt (the TIA UI can change state while it is visible).
        Assert.Equal(2, script.Listed.Count);
        var value = run.Document.GetProperty("result").GetProperty("value");
        Assert.False(value.GetProperty("completionVerified").GetBoolean());
        Assert.Contains(run.Document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == PlcDownloadPolicy.CompletionNote);
        var record = Assert.Single(run.Audit.Records);
        Assert.Equal(new WriteAuditConfirmation("user", "confirmed"), record.Confirmation);
        Assert.Equal(GuardSatisfactions.User, Assert.Single(record.Guards).SatisfiedBy);
        Assert.Equal("succeeded", Assert.Single(record.Items).Status);
    }

    [Theory]
    [InlineData("decline", true)]
    [InlineData("cancel", true)]
    [InlineData(null, false)]
    public async Task ReadWrite_WithoutConfirmation_DeniesBeforeDownload(string? answer, bool supportsElicitation)
    {
        var script = new Script();
        var run = await RunAsync(script, Item(), McpAccessMode.ReadWrite, answer: answer ?? "accept", supportsElicitation: supportsElicitation);

        Assert.Equal("blocked", run.Document.GetProperty("phase").GetString());
        Assert.Equal(WorkerFailureCategories.AccessDenied, run.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(script.Downloads);
        Assert.Equal(supportsElicitation ? 1 : 0, run.Prompts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PcInterfaceNumberExplicitness_ReachesTheWorkerRequest(bool numberExplicit)
    {
        var script = new Script();
        await RunAsync(script, Item(numberExplicit: numberExplicit), McpAccessMode.Full);
        Assert.Equal(numberExplicit, Assert.Single(script.Downloads).PcInterfaceNumberExplicit);
    }

    [Fact]
    public async Task SeveralPlcsimInterfaces_WithoutANumber_AreRefusedBeforeAnyDownload()
    {
        var script = new Script { Inventory = TwoPlcsimInterfaces() };
        var run = await RunAsync(script, Item(), McpAccessMode.Full);

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, run.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.True(run.Raw.IsError == true);
        Assert.Empty(script.Downloads);
    }

    [Fact]
    public async Task Full_RunsUnderPolicy_WithoutPrompt()
    {
        var script = new Script();
        var run = await RunAsync(script, Item(), McpAccessMode.Full);

        Assert.Equal(0, run.Prompts);
        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.Single(script.Downloads);
        Assert.Single(script.Listed);
        var record = Assert.Single(run.Audit.Records);
        Assert.Equal(new WriteAuditConfirmation("policy", "not_requested"), record.Confirmation);
        Assert.Equal(GuardSatisfactions.Policy, Assert.Single(record.Guards).SatisfiedBy);
    }

    [Fact]
    public async Task PathChangedWhilePromptVisible_IsStaleAndDoesNotDownload()
    {
        var script = new Script();
        script.Inventories.Enqueue(SinglePlc());
        var changed = SinglePlc();
        changed.Plcs[0].Modes[0].PcInterfaces[0].TargetInterfaces[0].Name = "1 X9";
        script.Inventories.Enqueue(changed);

        var run = await RunAsync(script, Item(), McpAccessMode.ReadWrite);

        Assert.Equal("blocked", run.Document.GetProperty("phase").GetString());
        Assert.Equal(WorkerFailureCategories.BindingConflict, run.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(script.Downloads);
    }

    [Fact]
    public async Task AbortEvidence_IsAnAppliedFailureWithTypedEvidence()
    {
        var script = new Script
        {
            Download = request => WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "plc_download aborted")
                with { PlcDownloadOutcome = WorkerJson.SerializePayload(Aborted(EffectsOf(request))) },
        };
        var run = await RunAsync(script, Item(), McpAccessMode.Full);

        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.False(run.Document.GetProperty("success").GetBoolean());
        Assert.False(run.Raw.IsError == true);
        Assert.Equal(JsonValueKind.Null, run.Document.GetProperty("error").ValueKind);
        var result = run.Document.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.GetProperty("failure").GetProperty("category").GetString());
        var value = result.GetProperty("value");
        Assert.Equal("Siemens.Engineering.Download.Configurations.DataBlockReinitialization",
            value.GetProperty("abort").GetProperty("type").GetString());
        Assert.Equal(new[] { "selected", "aborted" },
            value.GetProperty("configurationsSeen").EnumerateArray().Select(seen => seen.GetProperty("decision").GetString()));
        var warnings = run.Document.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()).ToArray();
        Assert.Contains(warnings, warning => warning!.Contains("Inspect it before retrying"));
        Assert.Equal("failed", Assert.Single(Assert.Single(run.Audit.Records).Items).Status);
    }

    [Fact]
    public async Task EngineeringRejection_KeepsCategoryAndEngineeringError()
    {
        var script = new Script
        {
            Download = _ => WorkerCallResult.Fail(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the operation: no")
                with { EngineeringError = new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = "no" } },
        };
        var run = await RunAsync(script, Item(), McpAccessMode.Full);

        var failure = run.Document.GetProperty("result").GetProperty("failure");
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.GetProperty("category").GetString());
        Assert.Equal("EngineeringTargetInvocationException", failure.GetProperty("engineeringError").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, run.Document.GetProperty("result").GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task AmbiguousTarget_FailsPlanningWithCandidates()
    {
        var script = new Script();
        var run = await RunAsync(script, Item(pc: Hardware), McpAccessMode.Full);

        Assert.Equal("error", run.Document.GetProperty("phase").GetString());
        var error = run.Document.GetProperty("error");
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.GetProperty("category").GetString());
        Assert.Contains("'1 X2'", error.GetProperty("message").GetString());
        Assert.Empty(script.Downloads);
    }

    [Fact]
    public async Task ListFailureAndMalformedInventory_FailPlanning()
    {
        var failing = new Script { ListFailure = WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound, "No PLC named 'X'.") };
        var run = await RunAsync(failing, Item(plc: "X"), McpAccessMode.Full);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, run.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(new string?[] { "X" }, failing.Listed);

        var malformed = new Script { ListFailure = WorkerCallResult.Ok("""{"plcs":[{"private":"PRIVATE"}]}""") };
        run = await RunAsync(malformed, Item(), McpAccessMode.Full);
        Assert.Equal(WorkerFailureCategories.ProtocolError, run.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("PRIVATE", run.Document.GetRawText());
        Assert.Empty(malformed.Downloads);
    }

    public static TheoryData<string, PlcDownloadWriteItem> InvalidItems => new()
    {
        { "scope", Item(scope: "everything") },
        { "scope", Item(scope: "Software") },
        { "mode", Item(mode: " ") },
        { "pcInterface.name", Item(pc: "") },
        { "pcInterface.name", Item() with { PcInterfaceName = null! } },
        { "pcInterface.number", Item(number: -1) },
        { "plcName", Item(plc: "") },
        { "targetInterface", Item(target: " ") },
        { "mode", Item(mode: new string('m', 257)) },
        { "portalConfirmations", Item(portalConfirmations: "Accept") },
        { "decisions", Item(decisions: new[] { Select("ResetModule", " ") }) },
        { "decisions.StartModules", Item(decisions: new[] { Select("StartModules", "NoAction") }) },
        { "passwords", Item(passwords: new[] { Password("ModuleWriteAccessPassword", "pass word") }) },
    };

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task InvalidArguments_AreValidationErrorsBeforeAnyWorkerCall(string field, PlcDownloadWriteItem item)
    {
        var script = new Script();
        var run = await RunAsync(script, item, McpAccessMode.Full);

        Assert.Equal("error", run.Document.GetProperty("phase").GetString());
        var error = run.Document.GetProperty("error");
        Assert.Equal(WorkerFailureCategories.ValidationError, error.GetProperty("category").GetString());
        Assert.Contains(field, error.GetProperty("message").GetString());
        Assert.Empty(script.Listed);
        Assert.Empty(script.Downloads);
        Assert.Single(run.Audit.Records);
    }

    [Fact]
    public async Task RhTarget_IsConfirmedAndForwarded_AndAnUnknownValueIsRefused()
    {
        // Issue #16 (not live-tested): an R/H system CPU downloads through RHDownloadProvider.DownloadToPrimary/DownloadToBackup.
        var script = new Script();
        var run = await RunAsync(script, Item() with { RhTarget = "backup" }, McpAccessMode.ReadWrite);
        Assert.True(run.Document.GetProperty("success").GetBoolean(), run.Document.ToString());
        Assert.Equal("backup", run.Document.GetProperty("effects").GetProperty("rhTarget").GetString());
        Assert.Contains("R/H system: the download goes to the backup CPU", run.PromptMessage);
        Assert.Equal("backup", Assert.Single(script.Downloads).RhTarget);

        var plain = await RunAsync(new Script(), Item(), McpAccessMode.Full, dryRun: true);
        Assert.False(plain.Document.GetProperty("effects").TryGetProperty("rhTarget", out _));

        var invalid = new Script().Domain().Validate(new[] { Item() with { RhTarget = "both" } }, McpAccessMode.Full);
        Assert.False(invalid.IsValid);
        Assert.Contains("rhTarget", invalid.Error!.Message);
    }

    [Fact]
    public void ReadOnly_IsAccessDenied()
    {
        var validation = new Script().Domain().Validate(new[] { Item() }, McpAccessMode.ReadOnly);
        Assert.False(validation.IsValid);
        Assert.Equal(WorkerFailureCategories.AccessDenied, validation.Error!.Category);
    }

    [Fact]
    public void Domain_ConfirmsEveryCall_AndGuardsAreRegisteredWithFixedSeverities()
    {
        Assert.True(new Script().Domain().ConfirmsEveryCall);
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.Equal(WriteGuardSeverities.Acknowledge, catalog.Get(PlcDownloadGuardDefinitions.ToHardware).Severity);
        Assert.Equal(WriteGuardSeverities.Acknowledge, catalog.Get(PlcDownloadGuardDefinitions.StopsPlc).Severity);
        Assert.Contains("real controller", catalog.Get(PlcDownloadGuardDefinitions.ToHardware).Description);
        Assert.False(catalog.TryGet("download_target_not_simulation", out _));
        Assert.Equal(WriteGuardSeverities.Acknowledge, catalog.Get(PlcDownloadGuardDefinitions.IncludesSafetyProgram).Severity);
        // No block guard: an F-program download to a real controller is acknowledged like any hardware download (owner decision).
        Assert.Empty(PlcDownloadGuardDefinitions.Definitions.Where(definition => definition.Severity == WriteGuardSeverities.Block));
        // The acknowledge guards are only legal because this domain confirms every call; the non-interactive
        // builder used by the data-write domains refuses them.
        Assert.Throws<ArgumentException>(() => WriteGuardCatalog.NonInteractive(PlcDownloadGuardDefinitions.Definitions, "plc_download"));
    }

    [Fact]
    public void Guards_AlwaysWarnAboutStopping_AndAboutHardwareOnlyOffPlcsim()
    {
        var domain = new Script().Domain();
        var simulation = domain.EvaluateGuards(new[] { Item() }, new[] { ItemPlan<PlcDownloadEffects>.Resolved(Effects()) });
        Assert.Equal(new[] { PlcDownloadGuardDefinitions.StopsPlc }, simulation.Select(guard => guard.Id));
        Assert.Contains("restarts it", simulation[0].Message);

        var stays = domain.EvaluateGuards(new[] { Item(start: false) }, new[] { ItemPlan<PlcDownloadEffects>.Resolved(Effects(Item(start: false))) });
        Assert.Contains("leaves it in STOP", stays[0].Message);

        var decided = Item(decisions: new[] { Select("StopModules", "NoAction") });
        var noStop = domain.EvaluateGuards(new[] { decided }, new[] { ItemPlan<PlcDownloadEffects>.Resolved(Effects(decided)) });
        Assert.Contains("NoAction by the caller", noStop[0].Message);

        var hardwareItem = Item(pc: Hardware, target: "1 X1", numberExplicit: true);
        var hardware = domain.EvaluateGuards(new[] { hardwareItem }, new[] { ItemPlan<PlcDownloadEffects>.Resolved(Effects(hardwareItem)) });
        Assert.Equal(new[] { PlcDownloadGuardDefinitions.ToHardware, PlcDownloadGuardDefinitions.StopsPlc }, hardware.Select(guard => guard.Id));
        Assert.Contains("real controller", hardware[0].Message);
        Assert.Contains("machine it controls may stop or move", hardware[0].Message);
        Assert.Contains("TargetForSoftware is answered CPU", hardware[0].Message);
    }
}
