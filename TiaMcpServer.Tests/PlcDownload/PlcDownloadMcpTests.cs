using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// plc_download and plc_read list_download_targets end to end against the scripted FakeWorker
/// (plc-download-roundtrip, -abort, -error and -confirmation scenarios), whose stand-in configuration
/// objects are answered by the production reflection access. Like every FakeWorker protocol test these
/// launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcDownloadMcpTests
{
    private const string Roundtrip = "plc-download-roundtrip";

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly FakeWorkerRequestLog _requests;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        private Session(string scenario)
        {
            Directory.CreateDirectory(_audit.Path);
            _requests = new FakeWorkerRequestLog(_audit.Path);
            _uiOpen = new FakeWorkerUiOpenProject(scenario);
        }

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public int Prompts { get; private set; }

        public string[] Methods => _requests.Methods();

        public string[] AuditLines => NetworkGuardedWriteMcpTests.AuditLines(_audit.Path);

        public static async Task<Session> StartAsync(McpAccessMode mode, string scenario = Roundtrip, string? elicitation = null)
        {
            var session = new Session(scenario);
            McpClientOptions? options = elicitation is null ? null : new()
            {
                Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (_, _) =>
                    {
                        session.Prompts++;
                        return ValueTask.FromResult(new ElicitResult
                        {
                            Action = elicitation,
                            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
                        });
                    },
                },
            };
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, scenario, clientOptions: options);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            return (raw.StructuredContent!.Value, raw);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public async Task PlcRead_ListsTheOfflineDownloadPath_InReadOnly()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var (document, raw) = await session.CallAsync("plc_read", Args(("operations", new object[]
        {
            new { operationId = "all", operation = "list_download_targets" },
            new { operationId = "one", operation = "list_download_targets", plcName = "plc_2" },
        })));

        Assert.False(raw.IsError == true);
        var operations = document.GetProperty("batch").GetProperty("operations");
        var all = operations[0].GetProperty("result").GetProperty("plcs");
        Assert.Equal(new[] { "PLC_1", "PLC_2", "PLC_3" }, all.EnumerateArray().Select(plc => plc.GetProperty("plcName").GetString()));
        Assert.False(all[2].GetProperty("downloadable").GetBoolean());
        var plcsim = all[0].GetProperty("modes")[0].GetProperty("pcInterfaces")[0];
        Assert.Equal("PLCSIM", plcsim.GetProperty("name").GetString());
        Assert.Equal(1, plcsim.GetProperty("number").GetInt32());
        Assert.Equal("1 X1", plcsim.GetProperty("targetInterfaces")[0].GetProperty("name").GetString());
        Assert.Equal("PLC_2", Assert.Single(operations[1].GetProperty("result").GetProperty("plcs").EnumerateArray())
            .GetProperty("plcName").GetString());
        Assert.DoesNotContain("plc_download", session.Methods);
    }

    [Fact]
    public async Task DryRun_InReadWrite_PreviewsWithoutPromptOrDownload()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("dryRun", true)));

        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean());
        Assert.Equal("1 X1", document.GetProperty("effects").GetProperty("path").GetProperty("targetInterface").GetString());
        Assert.False(Assert.Single(document.GetProperty("guards").EnumerateArray()).GetProperty("acknowledged").GetBoolean());
        Assert.Equal(0, session.Prompts);
        Assert.Contains("list_download_targets", session.Methods);
        Assert.DoesNotContain("plc_download", session.Methods);
        Assert.Single(session.AuditLines);
    }

    [Fact]
    public async Task Full_DownloadsToPlcsim_AndNeverClaimsCompletion()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, elicitation: "accept");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("scope", "software")));

        Assert.False(raw.IsError == true);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(0, session.Prompts);
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("Success", value.GetProperty("state").GetString());
        Assert.Equal("Software", value.GetProperty("downloadOptions").GetString());
        Assert.False(value.GetProperty("completionVerified").GetBoolean());
        Assert.Equal(new[] { "StopAll", "ConsistentDownload", "PlcSimulationAdvanced", "StartModule" },
            value.GetProperty("configurationsSeen").EnumerateArray().Select(seen => seen.GetProperty("selection").GetString()));
        Assert.All(value.GetProperty("configurationsSeen").EnumerateArray(), seen => Assert.Equal("default", seen.GetProperty("source").GetString()));
        Assert.Equal(new[] { "CPU", "PlcSimulationAdvanced" }, value.GetProperty("configurationsSeen")[2].GetProperty("availableSelections")
            .EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(0, value.GetProperty("portalConfirmations").GetArrayLength());
        Assert.Single(session.Methods, method => method == "plc_download");
        var record = JsonDocument.Parse(Assert.Single(session.AuditLines)).RootElement;
        Assert.Equal("policy", record.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.Equal("applied", record.GetProperty("phase").GetString());
    }

    [Theory]
    [InlineData("accept", true)]
    [InlineData("decline", false)]
    [InlineData(null, false)]
    public async Task ReadWrite_ConfirmsEveryActualDownload(string? elicitation, bool applies)
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: elicitation);
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("startAfterDownload", false)));

        Assert.Equal(elicitation is null ? 0 : 1, session.Prompts);
        Assert.Equal(applies ? "applied" : "blocked", document.GetProperty("phase").GetString());
        Assert.Equal(!applies, raw.IsError == true);
        Assert.Equal(applies ? 1 : 0, session.Methods.Count(method => method == "plc_download"));
        if (applies)
            Assert.Equal("NoAction", document.GetProperty("result").GetProperty("value").GetProperty("configurationsSeen")[3]
                .GetProperty("selection").GetString());
        else
            Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
    }

    private static (string, object?) HardwareInterface(int number = 1) => ("pcInterface", new { name = "Intel(R) Ethernet Connection", number });

    [Fact]
    public async Task Hardware_InFull_DownloadsWithoutPrompt_AndAnswersTargetForSoftwareCpu()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, elicitation: "accept");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), HardwareInterface(), ("targetInterface", "1 X1")));

        Assert.False(raw.IsError == true);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(0, session.Prompts);
        Assert.False(document.GetProperty("effects").GetProperty("simulationTarget").GetBoolean());
        Assert.Contains(document.GetProperty("guards").EnumerateArray(),
            guard => guard.GetProperty("id").GetString() == "download_to_hardware"
                && guard.GetProperty("severity").GetString() == "acknowledge" && guard.GetProperty("acknowledged").GetBoolean());
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("Intel(R) Ethernet Connection", value.GetProperty("path").GetProperty("pcInterface").GetProperty("name").GetString());
        Assert.Equal("CPU", value.GetProperty("configurationsSeen")[2].GetProperty("selection").GetString());
        Assert.Single(session.Methods, method => method == "plc_download");
        var record = JsonDocument.Parse(Assert.Single(session.AuditLines)).RootElement;
        Assert.Equal("policy", record.GetProperty("confirmation").GetProperty("by").GetString());
    }

    [Theory]
    [InlineData("accept", true)]
    [InlineData("decline", false)]
    public async Task Hardware_InReadWrite_AsksOncePerCall(string elicitation, bool applies)
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: elicitation);
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), HardwareInterface(), ("targetInterface", "1 X2")));

        Assert.Equal(1, session.Prompts);
        Assert.Equal(applies ? "applied" : "blocked", document.GetProperty("phase").GetString());
        Assert.Equal(applies ? 1 : 0, session.Methods.Count(method => method == "plc_download"));
    }

    [Fact]
    public async Task Hardware_DryRun_ShowsTheGuardWithoutDownloading()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), HardwareInterface(), ("targetInterface", "1 X1"), ("dryRun", true)));

        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.Equal(new[] { "download_to_hardware", "download_stops_plc" },
            document.GetProperty("guards").EnumerateArray().Select(guard => guard.GetProperty("id").GetString()));
        Assert.Equal(0, session.Prompts);
        Assert.DoesNotContain("plc_download", session.Methods);
    }

    [Fact]
    public async Task AmbiguousTarget_ListsCandidates_AndExplicitTargetResolves()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (ambiguous, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_2"), ("dryRun", true)));
        var error = ambiguous.GetProperty("error");
        Assert.Equal("target_ambiguous", error.GetProperty("category").GetString());
        Assert.Contains("'1 X1'", error.GetProperty("message").GetString());
        Assert.Contains("'1 X2'", error.GetProperty("message").GetString());

        var (explicitTarget, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_2"), ("targetInterface", "1 X2"), ("dryRun", true)));
        Assert.Equal("preview", explicitTarget.GetProperty("phase").GetString());
        Assert.False(explicitTarget.GetProperty("effects").GetProperty("targetInterfaceSelectedAutomatically").GetBoolean());

        var (several, _) = await session.CallAsync("plc_download", Args(("dryRun", true)));
        Assert.Equal("target_ambiguous", several.GetProperty("error").GetProperty("category").GetString());
        var (notDownloadable, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_3"), ("dryRun", true)));
        Assert.Equal("target_kind_unsupported", notDownloadable.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("plc_download", session.Methods);
    }

    [Fact]
    public async Task ExplicitNullPlcNameAndTargetInterface_MeanOmitted()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (several, _) = await session.CallAsync("plc_download", Args(("plcName", null), ("dryRun", true)));
        Assert.Equal("target_ambiguous", several.GetProperty("error").GetProperty("category").GetString());

        var (automatic, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("targetInterface", null), ("dryRun", true)));
        Assert.Equal("preview", automatic.GetProperty("phase").GetString());
        Assert.True(automatic.GetProperty("effects").GetProperty("targetInterfaceSelectedAutomatically").GetBoolean());
        Assert.DoesNotContain("plc_download", session.Methods);
    }

    [Fact]
    public async Task PortalConfirmationDuringDownload_IsDeclined_ReportedAndFailsTheCall()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-confirmation");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1")));

        Assert.False(raw.IsError == true);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        var result = document.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Contains("answered each one No", result.GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal(1, result.GetProperty("value").GetProperty("portalConfirmationsObserved").GetInt32());
        Assert.True(result.GetProperty("value").GetProperty("portalConfirmationsComplete").GetBoolean());
        var confirmation = Assert.Single(result.GetProperty("value").GetProperty("portalConfirmations").EnumerateArray());
        Assert.Equal("No", confirmation.GetProperty("answer").GetString());
        Assert.Equal("Siemens.Engineering.ConfirmationEventArgs", confirmation.GetProperty("type").GetString());
        Assert.Equal("Success", result.GetProperty("value").GetProperty("state").GetString());
    }

    [Fact]
    public async Task PortalConfirmationDuringDownload_AcceptPolicy_AnswersYes_AndSucceedsWithAWarning()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-confirmation");
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("portalConfirmations", "accept")));

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("accept", value.GetProperty("portalConfirmationPolicy").GetString());
        Assert.Equal("Yes", Assert.Single(value.GetProperty("portalConfirmations").EnumerateArray()).GetProperty("answer").GetString());
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("each was answered Yes"));
    }

    [Fact]
    public async Task UndecidedConfiguration_AbortsWithWhatARetryNeeds()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-abort");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1")));

        Assert.False(raw.IsError == true);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        var result = document.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal("worker_operation_failed", result.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("decisions: { \"DataBlockReinitialization\": \"<selection>\" } using one of: StopPlcAndReinitialize, NoAction",
            result.GetProperty("failure").GetProperty("message").GetString());
        var value = result.GetProperty("value");
        var abort = value.GetProperty("abort");
        Assert.Equal("Siemens.Engineering.Download.Configurations.DataBlockReinitialization", abort.GetProperty("type").GetString());
        Assert.Equal(("decision_required", "DataBlockReinitialization", "selection"),
            (abort.GetProperty("reason").GetString(), abort.GetProperty("decisionKey").GetString(), abort.GetProperty("kind").GetString()));
        Assert.Equal(new[] { "StopPlcAndReinitialize", "NoAction" }, abort.GetProperty("availableSelections").EnumerateArray().Select(name => name.GetString()));
        Assert.Equal(JsonValueKind.Null, value.GetProperty("state").ValueKind);
        Assert.Equal(new[] { "selected", "aborted" },
            value.GetProperty("configurationsSeen").EnumerateArray().Select(seen => seen.GetProperty("decision").GetString()));
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("Inspect it before retrying"));
        Assert.Single(session.AuditLines);
    }

    [Fact]
    public async Task InvalidSelectionName_AbortsListingTheValidNames()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-abort");
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
            ("decisions", new Dictionary<string, object> { ["DataBlockReinitialization"] = "KeepActualValues" })));

        var abort = document.GetProperty("result").GetProperty("value").GetProperty("abort");
        Assert.Equal("selection_unavailable", abort.GetProperty("reason").GetString());
        Assert.Equal(new[] { "StopPlcAndReinitialize", "NoAction" }, abort.GetProperty("availableSelections").EnumerateArray().Select(name => name.GetString()));
        Assert.Contains("Available: StopPlcAndReinitialize, NoAction", document.GetProperty("result").GetProperty("failure").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Retrying_WithDecisionsAndAPasswordVariable_Succeeds_WithoutLeakingThePassword()
    {
        const string variable = "TIA_MCP_TEST_PLC_DOWNLOAD_PW";
        const string secret = "Wr1te-Acc3ss-P@ss";
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "plc-download-abort", elicitation: "accept");

            // Step by step, as an agent would retry: each abort names the next configuration to decide.
            var (first, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
                ("decisions", new Dictionary<string, object> { ["DataBlockReinitialization"] = "NoAction" })));
            var check = first.GetProperty("result").GetProperty("value").GetProperty("abort");
            Assert.Equal(("UpgradeTargetDevice", "check", "decision_required"),
                (check.GetProperty("decisionKey").GetString(), check.GetProperty("kind").GetString(), check.GetProperty("reason").GetString()));

            var (second, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
                ("decisions", new Dictionary<string, object> { ["DataBlockReinitialization"] = "NoAction", ["UpgradeTargetDevice"] = true })));
            var password = second.GetProperty("result").GetProperty("value").GetProperty("abort");
            Assert.Equal(("ModuleWriteAccessPassword", "password"), (password.GetProperty("decisionKey").GetString(), password.GetProperty("kind").GetString()));
            Assert.Contains("passwords: { \"ModuleWriteAccessPassword\":", second.GetProperty("result").GetProperty("failure").GetProperty("message").GetString());

            var (done, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
                ("decisions", new Dictionary<string, object> { ["DataBlockReinitialization"] = "NoAction", ["UpgradeTargetDevice"] = true }),
                ("passwords", new Dictionary<string, object> { ["ModuleWriteAccessPassword"] = variable })));

            Assert.True(done.GetProperty("success").GetBoolean(), done.GetRawText());
            Assert.Equal(3, session.Prompts);
            var seen = done.GetProperty("result").GetProperty("value").GetProperty("configurationsSeen").EnumerateArray().ToArray();
            Assert.Equal(new[] { "selected", "selected", "checked", "password_set", "selected", "selected", "selected" },
                seen.Select(entry => entry.GetProperty("decision").GetString()));
            Assert.Equal(new[] { "default", "caller", "caller", "caller", "default", "default", "default" },
                seen.Select(entry => entry.GetProperty("source").GetString()));
            Assert.Equal(variable, seen[3].GetProperty("passwordVariable").GetString());
            Assert.Equal(variable, done.GetProperty("effects").GetProperty("passwords")[0].GetProperty("variable").GetString());
            foreach (var text in session.AuditLines.Append(raw.Content.OfType<TextContentBlock>().Single().Text))
                Assert.DoesNotContain(secret, text);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task PasswordVariableNotSet_IsRefusedBeforeTheWorkerIsAsked()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-abort");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
            ("passwords", new Dictionary<string, object> { ["ModuleWriteAccessPassword"] = "TIA_MCP_TEST_UNSET_VARIABLE_4711" })));

        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        // A "name" whose variable is unset may be a pasted password: it is redacted while binding, never echoed.
        Assert.Contains("passwords.ModuleWriteAccessPassword: the environment variable named for this configuration is not set",
            document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("TIA_MCP_TEST_UNSET_VARIABLE_4711", document.GetRawText());
        Assert.DoesNotContain("TIA_MCP_TEST_UNSET_VARIABLE_4711", string.Join("\n", session.AuditLines));
        Assert.DoesNotContain("plc_download", session.Methods);
        Assert.DoesNotContain("list_download_targets", session.Methods);
    }

    [Fact]
    public async Task ErrorState_IsAFailedDownloadWithMessages()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-error");
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1")));

        Assert.False(document.GetProperty("success").GetBoolean());
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("Error", value.GetProperty("state").GetString());
        Assert.Equal(1, value.GetProperty("errorCount").GetInt32());
        Assert.Equal("Download failed.", value.GetProperty("messages")[0].GetProperty("message").GetString());
    }
}
