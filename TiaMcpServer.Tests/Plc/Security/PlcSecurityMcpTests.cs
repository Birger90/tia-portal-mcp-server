using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Security;

/// <summary>
/// The CPU protection operations end to end through plc_read/plc_write against the FakeWorker scenarios plc-security and
/// plc-security-no-unprotect, which run the worker's Siemens-free <see cref="PlcSecurityRun"/> against stand-in services.
/// Like every FakeWorker protocol test these launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcSecurityMcpTests
{
    private const string FullVariable = "TIA_MCP_TEST_CPU_FULL_PASSWORD";
    private const string ReadVariable = "TIA_MCP_TEST_CPU_READ_PASSWORD";
    private const string SecretVariable = "TIA_MCP_TEST_CPU_SECRET_PASSWORD";
    private const string NewSecretVariable = "TIA_MCP_TEST_CPU_NEW_SECRET_PASSWORD";
    private const string FullPassword = "FakeCpuFull!1";

    static PlcSecurityMcpTests()
    {
        // Set before any worker starts, so the FakeWorker inherits them like the real worker inherits the host's environment.
        Environment.SetEnvironmentVariable(FullVariable, FullPassword);
        Environment.SetEnvironmentVariable(ReadVariable, "FakeCpuRead!1");
        Environment.SetEnvironmentVariable(SecretVariable, "FakeSecret!1");
        Environment.SetEnvironmentVariable(NewSecretVariable, "FakeSecretNew!1");
    }

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

        public string[] Methods => _requests.Methods();

        public static async Task<Session> StartAsync(McpAccessMode mode, string scenario = "plc-security")
        {
            var session = new Session(scenario);
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, scenario);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            return (raw.StructuredContent!.Value, raw);
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> WriteAsync(bool dryRun, params object[] operations)
            => await CallAsync("plc_write", new() { ["operations"] = operations, ["dryRun"] = dryRun });

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static JsonElement Item(JsonElement document, int index) => document.GetProperty("batch").GetProperty("operations")[index];

    [Fact]
    public async Task ReadOnly_ReportsBothSettingsWithoutWrites()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        Assert.DoesNotContain("plc_write", (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name));

        var (document, raw) = await session.CallAsync("plc_read", new()
        {
            ["operations"] = new object[] { new { operationId = "s", operation = "get_plc_security" } },
        });
        Assert.False(raw.IsError == true, document.GetRawText());
        var item = Item(document, 0);
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var plc = item.GetProperty("result").GetProperty("plcs")[0];
        Assert.Equal("PLC_1", plc.GetProperty("plcName").GetString());
        var level = plc.GetProperty("accessLevel");
        Assert.True(level.GetProperty("serviceOffered").GetBoolean());
        Assert.False(level.GetProperty("readable").GetBoolean());
        Assert.Equal(PlcSecurityPolicy.NoPasswordReadNote, level.GetProperty("note").GetString());
        Assert.Equal("Disabled", plc.GetProperty("accessControl").GetProperty("value").GetString());
        var confidential = plc.GetProperty("confidentialDataProtection");
        Assert.True(confidential.GetProperty("unprotectBindable").GetBoolean());
        Assert.True(confidential.GetProperty("protectBindable").GetBoolean());
        Assert.Equal("Protected", confidential.GetProperty("observedMembers")[0].GetProperty("value").GetString());
        Assert.DoesNotContain(session.Methods, PlcSecurityMethods.IsWrite);
    }

    /// <summary>D14 (live, V21, 2026-11-02): a listed access-control provider that GetService does not return leaves the rest of the read intact.</summary>
    [Fact]
    public async Task ListedButUnretrievableService_IsReportedUnavailable_AndTheRestIsStillRead()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly, "plc-security-control-unavailable");
        var (document, raw) = await session.CallAsync("plc_read", new()
        {
            ["operations"] = new object[] { new { operationId = "s", operation = "get_plc_security", plcName = "PLC_1" } },
        });
        Assert.False(raw.IsError == true, document.GetRawText());
        var item = Item(document, 0);
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var plc = item.GetProperty("result").GetProperty("plcs")[0];
        var control = plc.GetProperty("accessControl");
        Assert.True(control.GetProperty("serviceOffered").GetBoolean());
        Assert.False(control.GetProperty("readable").GetBoolean());
        Assert.Contains("GetService returned null", control.GetProperty("note").GetString());
        Assert.Equal(PlcSecurityPolicy.NoPasswordReadNote, plc.GetProperty("accessLevel").GetProperty("note").GetString());
        Assert.Equal("Protected", plc.GetProperty("confidentialDataProtection").GetProperty("observedMembers")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task DryRun_PreviewsBothItemsWithTheInfoGuard_AndCallsNoWrite()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(true,
            new { operationId = "level", operation = "set_plc_access_level", accessLevel = "FullAccess" },
            new { operationId = "secret", operation = "disable_plc_confidential_data_protection", plcName = "PLC_1" });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.Equal(new[] { "plc_lowers_cpu_protection", "plc_lowers_cpu_protection" },
            document.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        Assert.Equal("FullAccess", document.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0].GetProperty("requested").GetString());
        Assert.DoesNotContain(session.Methods, PlcSecurityMethods.IsWrite);
    }

    [Fact]
    public async Task Apply_SetsTheLevelAndUnprotects_ReportingWhatCouldNotBeReadBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "level", operation = "set_plc_access_level", accessLevel = "FullAccess" },
            new { operationId = "secret", operation = "disable_plc_confidential_data_protection" });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var level = Item(document, 0).GetProperty("result");
        Assert.Equal("unreadable", level.GetProperty("readBack").GetString());
        Assert.Equal("FullAccess", level.GetProperty("requested").GetString());
        Assert.Contains(Item(document, 0).GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("could not be read back"));
        var secret = Item(document, 1).GetProperty("result");
        Assert.Equal("void Unprotect()", secret.GetProperty("member").GetString());
        Assert.Equal("notAvailable", secret.GetProperty("readBack").GetString());
        Assert.Equal("Protected", secret.GetProperty("observedBefore")[0].GetProperty("value").GetString());
        Assert.Equal("NotProtected", secret.GetProperty("observedAfter")[0].GetProperty("value").GetString());
        // An unreadable level and the confidential-data operation have no read-back: trusted.
        Assert.All(document.GetProperty("verification").GetProperty("operations").EnumerateArray(),
            v => Assert.Equal("trusted", v.GetProperty("check").GetString()));
        Assert.Contains(PlcSecurityMethods.SetAccessLevel, session.Methods);
        Assert.Contains(PlcSecurityMethods.DisableConfidentialDataProtection, session.Methods);
    }

    [Fact]
    public async Task MissingUnprotect_FailsApiMemberMissing()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "plc-security-no-unprotect");
        var (document, _) = await session.WriteAsync(false, new { operationId = "secret", operation = "disable_plc_confidential_data_protection" });

        Assert.False(document.GetProperty("success").GetBoolean());
        var item = Item(document, 0);
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("api_member_missing", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("Nothing was changed.", item.GetProperty("failure").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("None")]
    [InlineData("fullaccess")]
    public async Task UnknownLevels_AreRefusedBeforeAnyWorkerCall(string level)
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(false, new { operationId = "level", operation = "set_plc_access_level", accessLevel = level });

        Assert.True(raw.IsError);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain(session.Methods, PlcSecurityMethods.IsWorkerMethod);
    }

    [Fact]
    public async Task ProtectedLevel_SetsTheLevelPasswordsFromSources_AndReadsTheLevelBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(false, new
        {
            operationId = "level", operation = "set_plc_access_level", accessLevel = "HMIAccess",
            accessLevelPasswords = new Dictionary<string, object> { ["FullAccess"] = FullVariable, ["ReadAccess"] = new { env = ReadVariable } },
        });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Contains(document.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == "plc_raises_cpu_protection");
        var result = Item(document, 0).GetProperty("result");
        Assert.Equal(new[]
        {
            // K48: the level first, then the passwords of the more open levels.
            "PlcProtectionAccessLevel { set; } [HMIAccess]",
            "void SetPassword(PlcProtectionAccessLevel, SecureString) [FullAccess]",
            "void SetPassword(PlcProtectionAccessLevel, SecureString) [ReadAccess]",
        }, result.GetProperty("calls").EnumerateArray().Select(c => c.GetString()));
        // With a level password configured, the documented getter reads the new level back.
        Assert.Equal(("matched", "HMIAccess"), (result.GetProperty("readBack").GetString(), result.GetProperty("after").GetString()));
        // K24: the matched read-back is the verification (check readBack), not "trusted, nothing was read back".
        var check = Assert.Single(document.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(("readBack", true, "HMIAccess", "HMIAccess"), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean(),
            check.GetProperty("expected").GetString(), check.GetProperty("actual").GetString()));
        Assert.Equal(new[] { (FullVariable, "environment"), (ReadVariable, "environment") },
            result.GetProperty("passwordSources").EnumerateArray().Select(p => (p.GetProperty("source").GetString(), p.GetProperty("kind").GetString())));
        Assert.DoesNotContain(FullPassword, document.GetRawText());

        var (reset, _) = await session.WriteAsync(false, new
        {
            operationId = "open", operation = "set_plc_access_level", accessLevel = "FullAccess", resetAccessLevelPasswords = new[] { "ReadAccess" },
        });
        Assert.True(reset.GetProperty("success").GetBoolean(), reset.GetRawText());
        Assert.Contains("void ResetPassword(PlcProtectionAccessLevel) [ReadAccess]",
            Item(reset, 0).GetProperty("result").GetProperty("calls").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task ProtectedLevel_WithoutAFullAccessPassword_RunsAndWarns()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(false, new { operationId = "level", operation = "set_plc_access_level", accessLevel = "NoAccess" });
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Contains(Item(document, 0).GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("needs a FullAccess password"));
    }

    [Fact]
    public async Task ConfidentialData_ProtectChangeAndUnprotect_UsePasswordSources()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "protect", operation = "protect_plc_confidential_data", passwordVariable = SecretVariable },
            new { operationId = "change", operation = "change_plc_confidential_data_password", passwordVariable = SecretVariable, newPasswordVariable = NewSecretVariable },
            new { operationId = "all", operation = "protect_plc_confidential_data", allConfiguration = true },
            new { operationId = "off", operation = "disable_plc_confidential_data_protection", passwordVariable = NewSecretVariable });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(new[] { "void Protect(SecureString)", "void ChangePassword(SecureString, SecureString)", "void ProtectAllPlcConfiguration()", "void Unprotect(SecureString)" },
            Enumerable.Range(0, 4).Select(i => Item(document, i).GetProperty("result").GetProperty("member").GetString()));
        Assert.Equal("NotProtected", Item(document, 3).GetProperty("result").GetProperty("observedAfter")[0].GetProperty("value").GetString());
        Assert.Equal(new[] { "PlcMasterSecretPassword", "PlcMasterSecretPassword:new" },
            Item(document, 1).GetProperty("result").GetProperty("passwordSources").EnumerateArray().Select(p => p.GetProperty("purpose").GetString()));
        Assert.Equal(3, document.GetProperty("guards").EnumerateArray().Count(g => g.GetProperty("id").GetString() == "plc_raises_cpu_protection"));
        Assert.Contains(document.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == "plc_lowers_cpu_protection" && g.GetProperty("operationId").GetString() == "off");
    }

    [Fact]
    public async Task ConfidentialData_APasswordValueOrACredentialOffWindows_IsRefused()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (pasted, pastedRaw) = await session.WriteAsync(false, new { operationId = "protect", operation = "protect_plc_confidential_data", passwordVariable = "my secret pw" });
        Assert.True(pastedRaw.IsError == true);
        Assert.DoesNotContain("my secret pw", pasted.GetRawText());

        var (credential, _) = await session.WriteAsync(false,
            new { operationId = "protect", operation = "protect_plc_confidential_data", passwordVariable = new { credential = "TiaMcp/Test/PLC_1/Secret" } });
        var failure = Item(credential, 0).GetProperty("failure");
        // Off Windows the shared resolver refuses before any Siemens call; on Windows the credential does not exist for the test user.
        Assert.Contains(failure.GetProperty("category").GetString(), new[] { "unsupported_capability", "validation_error" });
        Assert.Contains("Nothing was changed.", failure.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CompileCheck_HardwareScopeWithBlockPathOrUnknownScope_IsAValidationError()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        foreach (var arguments in new[]
                 {
                     new Dictionary<string, object?> { ["scope"] = "hardware", ["blockPath"] = "PLC_1/Main" },
                     new Dictionary<string, object?> { ["scope"] = "everything" },
                 })
        {
            var (document, raw) = await session.CallAsync("compile_check", arguments);
            Assert.True(raw.IsError);
            Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        }

        Assert.DoesNotContain("compile_check", session.Methods);
    }
}
