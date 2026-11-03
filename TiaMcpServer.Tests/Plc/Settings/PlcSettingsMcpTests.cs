using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>
/// The settings family end to end through plc_read/plc_write against the FakeWorker scenario plc-settings, which runs the
/// worker's Siemens-free <see cref="PlcSettingsRun"/> against stand-in services. Like every FakeWorker protocol test these launch
/// TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed partial class PlcSettingsMcpTests
{
    private const string DisplayVariable = "TIA_MCP_TEST_DISPLAY_PASSWORD";

    static PlcSettingsMcpTests()
    {
        // Set before any worker starts, so the FakeWorker inherits them like the real worker inherits the host's environment.
        Environment.SetEnvironmentVariable(DisplayVariable, "FakeDisplay!1");
        InitializeMore();
    }

    static partial void InitializeMore();

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

        public static async Task<Session> StartAsync(McpAccessMode mode, string scenario = "plc-settings")
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

        public async Task<(JsonElement Document, CallToolResult Raw)> ReadAsync(params object[] operations)
            => await CallAsync("plc_read", new() { ["operations"] = operations });

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

    private static JsonElement Section(JsonElement sections, string name)
        => sections.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static string? Property(JsonElement owner, string name)
        => owner.GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name).GetProperty("value").GetProperty("value").GetString();

    private static IEnumerable<string?> GuardIds(JsonElement document) => document.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString());

    private static JsonElement Verification(JsonElement document, string operationId)
        => document.GetProperty("verification").GetProperty("operations").EnumerateArray().Single(v => v.GetProperty("operationId").GetString() == operationId);

    [Fact]
    public async Task ReadOnly_GetCpuSettings_ReportsCpuDisplayAndProjectSections_WithoutWrites()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var (document, raw) = await session.ReadAsync(new { operationId = "s", operation = "get_cpu_settings" });
        Assert.False(raw.IsError == true, document.GetRawText());
        var result = Item(document, 0).GetProperty("result");
        var plc = result.GetProperty("plcs")[0];
        Assert.Equal("PLC_1", plc.GetProperty("plcName").GetString());
        var sections = plc.GetProperty("sections");
        Assert.Equal("5", Property(Section(sections, "accountLocking"), "NumberOfFailedLoginAttempts"));
        Assert.Equal("None", Property(Section(sections, "syslog"), "TransportProtocol"));
        var display = Section(sections, "displayProtection");
        Assert.True(display.GetProperty("offered").GetBoolean());
        Assert.Equal("device item 'Display'", display.GetProperty("owner").GetString());
        Assert.Contains("SetPassword(SecureString)", display.GetProperty("members").EnumerateArray().Select(m => m.GetString()));
        var apps = Section(sections, "webApplications").GetProperty("collections")[0].GetProperty("items");
        Assert.Equal(new[] { "System web pages", "MyApp" }, apps.EnumerateArray().Select(a => a.GetProperty("key").GetString()));
        var rows = Section(sections, "multilingualSupport").GetProperty("collections")[0].GetProperty("items");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("en-US", Property(rows[0], "ProjectLanguage"));
        Assert.Equal("True", Property(Section(result.GetProperty("project"), "plcPasswordPolicy"), "PasswordPolicyEnabled"));
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }

    [Fact]
    public async Task ReadOnly_DoesNotOfferTheWrites()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        Assert.DoesNotContain("plc_write", (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name));
        Assert.All(PlcSettingsCatalog.Write, method => Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method), method));
        Assert.All(PlcSettingsCatalog.Read.Where(method => method != PlcSafetyValidationMethods.CheckActivationTest),
            method => Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method), method));
        // U1: TestValidity.CheckValidity() may store a validity state (undocumented), so check_activation_test is a write capability.
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(PlcSafetyValidationMethods.CheckActivationTest));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcSafetyValidationMethods.CheckActivationTest));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, PlcSafetyValidationMethods.CheckActivationTest));
        var (refused, raw) = await session.ReadAsync(new { operationId = "k", operation = "check_activation_test", testName = "AT_Door" });
        Assert.True(raw.IsError == true, refused.GetRawText());
        Assert.DoesNotContain(PlcSafetyValidationMethods.CheckActivationTest, session.Methods);
    }

    [Fact]
    public async Task DryRun_FiresTheProtectionGuards_AndCallsNoWrite()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(true,
            new { operationId = "lock", operation = "set_account_locking", settings = new Dictionary<string, object> { ["EnableLockUserAccountAtRunTime"] = false } },
            new { operationId = "udp", operation = "set_syslog", settings = new Dictionary<string, object> { ["EnableSystemLogging"] = true, ["TransportProtocol"] = "UDP" } },
            new { operationId = "display", operation = "set_display_password", passwordVariable = DisplayVariable },
            new { operationId = "policy", operation = "set_plc_password_policy", settings = new Dictionary<string, object> { ["PasswordPolicyEnabled"] = false } });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.Equal(new[] { "plc_lowers_cpu_protection", "plc_lowers_cpu_protection", "plc_raises_cpu_protection", "plc_lowers_cpu_protection" }, GuardIds(document));
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }

    [Fact]
    public async Task Writes_AreReadBack_AndShowInTheNextRead()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "lock", operation = "set_account_locking", settings = new Dictionary<string, object> { ["NumberOfFailedLoginAttempts"] = 3, ["LockAccountForDuration"] = 10 } },
            new { operationId = "syslog", operation = "set_syslog", settings = new Dictionary<string, object> { ["EnableSystemLogging"] = true, ["TransportProtocol"] = "TLSOnlyServerAuthentication", ["SysLogClientCertificateId"] = 7 } },
            new { operationId = "server", operation = "add_syslog_server", address = "10.0.0.5", port = 6514 },
            new { operationId = "project", operation = "create_project_syslog_server", name = "Central", settings = new Dictionary<string, object> { ["Address"] = "10.0.0.9", ["Tls"] = true } },
            new { operationId = "web", operation = "set_default_web_application", name = "MyApp" },
            new { operationId = "watch", operation = "set_watch_table_access", tableName = "Watch_1", access = "Read" },
            new { operationId = "displayForce", operation = "set_force_table_access", tableName = "Force_1", access = "Write", display = true },
            new { operationId = "language", operation = "set_plc_project_language", position = 2, culture = "de-DE" },
            new { operationId = "ip", operation = "set_ip_accessibility", moduleName = "CP 1543-1" },
            new { operationId = "display", operation = "set_display_password", passwordVariable = DisplayVariable },
            new { operationId = "legacy", operation = "set_legacy_plc_password_policy", settings = new Dictionary<string, object> { ["PasswordPolicyEnabled"] = true, ["MinimumLength"] = 8 } });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        foreach (var id in new[] { "lock", "syslog", "server", "project", "web", "watch", "displayForce", "language", "ip", "legacy" })
        {
            var check = Verification(document, id);
            Assert.True(check.GetProperty("success").GetBoolean(), check.GetRawText());
            Assert.Equal("readBack", check.GetProperty("check").GetString());
        }

        var display = Verification(document, "display");
        Assert.Equal("trusted", display.GetProperty("check").GetString());
        Assert.Contains("never readable", display.GetProperty("message").GetString());
        var displayResult = Item(document, 9).GetProperty("result");
        Assert.Equal(DisplayVariable, displayResult.GetProperty("passwordSources")[0].GetProperty("source").GetString());
        Assert.DoesNotContain("FakeDisplay!1", document.GetRawText());

        var (read, _) = await session.ReadAsync(new { operationId = "s", operation = "get_cpu_settings", plcName = "PLC_1" });
        var sections = Item(read, 0).GetProperty("result").GetProperty("plcs")[0].GetProperty("sections");
        Assert.Equal("3", Property(Section(sections, "accountLocking"), "NumberOfFailedLoginAttempts"));
        Assert.Equal("TLSOnlyServerAuthentication", Property(Section(sections, "syslog"), "TransportProtocol"));
        Assert.Equal("7", Property(Section(sections, "syslogAttributes"), "SysLogClientCertificateId"));
        Assert.Equal("10.0.0.5", Section(sections, "syslog").GetProperty("collections")[0].GetProperty("items")[0].GetProperty("key").GetString());
        Assert.Equal("Watch_1", Section(sections, "watchAndForceTableAccess").GetProperty("collections")[0].GetProperty("items")[0].GetProperty("key").GetString());
        Assert.Equal("Write", Property(Section(sections, "displayWatchAndForceTableAccess").GetProperty("collections")[1].GetProperty("items")[0], "Access"));
        Assert.Equal("CP 1543-1", Property(Section(sections, "ipAccessibility"), "PlcAccessCommunicationModule"));
        var project = Item(read, 0).GetProperty("result").GetProperty("project");
        Assert.Equal("8", Property(Section(project, "legacyPlcPasswordPolicy"), "MinimumLength"));
        Assert.Equal("Central", Section(project, "projectSyslogServers").GetProperty("collections")[0].GetProperty("items")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task UnknownSetting_IsRefusedBeforeTheWorker()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "x", operation = "set_account_locking", settings = new Dictionary<string, object> { ["Password"] = "x" } });
        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("'Password' is not a setting of set_account_locking", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }

    [Fact]
    public async Task PastedPassword_IsRedacted_AndRefused()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "d", operation = "set_display_password", passwordVariable = "not a variable!" });
        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("not a variable!", document.GetRawText());
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
    }

    [Fact]
    public async Task RemovingAMissingSyslogServer_FailsTargetNotFound_WithAFalseVerification()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false, new { operationId = "r", operation = "remove_syslog_server", address = "192.0.2.1" });
        Assert.False(raw.IsError == true, document.GetRawText());
        var item = Item(document, 0);
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("target_not_found", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("Nothing was changed.", item.GetProperty("failure").GetProperty("message").GetString());
        foreach (var check in document.GetProperty("verification").GetProperty("operations").EnumerateArray())
            Assert.False(check.GetProperty("success").GetBoolean());
    }
}
