using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Safety;

/// <summary>
/// The fail-safe operations of plc_read/plc_write end to end against the plc-write-roundtrip FakeWorker (PLC_1 is a
/// password-protected F-CPU, not logged on, with the F-block Safety_FB). Like every FakeWorker protocol test these
/// launch TiaMcpServer.FakeWorker.exe. The password reaches the worker only through the environment variable whose
/// NAME the call passes.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcSafetyMcpTests
{
    private const string Scenario = "plc-write-roundtrip";
    private const string PasswordVariable = "TIA_MCP_TEST_SAFETY_PASSWORD";
    private const string WrongPasswordVariable = "TIA_MCP_TEST_SAFETY_WRONG_PASSWORD";

    /// <summary>The password the FakeWorker expects (Password).</summary>
    private const string Password = "FakeSafetyPassword!1";

    static PlcSafetyMcpTests()
    {
        // Set before any worker starts, so the FakeWorker inherits them like the real worker inherits the host's environment.
        Environment.SetEnvironmentVariable(PasswordVariable, Password);
        Environment.SetEnvironmentVariable(WrongPasswordVariable, "not-the-password");
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly FakeWorkerRequestLog _requests;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        private Session()
        {
            Directory.CreateDirectory(_audit.Path);
            _requests = new FakeWorkerRequestLog(_audit.Path);
            _uiOpen = new FakeWorkerUiOpenProject(Scenario);
        }

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public List<string> Prompts { get; } = new();

        public string[] Methods => _requests.Methods();

        public string AuditText => string.Join("\n", NetworkGuardedWriteMcpTests.AuditLines(_audit.Path));

        public static async Task<Session> StartAsync(McpAccessMode mode, string? elicitation = null)
        {
            var session = new Session();
            McpClientOptions? options = elicitation is null ? null : new()
            {
                Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (request, _) =>
                    {
                        session.Prompts.Add(request?.Message ?? string.Empty);
                        return ValueTask.FromResult(new ElicitResult
                        {
                            Action = elicitation,
                            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
                        });
                    },
                },
            };
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, Scenario, clientOptions: options);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            return (raw.StructuredContent!.Value, raw);
        }

        public Task<(JsonElement Document, CallToolResult Raw)> WriteAsync(params object[] operations)
            => CallAsync("plc_write", new() { ["operations"] = operations });

        public async Task<string> HashAsync(string blockPath)
        {
            var (document, _) = await CallAsync("plc_read", new() { ["operations"] = new object[] { new { operationId = "h", operation = "get_block_content", blockPath, format = "xml" } } });
            return document.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("contentHash").GetString()!;
        }

        public async Task<string> ContentAsync(string blockPath)
        {
            var (document, _) = await CallAsync("plc_read", new() { ["operations"] = new object[] { new { operationId = "c", operation = "get_block_content", blockPath, format = "xml" } } });
            return document.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("content").GetString()!;
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static IEnumerable<JsonElement> Guards(JsonElement document) => document.GetProperty("guards").EnumerateArray();

    private static string[] GuardIds(JsonElement document) => Guards(document).Select(g => g.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task ReadOnly_ReportsTheSafetyAdministrationAndTheFBlocks()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var (document, raw) = await session.CallAsync("plc_read", new()
        {
            ["operations"] = new object[]
            {
                new { operationId = "admin", operation = "get_safety_administration" },
                new { operationId = "blocks", operation = "list_safety_blocks", plcName = "Station_1" },
            },
        });

        Assert.False(raw.IsError == true, document.GetRawText());
        var operations = document.GetProperty("batch").GetProperty("operations");
        var plcs = operations[0].GetProperty("result").GetProperty("plcs").EnumerateArray().ToArray();
        var fcpu = plcs.Single(p => p.GetProperty("deviceName").GetString() == "Station_1");
        Assert.True(fcpu.GetProperty("available").GetBoolean());
        Assert.True(fcpu.GetProperty("passwordSet").GetBoolean());
        Assert.False(fcpu.GetProperty("loggedOn").GetBoolean());
        Assert.Equal("RTG1", fcpu.GetProperty("runtimeGroups")[0].GetProperty("name").GetString());
        Assert.Equal("0x12345678", fcpu.GetProperty("programSignatures")[0].GetProperty("hex").GetString());
        Assert.Contains(plcs, p => !p.GetProperty("available").GetBoolean());
        var block = operations[1].GetProperty("result").GetProperty("plcs")[0].GetProperty("blocks")[0];
        Assert.Equal("F_LAD", block.GetProperty("programmingLanguage").GetString());
        Assert.Equal("0xDEADBEEF", block.GetProperty("signatures")[0].GetProperty("hex").GetString());
    }

    [Fact]
    public async Task ReadOnly_DoesNotOfferTheSafetyWrites()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var tools = await session.Harness.Client.ListToolsAsync();
        Assert.DoesNotContain(tools, tool => tool.Name == "plc_write");
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcSafetyMethods.Login));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcSafetyMethods.GetAdministration));
    }

    [Fact]
    public async Task Full_LoginThenFBlockUpdate_RunsUnderPolicyWithTheAcceptanceGuard()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var hash = await session.HashAsync("Station_1/Safety_FB");
        var content = (await session.ContentAsync("Station_1/Safety_FB")).Replace("<Section Name=\"Static\" />", "<Section Name=\"Static\"></Section>");
        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable },
            new { operationId = "edit", operation = "update_block_logic", blockPath = "Station_1/Safety_FB", content, expectedContentHash = hash });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        var acceptance = Guards(document).Where(g => g.GetProperty("id").GetString() == "plc_safety_acceptance_required").ToArray();
        Assert.Equal(new[] { "login", "edit" }, acceptance.Select(g => g.GetProperty("operationId").GetString()));
        Assert.All(acceptance, g => Assert.True(g.GetProperty("acknowledged").GetBoolean()));
        Assert.All(acceptance, g => Assert.Contains("never replaces the safety acceptance", g.GetProperty("message").GetString()));
        Assert.DoesNotContain("plc_safety_login_required", GuardIds(document));
        var login = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.True(login.GetProperty("loggedOnAfter").GetBoolean());
        Assert.Equal(PasswordVariable, login.GetProperty("passwordVariable").GetString());
        var methods = session.Methods;
        Assert.True(Array.IndexOf(methods, "safety_login") < Array.LastIndexOf(methods, "update_block_logic"));
        Assert.DoesNotContain(Password, session.AuditText);
        Assert.DoesNotContain(Password, document.GetRawText());
    }

    /// <summary>K49 (live V21 2026-11-02): the safety access writes report the worker's read-back, not "trusted".</summary>
    [Fact]
    public async Task Full_LoginAndLogoff_AreVerifiedByTheWorkerReadBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (login, _) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable });
        var check = Assert.Single(login.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(("readBack", true, "loggedOnAfter=true", "loggedOnAfter=true"), (check.GetProperty("check").GetString(),
            check.GetProperty("success").GetBoolean(), check.GetProperty("expected").GetString(), check.GetProperty("actual").GetString()));

        var (logoff, _) = await session.WriteAsync(new { operationId = "logoff", operation = "safety_logoff", plcName = "Station_1" });
        check = Assert.Single(logoff.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(("readBack", true, "loggedOnAfter=false"), (check.GetProperty("check").GetString(),
            check.GetProperty("success").GetBoolean(), check.GetProperty("actual").GetString()));
    }

    [Fact]
    public async Task Full_FBlockUpdateWithoutLogin_IsBlockedBeforeAnyImport()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var hash = await session.HashAsync("Station_1/Safety_FB");
        var content = await session.ContentAsync("Station_1/Safety_FB");
        var (document, _) = await session.WriteAsync(
            new { operationId = "edit", operation = "update_block_logic", blockPath = "Station_1/Safety_FB", content, expectedContentHash = hash });

        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Contains("plc_safety_login_required", GuardIds(document));
        Assert.Contains("safety_login", Guards(document).Single(g => g.GetProperty("id").GetString() == "plc_safety_login_required").GetProperty("message").GetString());
        Assert.DoesNotContain("update_block_logic", session.Methods);
    }

    [Fact]
    public async Task Full_InterfaceEditOfAnFBlock_IsBlockedWithoutLogin_AndRunsAfterLogin()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (refused, _) = await session.WriteAsync(
            new { operationId = "add", operation = "add_interface_member", blockPath = "Station_1/Safety_FB", section = "Static", memberPath = "Extra", dataType = "Bool" });
        Assert.Equal("blocked", refused.GetProperty("phase").GetString());
        Assert.Contains("plc_safety_login_required", GuardIds(refused));
        Assert.DoesNotContain("update_block_logic", session.Methods);

        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", passwordVariable = PasswordVariable, plcName = "Station_1" },
            new { operationId = "add", operation = "add_interface_member", blockPath = "Station_1/Safety_FB", section = "Static", memberPath = "Extra", dataType = "Bool" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Contains(Guards(document), g => g.GetProperty("id").GetString() == "plc_safety_acceptance_required" && g.GetProperty("operationId").GetString() == "add");
    }

    [Theory]
    [InlineData("delete_block")]
    [InlineData("rename_block")]
    [InlineData("renumber_block")]
    [InlineData("copy_block")]
    [InlineData("move_block")]
    public async Task Full_FBlockStructureEdits_FollowTheLoginRule_WithTheAcceptanceGuard(string operation)
    {
        object Item() => operation switch
        {
            "rename_block" => new { operationId = "op", operation, blockPath = "Station_1/Safety_FB", newName = "Safety_FB_Renamed" },
            "renumber_block" => new { operationId = "op", operation, blockPath = "Station_1/Safety_FB", number = 77 },
            "copy_block" => new { operationId = "op", operation, blockPath = "Station_1/Safety_FB", newName = "Safety_FB_Copy" },
            "move_block" => new { operationId = "op", operation, blockPath = "Station_1/Safety_FB", targetGroupPath = "Station_1/Blocks/Motors" },
            _ => (object)new { operationId = "op", operation, blockPath = "Station_1/Safety_FB" },
        };
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        // A safety password is set and the session is not logged on: TIA Portal's own rule, blocked before any change.
        var (blocked, _) = await session.WriteAsync(Item());
        Assert.Equal("blocked", blocked.GetProperty("phase").GetString());
        Assert.Contains("plc_safety_login_required", GuardIds(blocked));
        Assert.Contains("plc_safety_acceptance_required", GuardIds(blocked));

        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable }, Item());
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Contains(Guards(document), g => g.GetProperty("id").GetString() == "plc_safety_acceptance_required" && g.GetProperty("operationId").GetString() == "op");
        Assert.DoesNotContain("plc_safety_login_required", GuardIds(document));
    }

    [Fact]
    public async Task Full_WithoutASafetyPassword_FBlockChangesNeedNoLogin()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (revoked, _) = await session.WriteAsync(
            new { operationId = "revoke", operation = "revoke_safety_password", plcName = "Station_1", passwordVariable = PasswordVariable });
        Assert.Equal("applied", revoked.GetProperty("phase").GetString());

        // TIA Portal allows F-program changes without login when no password is set; so does the server.
        var hash = await session.HashAsync("Station_1/Safety_FB");
        var content = (await session.ContentAsync("Station_1/Safety_FB")).Replace("<Section Name=\"Static\" />", "<Section Name=\"Static\"></Section>");
        var (document, raw) = await session.WriteAsync(
            new { operationId = "edit", operation = "update_block_logic", blockPath = "Station_1/Safety_FB", content, expectedContentHash = hash },
            new { operationId = "rename", operation = "rename_block", blockPath = "Station_1/Safety_FB", newName = "Safety_FB_2" },
            new { operationId = "rtg", operation = "create_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG2" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.DoesNotContain("plc_safety_login_required", GuardIds(document));
        Assert.Equal(new[] { "edit", "rename", "rtg" },
            Guards(document).Where(g => g.GetProperty("id").GetString() == "plc_safety_acceptance_required").Select(g => g.GetProperty("operationId").GetString()));
    }

    [Fact]
    public async Task ReadWrite_SafetyLogin_AsksOnceWithTheAcceptanceNotice()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        var prompt = Assert.Single(session.Prompts);
        Assert.Contains("never replaces the safety acceptance", prompt);
        Assert.Contains("safety_login on PLC", prompt);
        Assert.DoesNotContain(Password, prompt);
        var record = JsonDocument.Parse(session.AuditText.Split('\n').Last(line => line.Contains("\"plc_write\"", StringComparison.Ordinal))).RootElement;
        Assert.Equal("user", record.GetProperty("confirmation").GetProperty("by").GetString());
    }

    [Theory]
    [InlineData("decline")]
    [InlineData(null)]
    public async Task ReadWrite_SafetyLogin_WithoutAcceptance_ChangesNothing(string? elicitation)
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation);
        var (document, _) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable });

        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("safety_login", session.Methods);
    }

    [Fact]
    public async Task OrdinaryPlcWrites_StillAskNothing()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var (document, raw) = await session.WriteAsync(
            new { operationId = "t", operation = "create_tag_table", plcName = "Station_1", tableName = "Safety_Test_Table" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Empty(session.Prompts);
        Assert.DoesNotContain("plc_safety_acceptance_required", GuardIds(document));
    }

    [Theory]
    [InlineData("Pa55 word!")]
    [InlineData("TIA_MCP_TEST_SAFETY_UNSET_VARIABLE")]
    public async Task PasswordVariable_ThatIsNotASetVariableName_IsRefusedWithoutEchoingIt(string value)
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = value });

        Assert.True(raw.IsError == true);
        Assert.Equal(WorkerFailureCategories.ValidationError, document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("passwordVariable", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(value, document.GetRawText());
        Assert.DoesNotContain(value, session.AuditText);
        Assert.DoesNotContain("safety_login", session.Methods);
    }

    [Fact]
    public async Task WrongPassword_FailsTheItem_AndTheSessionStaysLoggedOff()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = WrongPasswordVariable });
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal("engineering_rejected", document.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());

        var (read, _) = await session.CallAsync("plc_read", new() { ["operations"] = new object[] { new { operationId = "a", operation = "get_safety_administration", plcName = "Station_1" } } });
        Assert.False(read.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("plcs")[0].GetProperty("loggedOn").GetBoolean());
    }

    [Fact]
    public async Task RuntimeGroup_IsCreatedOnlyAfterLogin_AndExistingNamesAreRefused()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (blocked, _) = await session.WriteAsync(new { operationId = "g", operation = "create_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG2" });
        Assert.Equal("blocked", blocked.GetProperty("phase").GetString());
        Assert.Contains("plc_safety_login_required", GuardIds(blocked));

        var (exists, _) = await session.WriteAsync(new { operationId = "g", operation = "create_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG1" });
        Assert.Equal(WorkerFailureCategories.ValidationError, exists.GetProperty("error").GetProperty("category").GetString());

        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable },
            new { operationId = "g", operation = "create_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG2" },
            new { operationId = "logoff", operation = "safety_logoff", plcName = "Station_1" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        var group = document.GetProperty("batch").GetProperty("operations")[1].GetProperty("result");
        Assert.Equal("Create(String)", group.GetProperty("overload").GetString());
        Assert.Equal("RTG2", group.GetProperty("runtimeGroup").GetProperty("name").GetString());
        Assert.False(document.GetProperty("batch").GetProperty("operations")[2].GetProperty("result").GetProperty("loggedOnAfter").GetBoolean());
    }

    /// <summary>set_safety_runtime_group (repair of E7): login rule, existing group only, the assignment read back by name.</summary>
    [Fact]
    public async Task SetRuntimeGroup_AssignsTheMainSafetyBlockOfAnExistingGroup()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (blocked, _) = await session.WriteAsync(new { operationId = "s", operation = "set_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG1", mainSafetyBlockPath = "Station_1/Blocks/Safety_FB" });
        Assert.Equal("blocked", blocked.GetProperty("phase").GetString());
        Assert.Contains("plc_safety_login_required", GuardIds(blocked));

        var (missing, _) = await session.WriteAsync(new { operationId = "s", operation = "set_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG9", mainSafetyBlockPath = "Station_1/Blocks/Safety_FB" });
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.GetProperty("error").GetProperty("category").GetString());

        var (document, raw) = await session.WriteAsync(
            new { operationId = "login", operation = "safety_login", plcName = "Station_1", passwordVariable = PasswordVariable },
            new { operationId = "s", operation = "set_safety_runtime_group", plcName = "Station_1", runtimeGroupName = "RTG1", mainSafetyBlockPath = "Station_1/Blocks/Safety_FB" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Contains("plc_safety_acceptance_required", GuardIds(document));
        var effect = document.GetProperty("effects")[1].GetProperty("effect").GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == "mainSafetyBlock");
        Assert.Equal("FB_Safety", effect.GetProperty("current").GetString());
        var result = document.GetProperty("batch").GetProperty("operations")[1].GetProperty("result");
        var assignment = result.GetProperty("assignments")[0];
        Assert.Equal("MainSafetyBlock", assignment.GetProperty("member").GetString());
        Assert.Equal("Safety_FB", assignment.GetProperty("after").GetString());
        Assert.Equal("FB_Safety", assignment.GetProperty("before").GetString());

        var (read, _) = await session.CallAsync("plc_read", new() { ["operations"] = new object[] { new { operationId = "a", operation = "get_safety_administration", plcName = "Station_1" } } });
        var attributes = read.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("plcs")[0].GetProperty("runtimeGroups")[0].GetProperty("attributes");
        Assert.Equal("Safety_FB", attributes.EnumerateArray().Single(a => a.GetProperty("name").GetString() == "MainSafetyBlockName").GetProperty("value").GetString());
    }

    /// <summary>D13 (live, V21, 2026-11-02): a block lists SafetySignatureProvider but GetService returns null; the listing still succeeds.</summary>
    [Fact]
    public async Task ListSafetyBlocks_ReportsAnUnretrievableSignatureProvider_WithoutFailing()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var (document, raw) = await session.CallAsync("plc_read", new() { ["operations"] = new object[] { new { operationId = "b", operation = "list_safety_blocks", plcName = "Station_1" } } });
        Assert.False(raw.IsError == true, document.GetRawText());
        var item = document.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var blocks = item.GetProperty("result").GetProperty("plcs")[0].GetProperty("blocks").EnumerateArray().ToArray();
        Assert.Equal("read", blocks.Single(b => b.GetProperty("name").GetString() == "Safety_FB").GetProperty("signatureState").GetString());
        var unavailable = blocks.Single(b => b.GetProperty("name").GetString() == "FB_Safety");
        Assert.Equal("unreadable", unavailable.GetProperty("signatureState").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("signatures").ValueKind);
        Assert.Contains("GetService returned null", unavailable.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task StandardCpu_RefusesSafetyWrites()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(new { operationId = "x", operation = "safety_logoff", plcName = "PLC_2" });
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("safety_logoff", session.Methods);
    }

    [Fact]
    public async Task CredentialSource_ReachesTheWorkerAsATargetName_AndIsUnsupportedOffWindows()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(new
        {
            operationId = "login", operation = "safety_login", plcName = "Station_1",
            passwordVariable = new { credential = "TiaMcp/MCP_Livetest_01/PLC_1/Safety" },
        });

        Assert.False(raw.IsError == true, document.GetRawText());
        var item = document.GetProperty("batch").GetProperty("operations")[0];
        // Off Windows the shared resolver refuses; on Windows the credential does not exist for the test user.
        var category = item.GetProperty("failure").GetProperty("category").GetString();
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.UnsupportedCapability, category);
        Assert.Contains("credential:TiaMcp/MCP_Livetest_01/PLC_1/Safety", session.AuditText.Replace("\\u0022", "\""));
    }

    [Fact]
    public async Task InputSchemas_OfferEnvironmentAndCredentialSources()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var tools = await session.Harness.Client.ListToolsAsync();
        foreach (var name in new[] { "plc_write", "plc_download", "plc_tia_online", "plc_station_upload" })
        {
            var schema = tools.Single(tool => tool.Name == name).JsonSchema.GetRawText();
            Assert.Contains("\"credential\"", schema);
            Assert.Contains("^TiaMcp/", schema);
        }
    }
}
