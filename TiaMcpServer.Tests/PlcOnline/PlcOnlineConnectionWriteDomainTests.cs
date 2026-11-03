using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// plc_tia_online and plc_station_upload through the real guarded pipeline and production guard catalog,
/// with a fake gate, a recording audit sink and scripted worker delegates: no worker process is involved.
/// </summary>
public sealed class PlcOnlineConnectionWriteDomainTests
{
    private const string Hardware = "Intel(R) Ethernet";

    private static PlcOnlineStatePlcInfo Plc(string software, string device, string state = "Offline", bool provider = true) => new()
    {
        PlcName = software, DeviceName = device, OnlineProviderAvailable = provider,
        State = provider ? state : null, IsConfigured = provider ? true : null,
        Modes = provider ? new()
        {
            new PlcConnectionModeInfo
            {
                Name = "PN/IE",
                PcInterfaces =
                {
                    new PlcConnectionPcInterfaceInfo { Name = "PLCSIM", Number = 1, TargetInterfaces = { new() { Name = "1 X1" } } },
                    new PlcConnectionPcInterfaceInfo { Name = Hardware, Number = 1, TargetInterfaces = { new() { Name = "1 X1" }, new() { Name = "1 X2" } } },
                },
            },
        } : new(),
    };

    private sealed class Script
    {
        public PlcOnlineStateInventoryInfo Inventory { get; set; } = new() { Plcs = { Plc("PLC_1", "Station_1") } };
        public Func<string, PlcOnlineRequestInfo, WorkerCallResult>? Act { get; set; }
        public List<(string Method, PlcOnlineRequestInfo Request)> Actions { get; } = new();

        public PlcOnlineConnectionWriteDomain Domain() => new(
            _ => Task.FromResult(WorkerCallResult.Ok(WorkerJson.SerializePayload(Inventory))),
            (method, request) =>
            {
                Actions.Add((method, request));
                return Task.FromResult(Act?.Invoke(method, request) ?? Reached(method, request));
            });
    }

    private static PlcOnlineActionResultInfo Result(string method, PlcOnlineRequestInfo request, bool reached = true, string? after = null)
    {
        var online = method == PlcOnlineMethods.GoOnline;
        var result = new PlcOnlineActionResultInfo
        {
            Action = online ? "go_online" : "go_offline", PlcName = request.PlcName!, DeviceName = request.DeviceName!,
            TimeoutSeconds = request.TimeoutSeconds, StateBefore = online ? "Offline" : "Online",
            StateAfter = after ?? (online ? "Online" : "Offline"), GoalReached = reached,
            Outcome = online ? (reached ? PlcOnlinePolicy.ActionOutcomes.Connected : PlcOnlinePolicy.ActionOutcomes.NotReached)
                : (reached ? PlcOnlinePolicy.ActionOutcomes.Disconnected : PlcOnlinePolicy.ActionOutcomes.NotReached),
            PathVerified = online && reached,
            Path = online ? new PlcConnectionPathInfo
            {
                Mode = request.Mode!, TargetInterface = request.TargetInterface,
                PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = request.PcInterfaceName!, Number = request.PcInterfaceNumber },
            } : null,
        };
        result.StatesObserved.Add(result.StateAfter!);
        return result;
    }

    private static WorkerCallResult Reached(string method, PlcOnlineRequestInfo request) => WorkerCallResult.Ok(WorkerJson.SerializePayload(Result(method, request)));

    private static PlcOnlineConnectionItem GoOnline(string? plc = null, string pc = "PLCSIM", int number = 1, string? target = null, int timeout = 30,
        params (string Type, string Variable)[] passwords) => new()
    {
        Action = PlcOnlineConnectionActions.GoOnline, Operation = PlcOnlineMethods.GoOnline, PlcName = plc,
        PcInterface = new PlcOnlinePcInterfaceArgument { Name = pc, Number = number }, TargetInterface = target, TimeoutSeconds = timeout,
        Passwords = passwords.Select(p => new PlcCallbackPasswordInfo { Type = p.Type, Variable = p.Variable }).ToArray(),
    };

    private static PlcOnlineConnectionItem GoOffline(string? plc = null) => new()
    {
        Action = PlcOnlineConnectionActions.GoOffline, Operation = PlcOnlineMethods.GoOffline, PlcName = plc,
    };

    private sealed record Run(JsonElement Document, CallToolResult Raw, RecordingAuditSink Audit, int Prompts, string? PromptMessage);

    private static async Task<Run> RunAsync<TItem, TEffect, TResponse>(IWriteDomain<TItem, TEffect, object, TResponse> domain, TItem item,
        McpAccessMode mode, bool dryRun = false, string answer = "accept") where TItem : TiaMcpServer.OperationBatches.IOperationBatchItem
    {
        var gate = new FakeWriteBindingGate { AccessMode = mode };
        var audit = new RecordingAuditSink(gate);
        var execution = new WriteExecution(gate, audit, WriteGuardRegistration.ProductionCatalog(), TimeProvider.System);
        var prompts = 0;
        string? message = null;
        var confirmation = new UserConfirmation(true, (request, _) =>
        {
            prompts++;
            message = request.Message;
            return ValueTask.FromResult(new ElicitResult
            {
                Action = answer,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
            });
        }, TimeSpan.FromSeconds(10));
        var raw = await execution.RunAsync(domain, new WriteCall<TItem>(new[] { item }, dryRun), new WriteConfirmationContext(confirmation));
        Assert.Equal(raw.Content.OfType<TextContentBlock>().Single().Text, CanonicalJson.Serialize(raw.StructuredContent!.Value));
        return new Run(raw.StructuredContent!.Value, raw, audit, prompts, message);
    }

    private static string[] GuardIds(JsonElement document)
        => document.GetProperty("guards").EnumerateArray().Select(guard => guard.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task GoOnline_DryRunResolvesTheOnlyTargetWithoutConnectingOrPrompting()
    {
        var script = new Script();
        var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.ReadWrite, dryRun: true);

        Assert.Equal("preview", run.Document.GetProperty("phase").GetString());
        var effects = run.Document.GetProperty("effects");
        Assert.Equal("1 X1", effects.GetProperty("path").GetProperty("targetInterface").GetString());
        Assert.True(effects.GetProperty("targetInterfaceSelectedAutomatically").GetBoolean());
        Assert.True(effects.GetProperty("simulationInterface").GetBoolean());
        Assert.Empty(GuardIds(run.Document));
        Assert.Empty(script.Actions);
        Assert.Equal(0, run.Prompts);
    }

    [Fact]
    public async Task GoOnline_ReadWriteConfirmsAndRunsTheExactPlannedPath()
    {
        var script = new Script();
        var run = await RunAsync(script.Domain(), GoOnline(pc: Hardware, target: "1 X2", passwords: ("OnlinePasswordConfiguration", "PLC_PW")),
            McpAccessMode.ReadWrite);

        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.Equal(1, run.Prompts);
        Assert.Contains("a hardware interface, not PLCSIM", run.PromptMessage);
        Assert.Contains("OnlinePasswordConfiguration", run.PromptMessage);
        Assert.Equal(new[] { PlcOnlineGuardDefinitions.HardwareTarget }, GuardIds(run.Document));
        var (method, request) = Assert.Single(script.Actions);
        Assert.Equal(PlcOnlineMethods.GoOnline, method);
        Assert.Equal(("PLC_1", "Station_1", "PN/IE", Hardware, 1, "1 X2"),
            (request.PlcName, request.DeviceName, request.Mode, request.PcInterfaceName, request.PcInterfaceNumber, request.TargetInterface));
        Assert.Equal("PLC_PW", Assert.Single(request.Passwords).Variable);
        Assert.Equal("Online", run.Document.GetProperty("result").GetProperty("value").GetProperty("stateAfter").GetString());
        Assert.Equal("user", Assert.Single(run.Audit.Records).Confirmation.By);
    }

    [Fact]
    public async Task GoOnline_FullRunsWithoutPrompt_AndDeclineDeniesInReadWrite()
    {
        var full = await RunAsync(new Script().Domain(), GoOnline(), McpAccessMode.Full);
        Assert.Equal(0, full.Prompts);
        Assert.True(full.Document.GetProperty("success").GetBoolean());

        var script = new Script();
        var declined = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.ReadWrite, answer: "decline");
        Assert.Equal(WorkerFailureCategories.AccessDenied, declined.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(script.Actions);
    }

    [Theory]
    [InlineData(Hardware, null, WorkerFailureCategories.TargetAmbiguous)]
    [InlineData(Hardware, "1 X9", WorkerFailureCategories.TargetNotFound)]
    [InlineData("PLCSIM", "1 X2", WorkerFailureCategories.TargetNotFound)]
    public async Task GoOnline_ResolvesTargetsExactly(string pc, string? target, string category)
    {
        var script = new Script();
        var run = await RunAsync(script.Domain(), GoOnline(pc: pc, target: target), McpAccessMode.Full);

        Assert.Equal(category, run.Document.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(script.Actions);
    }

    [Fact]
    public async Task GoOnline_PlcSelectionIsExact()
    {
        var script = new Script { Inventory = new() { Plcs = { Plc("PLC_1", "Station_1"), Plc("PLC_2", "Station_2") } } };
        var ambiguous = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ambiguous.Document.GetProperty("error").GetProperty("category").GetString());

        var noProvider = new Script { Inventory = new() { Plcs = { Plc("PLC_3", "Station_3", provider: false) } } };
        var unsupported = await RunAsync(noProvider.Domain(), GoOnline(), McpAccessMode.Full);
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, unsupported.Document.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task GoOnline_NotReachedIsAnAttemptedFailureWithEvidence()
    {
        var script = new Script { Act = (method, request) => WorkerCallResult.Ok(WorkerJson.SerializePayload(Result(method, request, reached: false, after: "NotReachable"))) };
        var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);

        Assert.Equal("applied", run.Document.GetProperty("phase").GetString());
        Assert.False(run.Document.GetProperty("success").GetBoolean());
        Assert.False(run.Raw.IsError == true);
        var result = run.Document.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Contains("NotReachable", result.GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal("NotReachable", result.GetProperty("value").GetProperty("stateAfter").GetString());
    }

    [Fact]
    public async Task GoOnline_WorkerEvidenceForAnotherPathIsAProtocolError()
    {
        var script = new Script
        {
            Act = (method, request) => WorkerCallResult.Ok(WorkerJson.SerializePayload(Result(method, new PlcOnlineRequestInfo
            {
                PlcName = request.PlcName, DeviceName = request.DeviceName, Mode = request.Mode, PcInterfaceName = request.PcInterfaceName,
                PcInterfaceNumber = request.PcInterfaceNumber, TargetInterface = "1 X9", TimeoutSeconds = request.TimeoutSeconds,
            }))),
        };
        var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);

        Assert.Equal(WorkerFailureCategories.ProtocolError, run.Document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, run.Document.GetProperty("result").GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task GoOnline_EvidenceUnderAnotherConfirmationPolicyOrWithInconsistentCountsIsAProtocolError()
    {
        foreach (var tamper in new Action<PlcOnlineActionResultInfo>[]
                 {
                     r => r.PortalConfirmationPolicy = PlcDownloadPolicy.PortalConfirmationPolicies.Accept,
                     r => r.PortalConfirmationsObserved = 2,
                     r => r.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "T", Answer = "Yes" }),
                     r => r.PortalConfirmationsComplete = false,
                 })
        {
            var script = new Script
            {
                Act = (method, request) =>
                {
                    var result = Result(method, request);
                    tamper(result);
                    return WorkerCallResult.Ok(WorkerJson.SerializePayload(result));
                },
            };
            var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);
            Assert.Equal(WorkerFailureCategories.ProtocolError, run.Document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        }
    }

    [Fact]
    public async Task GoOnline_PassesThePolicyToTheWorker_AndTheConfirmationPromptNamesIt()
    {
        PlcOnlineRequestInfo? sent = null;
        var script = new Script
        {
            Act = (method, request) =>
            {
                sent = request;
                var result = Result(method, request);
                result.PortalConfirmationPolicy = request.PortalConfirmations;
                return WorkerCallResult.Ok(WorkerJson.SerializePayload(result));
            },
        };
        var run = await RunAsync(script.Domain(), GoOnline() with { PortalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Accept },
            McpAccessMode.ReadWrite);

        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.Equal(PlcDownloadPolicy.PortalConfirmationPolicies.Accept, sent!.PortalConfirmations);
        Assert.Contains("answered Yes (portalConfirmations: accept)", run.PromptMessage);
    }

    [Fact]
    public void OnlineActionEvidence_NeverDecodesAsPlcDownloadEvidence()
    {
        // Both travel on WorkerResponse.PlcDownloadOutcome; each domain decodes it with its own strict contract,
        // so evidence of one can never be read as the other (download contract 2.0 requires its own members).
        var request = new PlcOnlineRequestInfo
        {
            PlcName = "PLC_1", DeviceName = "Station_1", Mode = "PN/IE", PcInterfaceName = "PLCSIM", PcInterfaceNumber = 1, TargetInterface = "1 X1",
        };
        var evidence = WorkerJson.SerializePayload(Result(PlcOnlineMethods.GoOnline, request));
        var planned = new TiaMcpServer.PlcDownload.PlcDownloadEffects("PLC_1", "Station_1",
            new PlcDownloadPathInfo { Mode = "PN/IE", PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = "PLCSIM", Number = 1 }, TargetInterface = "1 X1" },
            false, PlcDownloadPolicy.Scopes.HardwareAndSoftware, "Hardware, Software", true, true,
            Array.Empty<PlcDownloadDecisionInfo>(), Array.Empty<PlcDownloadPasswordInfo>(), PlcDownloadPolicy.PortalConfirmationPolicies.Decline,
            Array.Empty<string>());
        Assert.ThrowsAny<JsonException>(() => TiaMcpServer.PlcDownload.PlcDownloadPayloadContract.DecodeResult(evidence, planned, requireDownloadResult: false));
    }

    [Fact]
    public async Task GoOnline_AlreadyOnlineReportsNoPathAndAnUnverifiedConnection()
    {
        var script = new Script
        {
            Act = (method, request) =>
            {
                var result = Result(method, request);
                result.Path = null;
                result.Outcome = PlcOnlinePolicy.ActionOutcomes.AlreadyOnline;
                result.PathVerified = false;
                result.Note = "The PLC was already online.";
                return WorkerCallResult.Ok(WorkerJson.SerializePayload(result));
            },
        };
        var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);
        var value = run.Document.GetProperty("result").GetProperty("value");
        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.Equal("already_online", value.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("path").ValueKind);
        Assert.False(value.GetProperty("pathVerified").GetBoolean());
        Assert.Contains(run.Document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("already online"));
    }

    [Fact]
    public async Task GoOnline_EvidenceThatClaimsAPathOrVerificationItDidNotApplyIsAProtocolError()
    {
        foreach (var tamper in new Action<PlcOnlineActionResultInfo>[]
                 {
                     r => { r.Outcome = PlcOnlinePolicy.ActionOutcomes.AlreadyOnline; r.PathVerified = false; },
                     r => r.PathVerified = false,
                     r => { r.Outcome = PlcOnlinePolicy.ActionOutcomes.NotReached; },
                     r => r.Outcome = PlcOnlinePolicy.ActionOutcomes.Disconnected,
                 })
        {
            var script = new Script
            {
                Act = (method, request) =>
                {
                    var result = Result(method, request);
                    tamper(result);
                    return WorkerCallResult.Ok(WorkerJson.SerializePayload(result));
                },
            };
            var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);
            Assert.Equal(WorkerFailureCategories.ProtocolError, run.Document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        }
    }

    [Fact]
    public async Task GoOnline_AbortEvidenceTravelsWithTheFailure()
    {
        var script = new Script
        {
            Act = (method, request) =>
            {
                var evidence = Result(method, request, reached: false, after: "Offline");
                evidence.ConfigurationsSeen.Add(new PlcCallbackConfigurationInfo
                {
                    Type = "Siemens.Engineering.Online.Configurations.TlsVerificationConfiguration",
                    CanonicalType = "Siemens.Engineering.Online.Configurations.TlsVerificationConfiguration", DecisionKey = "TlsVerificationConfiguration",
                    Kind = PlcCallbackPolicy.Kinds.Selection, AvailableSelections = { "NonVerified", "Trusted", "NonTrusted" }, Decision = PlcCallbackPolicy.Decisions.Aborted,
                });
                evidence.Abort = new PlcCallbackAbortInfo
                {
                    Type = evidence.ConfigurationsSeen[0].Type, DecisionKey = "TlsVerificationConfiguration", Kind = PlcCallbackPolicy.Kinds.Selection,
                    AvailableSelections = { "NonVerified", "Trusted", "NonTrusted" }, Reason = PlcCallbackPolicy.AbortReasons.DecisionMissing, Detail = "no decision",
                };
                return WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "Aborted") with { PlcDownloadOutcome = WorkerJson.SerializePayload(evidence) };
            },
        };
        var run = await RunAsync(script.Domain(), GoOnline(), McpAccessMode.Full);

        var value = run.Document.GetProperty("result").GetProperty("value");
        Assert.Equal("decision_missing", value.GetProperty("abort").GetProperty("reason").GetString());
        Assert.Equal(3, value.GetProperty("abort").GetProperty("availableSelections").GetArrayLength());
    }

    [Fact]
    public async Task GoOffline_FiresTheDisconnectGuardAndTakesNoPath()
    {
        var script = new Script { Inventory = new() { Plcs = { Plc("PLC_2", "Station_2", state: "Online") } } };
        var run = await RunAsync(script.Domain(), GoOffline(), McpAccessMode.ReadWrite);

        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { PlcOnlineGuardDefinitions.Disconnects }, GuardIds(run.Document));
        Assert.Contains("including one opened in the TIA Portal UI", run.PromptMessage);
        var (method, request) = Assert.Single(script.Actions);
        Assert.Equal(PlcOnlineMethods.GoOffline, method);
        Assert.Null(request.TargetInterface);
        Assert.Equal(JsonValueKind.Null, run.Document.GetProperty("effects").GetProperty("path").ValueKind);
    }

    [Fact]
    public void Validate_RejectsShapesThatDoNotFitTheAction()
    {
        var domain = new Script().Domain();
        Assert.Contains("requires pcInterface", Validate(domain, GoOnline() with { PcInterface = null }));
        Assert.Contains("go_offline takes only", Validate(domain, GoOffline() with { TargetInterface = "1 X1" }));
        Assert.Contains("go_offline takes only", Validate(domain, GoOffline() with { Mode = "PN/IE" }));
        Assert.Contains("action must be one of", Validate(domain, GoOnline() with { Action = "go_sideways", Operation = "" }));
        Assert.Contains("timeoutSeconds", Validate(domain, GoOnline(timeout: 0)));
        Assert.Contains("environment variable", Validate(domain, GoOnline(passwords: ("OnlinePasswordConfiguration", "not a name"))));
        Assert.Equal(WorkerFailureCategories.AccessDenied, domain.Validate(new[] { GoOnline() }, McpAccessMode.ReadOnly).Error!.Category);
        Assert.True(domain.Validate(new[] { GoOnline() }, McpAccessMode.ReadWrite).IsValid);
    }

    private static string Validate(PlcOnlineConnectionWriteDomain domain, PlcOnlineConnectionItem item)
        => domain.Validate(new[] { item }, McpAccessMode.Full).Error?.Message ?? "valid";

    // plc_station_upload

    private sealed class UploadScript
    {
        public PlcStationUploadInterfacesInfo Interfaces { get; set; } = new()
        {
            Available = true,
            Modes = { new PlcConnectionModeInfo { Name = "PN/IE", PcInterfaces = { new() { Name = "PLCSIM", Number = 1 }, new() { Name = Hardware, Number = 1 } } } },
        };

        public Func<PlcOnlineRequestInfo, WorkerCallResult>? Upload { get; set; }
        public List<PlcOnlineRequestInfo> Uploads { get; } = new();

        public PlcStationUploadWriteDomain Domain() => new(
            () => Task.FromResult(WorkerCallResult.Ok(WorkerJson.SerializePayload(Interfaces))),
            request =>
            {
                Uploads.Add(request);
                return Task.FromResult(Upload?.Invoke(request) ?? WorkerCallResult.Ok(WorkerJson.SerializePayload(Uploaded(request))));
            });
    }

    private static PlcStationUploadResultInfo Uploaded(PlcOnlineRequestInfo request)
    {
        var result = new PlcStationUploadResultInfo
        {
            Address = request.Address!, State = "Success", ErrorCount = 0, UploadedStationName = "Uploaded_Station",
            Path = new PlcConnectionPathInfo { Mode = request.Mode!, PcInterface = new() { Name = request.PcInterfaceName!, Number = request.PcInterfaceNumber } },
        };
        foreach (var decision in request.Decisions)
            result.ConfigurationsSeen.Add(new PlcCallbackConfigurationInfo
            {
                Type = "Siemens.Engineering.Upload.Configurations." + decision.Type, CanonicalType = "Siemens.Engineering.Upload.Configurations." + decision.Type,
                DecisionKey = decision.Type, Kind = PlcCallbackPolicy.Kinds.Selection, AvailableSelections = { "NoAction", "TryUpload" },
                Decision = PlcCallbackPolicy.Decisions.Selected, Selection = decision.Selection,
            });
        return result;
    }

    private static PlcStationUploadWriteItem Upload(string address = "192.168.0.10", string pc = Hardware, params (string Type, string Selection)[] decisions) => new()
    {
        Address = address, PcInterface = new PlcOnlinePcInterfaceArgument { Name = pc, Number = 1 },
        Decisions = decisions.Select(d => new PlcCallbackDecisionInfo { Type = d.Type, Selection = d.Selection }).ToArray(),
    };

    [Fact]
    public async Task StationUpload_ConfirmsAndAddsADevice()
    {
        var script = new UploadScript();
        var run = await RunAsync(script.Domain(), Upload(decisions: ("UploadMissingProducts", "NoAction")), McpAccessMode.ReadWrite);

        Assert.True(run.Document.GetProperty("success").GetBoolean());
        Assert.Equal(1, run.Prompts);
        Assert.Contains("add it as a new device", run.PromptMessage);
        Assert.Equal(new[] { PlcOnlineGuardDefinitions.AddsDevice }, GuardIds(run.Document));
        var request = Assert.Single(script.Uploads);
        Assert.Equal(("192.168.0.10", "PN/IE", Hardware, 1), (request.Address, request.Mode, request.PcInterfaceName, request.PcInterfaceNumber));
        Assert.Equal("Uploaded_Station", run.Document.GetProperty("result").GetProperty("value").GetProperty("uploadedStationName").GetString());
        Assert.Contains(run.Document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("save_project"));
    }

    [Fact]
    public async Task StationUpload_DryRunDoesNotConnect()
    {
        var script = new UploadScript();
        var run = await RunAsync(script.Domain(), Upload(), McpAccessMode.Full, dryRun: true);

        Assert.Equal("preview", run.Document.GetProperty("phase").GetString());
        Assert.True(run.Document.GetProperty("effects").GetProperty("addsDeviceToProject").GetBoolean());
        Assert.Empty(script.Uploads);
    }

    [Fact]
    public async Task StationUpload_UnknownPcInterfaceIsNotFound_AndUnavailableProviderUnsupported()
    {
        var missing = await RunAsync(new UploadScript().Domain(), Upload(pc: "CP 5711"), McpAccessMode.Full);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.Document.GetProperty("error").GetProperty("category").GetString());

        var unavailable = await RunAsync(new UploadScript { Interfaces = new() { Available = false } }.Domain(), Upload(), McpAccessMode.Full);
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, unavailable.Document.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task StationUpload_ASelectionTheCallerDidNotGiveIsAProtocolError()
    {
        var script = new UploadScript
        {
            Upload = request =>
            {
                var forged = Uploaded(request);
                forged.ConfigurationsSeen[0].Selection = "TryUpload";
                return WorkerCallResult.Ok(WorkerJson.SerializePayload(forged));
            },
        };
        var run = await RunAsync(script.Domain(), Upload(decisions: ("UploadMissingProducts", "NoAction")), McpAccessMode.Full);

        Assert.Equal(WorkerFailureCategories.ProtocolError, run.Document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task StationUpload_ErrorStateOrMissingStationFails()
    {
        var script = new UploadScript
        {
            Upload = request =>
            {
                var failed = Uploaded(request);
                failed.State = "Error";
                failed.ErrorCount = 2;
                failed.UploadedStationName = null;
                return WorkerCallResult.Ok(WorkerJson.SerializePayload(failed));
            },
        };
        var run = await RunAsync(script.Domain(), Upload(), McpAccessMode.Full);

        Assert.False(run.Document.GetProperty("success").GetBoolean());
        Assert.Contains("no uploaded station", run.Document.GetProperty("result").GetProperty("failure").GetProperty("message").GetString());
        Assert.Contains(run.Document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("Inspect the project tree"));
    }

    [Fact]
    public void StationUpload_ValidateRejectsBadInput()
    {
        var domain = new UploadScript().Domain();
        Assert.Contains("address", domain.Validate(new[] { Upload(address: "") }, McpAccessMode.Full).Error!.Message);
        Assert.Contains("enum member", domain.Validate(new[] { Upload(decisions: ("UploadMissingProducts", "No Action")) }, McpAccessMode.Full).Error!.Message);
        Assert.Equal(WorkerFailureCategories.AccessDenied, domain.Validate(new[] { Upload() }, McpAccessMode.ReadOnly).Error!.Category);
    }
}
