using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// The compare, online-state, plc_tia_online, plc_station_upload, watch/force table and update_program
/// operations end to end against the scripted FakeWorker (plc-online-roundtrip). Like every FakeWorker
/// protocol test these launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcOnlineMcpTests
{
    private const string Scenario = "plc-online-roundtrip";

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly FakeWorkerRequestLog _requests;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        private readonly string _scenario;

        private Session(string scenario)
        {
            _scenario = scenario;
            Directory.CreateDirectory(_audit.Path);
            _requests = new FakeWorkerRequestLog(_audit.Path);
            _uiOpen = new FakeWorkerUiOpenProject(scenario);
        }

        /// <summary>Every audit line written so far, joined.</summary>
        public string AuditText => string.Join("\n", NetworkGuardedWriteMcpTests.AuditLines(_audit.Path));

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public int Prompts { get; private set; }

        public string[] Methods => _requests.Methods();

        public static async Task<Session> StartAsync(McpAccessMode mode, string? elicitation = null, string scenario = Scenario)
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
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, session._scenario, clientOptions: options);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            return (raw.StructuredContent!.Value, raw);
        }

        public async Task<JsonElement[]> ReadAsync(params object[] operations)
        {
            var (document, raw) = await CallAsync("plc_read", Args(("operations", operations)));
            Assert.False(raw.IsError == true);
            return document.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public async Task ReadOnly_ComparesHardwareOfflineAndOnline_AndRefusesATargetWithoutIHardwareCompareTarget()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var items = await session.ReadAsync(
            new { operationId = "plc", operation = "compare_hardware", plcName = "PLC_1", compareTarget = "plc", targetName = "PLC_2" },
            new { operationId = "cpu", operation = "compare_hardware", plcName = "PLC_1", compareTarget = "plc", targetName = "PLC_2", hardwareScope = "cpu", differencesOnly = false },
            new { operationId = "copy", operation = "compare_hardware", plcName = "PLC_1", compareTarget = "master_copy", targetName = "StationTemplate" },
            new { operationId = "online", operation = "compare_hardware_to_online", plcName = "PLC_2" },
            new { operationId = "none", operation = "compare_hardware_to_online", plcName = "PLC_3" });

        var station = items[0].GetProperty("result");
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("Station_1", station.GetProperty("root").GetProperty("leftName").GetString());
        Assert.Equal("Station_2", station.GetProperty("root").GetProperty("rightName").GetString());
        Assert.False(station.GetProperty("identical").GetBoolean());
        Assert.Equal(1, station.GetProperty("identicalElementsSkipped").GetInt32());
        Assert.Equal("PLC_1", items[1].GetProperty("result").GetProperty("root").GetProperty("leftName").GetString());
        Assert.Equal("unsupported_capability", items[2].GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("IHardwareCompareTarget", items[2].GetProperty("failure").GetProperty("message").GetString());
        var online = items[3].GetProperty("result");
        Assert.Equal("compare_hardware_to_online", online.GetProperty("operation").GetString());
        Assert.Equal("online", online.GetProperty("compareTarget").GetString());
        Assert.Equal("unsupported_capability", items[4].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task ReadOnly_ComparesOnlineAndOffline_ReadsStateAndTables()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var items = await session.ReadAsync(
            new { operationId = "online", operation = "compare_to_online", plcName = "PLC_1" },
            new { operationId = "plc", operation = "compare_software", plcName = "PLC_1", compareTarget = "plc", targetName = "PLC_2", differencesOnly = false },
            new { operationId = "state", operation = "get_online_state" },
            new { operationId = "upload", operation = "list_station_upload_interfaces" },
            new { operationId = "tables", operation = "list_watch_and_force_tables", plcName = "PLC_1" },
            new { operationId = "watch", operation = "read_watch_table", plcName = "PLC_1", tableName = "watch_1" },
            new { operationId = "force", operation = "read_force_table", plcName = "PLC_1", tableName = "Force_1" },
            new { operationId = "xml", operation = "get_watch_table_content", plcName = "PLC_1", tableName = "Watch_1" });

        Assert.All(items, item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        var online = items[0].GetProperty("result");
        Assert.Equal("online", online.GetProperty("compareTarget").GetString());
        Assert.False(online.GetProperty("identical").GetBoolean());
        Assert.True(online.GetProperty("disconnectedAfterCompare").GetBoolean());
        Assert.Equal(2, online.GetProperty("identicalElementsSkipped").GetInt32());
        var blocks = online.GetProperty("root").GetProperty("elements")[0];
        Assert.Equal(new[] { "Main", "Block_3" }, blocks.GetProperty("elements").EnumerateArray().Select(e => e.GetProperty("leftName").GetString()));
        Assert.Equal(6, items[1].GetProperty("result").GetProperty("elementsListed").GetInt32());
        Assert.Equal("PLC_2", items[1].GetProperty("result").GetProperty("targetName").GetString());
        var plcs = items[2].GetProperty("result").GetProperty("plcs");
        Assert.Equal(new[] { "Offline", "Online", null }, plcs.EnumerateArray().Select(p => p.GetProperty("state").GetString()));
        Assert.True(items[3].GetProperty("result").GetProperty("available").GetBoolean());
        Assert.Equal(new[] { "/Line" }, items[4].GetProperty("result").GetProperty("plcs")[0].GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
        var entries = items[5].GetProperty("result").GetProperty("entries");
        Assert.Equal("%Q0.0", entries[0].GetProperty("address").GetString());
        Assert.True(entries[0].GetProperty("modifyIntention").GetBoolean());
        Assert.Equal("TRUE", items[6].GetProperty("result").GetProperty("entries")[0].GetProperty("forceValue").GetString());
        var xml = items[7].GetProperty("result");
        Assert.Equal("xml", xml.GetProperty("format").GetString());
        Assert.Equal(ContentHashRules.Compute("xml", xml.GetProperty("content").GetString()!), xml.GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task ReadOnly_ReadsStationFingerprintData_AndRefusesAMissingPcInterface()
    {
        // Audit N3 (not live-tested): OnlineObserve, allowed in read-only.
        Environment.SetEnvironmentVariable("FAKE_PLC_PASSWORD", "set-for-the-host-check");
        try
        {
            await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
            var items = await session.ReadAsync(
                new { operationId = "fp", operation = "get_station_fingerprint_data", mode = "PN/IE", pcInterface = new { name = "PLCSIM", number = 1 },
                    targetAddress = "192.168.0.10", onlineUser = new { userName = "Engineer" },
                    passwords = new Dictionary<string, string> { ["OnlineAuthenticationConfiguration"] = "FAKE_PLC_PASSWORD" } },
                new { operationId = "none", operation = "get_station_fingerprint_data", mode = "PN/IE", pcInterface = new { name = "PLCSIM", number = 1 },
                    targetAddress = "192.168.0.250" },
                new { operationId = "one", operation = "get_station_fingerprint_data", mode = "PN/IE", pcInterface = new { name = "PLCSIM", number = 1 },
                    targetAddress = "192.168.0.10", maxElements = 1 });

            var result = items[0].GetProperty("result");
            Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
            Assert.Equal(2, result.GetProperty("itemCount").GetInt32());
            Assert.Equal("HardwareConfiguration", result.GetProperty("items")[0].GetProperty("identifier").GetProperty("value").GetString());
            Assert.Equal("0A1B", result.GetProperty("items")[0].GetProperty("value").GetProperty("value").GetString());
            Assert.Equal("password_set", Assert.Single(result.GetProperty("configurationsSeen").EnumerateArray()).GetProperty("decision").GetString());
            Assert.Equal("failed", items[1].GetProperty("status").GetString());
            Assert.True(items[2].GetProperty("result").GetProperty("truncated").GetBoolean());
            Assert.Single(items[2].GetProperty("result").GetProperty("items").EnumerateArray());

            var (refused, raw) = await session.CallAsync("plc_read", Args(("operations", new[]
            {
                new { operationId = "x", operation = "get_station_fingerprint_data", mode = "PN/IE", targetAddress = "192.168.0.10" },
            })));
            Assert.True(raw.IsError == true);
            Assert.Contains("pcInterface", refused.GetProperty("error").GetProperty("message").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PLC_PASSWORD", null);
        }
    }

    [Fact]
    public async Task ReadOnly_DiscoversNeitherOnlineControlNorUploadTool()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var tools = (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        Assert.DoesNotContain("plc_tia_online", tools);
        Assert.DoesNotContain("plc_station_upload", tools);
        Assert.Contains("plc_read", tools);
    }

    [Fact]
    public async Task GoOnline_ReadWriteConfirmsReachesOnlineAndGoOfflineCloses()
    {
        // Password names bind only when the variable is set in the server's environment (the worker inherits it).
        Environment.SetEnvironmentVariable("FAKE_PLC_PASSWORD", "set-for-the-host-check");
        try
        {
            await GoOnlineAndOffline();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PLC_PASSWORD", null);
        }
    }

    private static async Task GoOnlineAndOffline()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var (online, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"),
            ("pcInterface", new { name = "PLCSIM", number = 1 }), ("passwords", new Dictionary<string, string> { ["OnlinePasswordConfiguration"] = "FAKE_PLC_PASSWORD" })));

        Assert.True(online.GetProperty("success").GetBoolean());
        Assert.Equal(1, session.Prompts);
        var value = online.GetProperty("result").GetProperty("value");
        Assert.Equal(new[] { "Connecting", "Online" }, value.GetProperty("statesObserved").EnumerateArray().Select(s => s.GetString()));
        var seen = Assert.Single(value.GetProperty("configurationsSeen").EnumerateArray());
        Assert.Equal("password_set", seen.GetProperty("decision").GetString());
        Assert.Equal("FAKE_PLC_PASSWORD", seen.GetProperty("passwordVariable").GetString());
        Assert.DoesNotContain("fake-password", online.GetRawText());

        var (offline, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_offline"), ("plcName", "PLC_1")));
        Assert.True(offline.GetProperty("success").GetBoolean());
        Assert.Equal(2, session.Prompts);
        Assert.Contains("online_disconnects", offline.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        Assert.Contains("get_online_state", session.Methods);
        Assert.Equal(new[] { "plc_go_online", "plc_go_offline" }, session.Methods.Where(method => method.StartsWith("plc_go_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GoOnline_AnswersAUserBasedLogin_OnlyOverASecureChannel()
    {
        // Audit N2 (not live-tested): onlineUser plus passwords["OnlineAuthenticationConfiguration"].
        Environment.SetEnvironmentVariable("FAKE_PLC_PASSWORD", "set-for-the-host-check");
        try
        {
            var arguments = Args(("action", "go_online"), ("plcName", "PLC_1"), ("pcInterface", new { name = "PLCSIM", number = 1 }),
                ("onlineUser", new { userName = "Engineer", userType = "ProjectUser" }),
                ("passwords", new Dictionary<string, string> { ["OnlineAuthenticationConfiguration"] = "FAKE_PLC_PASSWORD" }));
            await using (var session = await Session.StartAsync(McpAccessMode.Full))
            {
                var (online, _) = await session.CallAsync("plc_tia_online", arguments);
                Assert.True(online.GetProperty("success").GetBoolean(), online.GetRawText());
                Assert.Equal("Engineer", online.GetProperty("effects").GetProperty("onlineUser").GetProperty("userName").GetString());
                var seen = Assert.Single(online.GetProperty("result").GetProperty("value").GetProperty("configurationsSeen").EnumerateArray());
                Assert.Equal("OnlineAuthenticationConfiguration", seen.GetProperty("decisionKey").GetString());
                Assert.Equal("password_set", seen.GetProperty("decision").GetString());
                Assert.True(seen.GetProperty("secureCommunication").GetBoolean());
                Assert.DoesNotContain("fake-password", online.GetRawText());

                var (offline, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_offline"), ("plcName", "PLC_1"),
                    ("onlineUser", new { userName = "Engineer" })));
                Assert.True(raw.IsError == true);
                Assert.Contains("onlineUser", offline.GetProperty("error").GetProperty("message").GetString());
            }

            await using var insecure = await Session.StartAsync(McpAccessMode.Full, scenario: "plc-online-insecure-login");
            var (refused, _) = await insecure.CallAsync("plc_tia_online", arguments);
            Assert.False(refused.GetProperty("success").GetBoolean());
            Assert.Equal("insecure_communication", refused.GetProperty("result").GetProperty("value").GetProperty("abort").GetProperty("reason").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PLC_PASSWORD", null);
        }
    }

    [Fact]
    public async Task GoOnline_WithTargetAddress_InformsAndUsesGoOnlineWithAConfigurationAddress()
    {
        // Audit N4 (not live-tested).
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (online, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"),
            ("pcInterface", new { name = "PLCSIM", number = 1 }), ("targetAddress", "192.168.0.77")));

        Assert.True(online.GetProperty("success").GetBoolean(), online.GetRawText());
        Assert.Equal("192.168.0.77", online.GetProperty("effects").GetProperty("targetAddress").GetString());
        Assert.Contains("online_custom_address", online.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        Assert.Contains(online.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("GoOnline(ConfigurationAddress)", StringComparison.Ordinal));

        var (offline, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_offline"), ("plcName", "PLC_1"), ("targetAddress", "192.168.0.77")));
        Assert.True(raw.IsError == true);
        Assert.Contains("targetAddress", offline.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task GoOnline_UnreachableHardwareTargetFailsWithEvidence()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"),
            ("pcInterface", new { name = "Intel(R) Ethernet Connection", number = 1 }), ("targetInterface", "1 X2")));

        Assert.False(raw.IsError == true);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Contains("online_hardware_target", document.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        Assert.Equal("NotReachable", document.GetProperty("result").GetProperty("value").GetProperty("stateAfter").GetString());
    }

    [Fact]
    public async Task GoOnline_ArgumentShapeErrorsAreCanonicalRejections()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (nullPc, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("pcInterface", null)));
        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", nullPc.GetProperty("error").GetProperty("category").GetString());

        var (missing, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1")));
        Assert.Contains("requires pcInterface", missing.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("plc_go_online", session.Methods);
    }

    [Fact]
    public async Task StationUpload_WithoutDecisionAbortsAndListsSelections_ThenSucceedsWithIt()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var pc = new { name = "PLCSIM", number = 1 };
        var (aborted, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", pc)));

        Assert.False(aborted.GetProperty("success").GetBoolean());
        var abort = aborted.GetProperty("result").GetProperty("value").GetProperty("abort");
        Assert.Equal("decision_missing", abort.GetProperty("reason").GetString());
        Assert.Equal("UploadMissingProducts", abort.GetProperty("decisionKey").GetString());
        Assert.Equal(new[] { "NoAction", "TryUpload" }, abort.GetProperty("availableSelections").EnumerateArray().Select(s => s.GetString()));

        var (uploaded, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", pc),
            ("decisions", new Dictionary<string, string> { ["UploadMissingProducts"] = "NoAction" })));
        Assert.True(uploaded.GetProperty("success").GetBoolean());
        var value = uploaded.GetProperty("result").GetProperty("value");
        Assert.Equal("Uploaded_Station", value.GetProperty("uploadedStationName").GetString());
        Assert.Equal("NoAction", value.GetProperty("configurationsSeen")[0].GetProperty("selection").GetString());
        Assert.Contains(uploaded.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("save_project"));
    }

    [Fact]
    public async Task PortalConfirmations_DeclinedByDefaultFailWithEvidence_AcceptedAreRecordedAndWarned()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, scenario: "plc-online-confirmation");
        var pc = new { name = "PLCSIM", number = 1 };

        var (declined, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"), ("pcInterface", pc)));
        Assert.False(raw.IsError == true);
        Assert.Equal("applied", declined.GetProperty("phase").GetString());
        Assert.False(declined.GetProperty("success").GetBoolean());
        Assert.Equal("decline", declined.GetProperty("effects").GetProperty("portalConfirmations").GetString());
        var result = declined.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Contains("answered each one No", result.GetProperty("failure").GetProperty("message").GetString());
        var value = result.GetProperty("value");
        Assert.Equal("decline", value.GetProperty("portalConfirmationPolicy").GetString());
        Assert.Equal(1, value.GetProperty("portalConfirmationsObserved").GetInt32());
        Assert.Equal("No", value.GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());

        var (accepted, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_offline"), ("plcName", "PLC_1"),
            ("portalConfirmations", "accept")));
        Assert.True(accepted.GetProperty("success").GetBoolean());
        Assert.Equal("Yes", accepted.GetProperty("result").GetProperty("value").GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());
        Assert.Contains(accepted.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("answered Yes"));

        var (upload, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", pc),
            ("decisions", new Dictionary<string, string> { ["UploadMissingProducts"] = "NoAction" })));
        Assert.False(upload.GetProperty("success").GetBoolean());
        Assert.Equal("No", upload.GetProperty("result").GetProperty("value").GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());
        var (uploadAccepted, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", pc),
            ("decisions", new Dictionary<string, string> { ["UploadMissingProducts"] = "NoAction" }), ("portalConfirmations", "accept")));
        Assert.True(uploadAccepted.GetProperty("success").GetBoolean());
        Assert.Contains(uploadAccepted.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("answered Yes"));

        var compare = await session.ReadAsync(new { operationId = "c", operation = "compare_to_online", plcName = "PLC_1" });
        Assert.Equal("failed", compare[0].GetProperty("status").GetString());
        Assert.Contains("answered No", compare[0].GetProperty("failure").GetProperty("message").GetString());
    }

    [Fact]
    public async Task PortalConfirmations_UnknownPolicyIsAValidationError()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (online, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_offline"), ("plcName", "PLC_1"), ("portalConfirmations", "always")));
        Assert.Equal("validation_error", online.GetProperty("error").GetProperty("category").GetString());
        var (upload, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"),
            ("pcInterface", new { name = "PLCSIM", number = 1 }), ("portalConfirmations", "always")));
        Assert.Equal("validation_error", upload.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain(session.Methods, method => method is "plc_go_offline" or "plc_station_upload");
    }

    [Fact]
    public async Task Passwords_AnUnsetVariableNameIsRedacted_NeverEchoedOrAudited()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (online, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"),
            ("pcInterface", new { name = "PLCSIM", number = 1 }),
            ("passwords", new Dictionary<string, string> { ["OnlinePasswordConfiguration"] = "PastedPassw0rd" })));
        Assert.True(raw.IsError == true);
        Assert.Contains("the environment variable named for this configuration is not set", online.GetProperty("error").GetProperty("message").GetString());
        var (upload, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"),
            ("pcInterface", new { name = "PLCSIM", number = 1 }),
            ("passwords", new Dictionary<string, string> { ["ModuleReadAccessPassword"] = "PastedPassw0rd" })));
        Assert.Equal("validation_error", upload.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("PastedPassw0rd", online.GetRawText() + upload.GetRawText());
        Assert.DoesNotContain("PastedPassw0rd", session.AuditText);
        Assert.DoesNotContain(session.Methods, method => method is "plc_go_online" or "plc_station_upload");
    }

    [Fact]
    public async Task Passwords_ArePlcDownloadMaps_APastedPasswordIsRedactedAndNeverAudited()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var pc = new { name = "PLCSIM", number = 1 };
        var (pasted, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"), ("pcInterface", pc),
            ("passwords", new Dictionary<string, string> { ["OnlinePasswordConfiguration"] = "S3cret pass!" })));
        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", pasted.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("S3cret pass!", pasted.GetRawText());
        Assert.DoesNotContain("S3cret pass!", session.AuditText);

        var (check, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", pc),
            ("decisions", new Dictionary<string, object> { ["UploadMissingProducts"] = true })));
        Assert.Equal("validation_error", check.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("no check decisions", check.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("plc_station_upload", session.Methods);
    }

    private const string Edited = "<Document><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>Watch_1</Name><Comment>edited</Comment></AttributeList></SW.WatchAndForceTables.PlcWatchTable></Document>";

    [Fact]
    public async Task PlcWrite_EditsWatchTablesThroughTheGuardedPipeline()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var xml = (await session.ReadAsync(new { operationId = "xml", operation = "get_watch_table_content", plcName = "PLC_1", tableName = "Watch_1" }))[0]
            .GetProperty("result");
        var hash = xml.GetProperty("contentHash").GetString();

        var (preview, _) = await session.CallAsync("plc_write", Args(("dryRun", true), ("operations", new object[]
        {
            new { operationId = "group", operation = "create_watch_table_group", plcName = "PLC_1", folderPath = "/Cell" },
            new { operationId = "table", operation = "create_watch_table", plcName = "PLC_1", folderPath = "/Cell", tableName = "Watch_Cell" },
            new { operationId = "drop", operation = "delete_watch_table", plcName = "PLC_1", folderPath = "/Cell", tableName = "Watch_Cell" },
            new { operationId = "edit", operation = "update_watch_table_content", plcName = "PLC_1", tableName = "Watch_1", content = Edited, expectedContentHash = hash },
            new { operationId = "upgrade", operation = "update_program", plcName = "PLC_1" },
        })));
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var effects = preview.GetProperty("effects").EnumerateArray().ToArray();
        Assert.Equal("group", effects[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal("table", effects[2].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal("WatchTable", effects[3].GetProperty("effect").GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal("PlcSoftware", effects[4].GetProperty("effect").GetProperty("target").GetProperty("kind").GetString());
        Assert.Contains("plc_deletes_watch_table", preview.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        Assert.DoesNotContain("create_watch_table", session.Methods);

        var (applied, raw) = await session.CallAsync("plc_write", Args(("operations", new object[]
        {
            new { operationId = "edit", operation = "update_watch_table_content", plcName = "PLC_1", tableName = "Watch_1", content = Edited, expectedContentHash = hash },
            new { operationId = "upgrade", operation = "update_program", plcName = "PLC_1" },
        })));
        Assert.False(raw.IsError == true);
        Assert.True(applied.GetProperty("success").GetBoolean());
        var upgrade = applied.GetProperty("batch").GetProperty("operations")[1].GetProperty("result");
        Assert.Equal("uninterpreted", upgrade.GetProperty("outcome").GetString());
        var verification = applied.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal("contentHash", verification.GetProperty("check").GetString());
        Assert.Equal(ContentHashRules.Compute("xml", Edited), verification.GetProperty("contentHash").GetString());

        var (stale, _) = await session.CallAsync("plc_write", Args(("operations", new object[]
        {
            new { operationId = "edit", operation = "update_watch_table_content", plcName = "PLC_1", tableName = "Watch_1", content = "<Document>again</Document>", expectedContentHash = hash },
        })));
        Assert.Equal("state_changed", stale.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task PlcWrite_ADocumentNamingAnotherTableIsRefusedBeforeTheImport()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var hash = (await session.ReadAsync(new { operationId = "xml", operation = "get_watch_table_content", plcName = "PLC_1", tableName = "Watch_1" }))[0]
            .GetProperty("result").GetProperty("contentHash").GetString();
        var other = Edited.Replace("<Name>Watch_1</Name>", "<Name>Watch_Line</Name>", StringComparison.Ordinal);
        var (document, _) = await session.CallAsync("plc_write", Args(("operations", new object[]
        {
            new { operationId = "edit", operation = "update_watch_table_content", plcName = "PLC_1", tableName = "Watch_1", content = other, expectedContentHash = hash },
        })));

        Assert.False(document.GetProperty("success").GetBoolean());
        var failure = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("validation_error", failure.GetProperty("category").GetString());
        Assert.Contains("names table 'Watch_Line'", failure.GetProperty("message").GetString());
        var after = (await session.ReadAsync(new { operationId = "xml", operation = "get_watch_table_content", plcName = "PLC_1", tableName = "Watch_1" }))[0];
        Assert.Equal(hash, after.GetProperty("result").GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task PlcWrite_DeletingAMissingWatchTableIsTargetNotFound()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.CallAsync("plc_write", Args(("operations", new object[]
        {
            new { operationId = "drop", operation = "delete_watch_table", plcName = "PLC_1", tableName = "Missing" },
        })));

        Assert.Equal("target_not_found", document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("delete_watch_table", session.Methods);
    }
}
