using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcWebApi;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

public sealed class PlcWebApiServiceAndReadTests
{
    private static JsonElement Document(CallToolResult result)
    {
        var violations = StructuredContractInspector.FindViolations(result);
        Assert.True(violations.Count == 0, string.Join("; ", violations));
        return result.StructuredContent!.Value;
    }

    private static PlcOnlineReadRequest Op(string id, string operation, string target = "Line1") => new() { OperationId = id, Operation = operation, Target = target };

    [Fact]
    public async Task AnonymousTarget_NeverLogsIn()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var target = service.Configuration.Find("Line1")!;

        Assert.Equal("ping-Line1", await service.CallAsync(target, (client, ct) => client.PingAsync(ct), CancellationToken.None));
        Assert.Equal("run", await service.CallAsync(target, (client, ct) => client.ReadOperatingModeAsync(ct), CancellationToken.None));

        Assert.Equal(0, world.Plc("Line1").Logins);
        Assert.DoesNotContain(world.Calls, call => call.Contains("LoginAsync", StringComparison.Ordinal));
        Assert.Equal(1, world.ClientsCreated);
    }

    [Fact]
    public async Task AuthenticatedTarget_LogsInOnce_WithCredentialsFromTheEnvironment()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var target = service.Configuration.Find("Secure")!;

        await service.CallAsync(target, (client, ct) => client.PingAsync(ct), CancellationToken.None);
        await service.CallAsync(target, (client, ct) => client.VersionAsync(ct), CancellationToken.None);

        Assert.Equal(1, world.Plc("Secure").Logins);
        Assert.Contains("Secure:LoginAsync(operator)", world.Calls);
        Assert.DoesNotContain(world.Calls, call => call.Contains(FakePlcWebApiWorld.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredSession_LogsInAgainOnce_AndRepeatsTheCall()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var target = service.Configuration.Find("Secure")!;
        await service.CallAsync(target, (client, ct) => client.PingAsync(ct), CancellationToken.None);
        world.Plc("Secure").SessionExpired = true;

        Assert.Equal("run", await service.CallAsync(target, (client, ct) => client.ReadOperatingModeAsync(ct), CancellationToken.None));

        Assert.Equal(2, world.Plc("Secure").Logins);
        Assert.Equal(1, world.Plc("Secure").Logouts);
    }

    [Fact]
    public async Task PersistentPermissionDenied_IsReportedAfterOneRelogin()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var target = service.Configuration.Find("Secure")!;
        PlcWebApiException Denied() => new(WorkerFailureCategories.WebApiPermissionDenied, "Web API error 2 (PermissionDenied).",
            new PlcWebApiErrorInfo(2, "PermissionDenied", "Permission denied", null));
        await service.CallAsync(target, (client, ct) => client.PingAsync(ct), CancellationToken.None);
        world.Plc("Secure").Failures["GetPermissionsAsync"] = Denied;

        var attempts = 0;
        var exception = await Assert.ThrowsAsync<PlcWebApiException>(() => service.CallAsync<IReadOnlyList<string>>(target, (client, ct) =>
        {
            attempts++;
            if (attempts == 2) world.Plc("Secure").Failures["GetPermissionsAsync"] = Denied;
            return client.GetPermissionsAsync(ct);
        }, CancellationToken.None));

        Assert.Equal(WorkerFailureCategories.WebApiPermissionDenied, exception.Category);
        Assert.Equal(2, attempts);
        Assert.Equal(2, world.Plc("Secure").Logins);
    }

    [Fact]
    public async Task MissingCredentialVariables_FailAuthentication_NamingOnlyTheVariable()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly, environment: name => name == "TIA_TEST_WEBAPI_USER" ? "operator" : null);

        var exception = await Assert.ThrowsAsync<PlcWebApiException>(() =>
            service.CallAsync(service.Configuration.Find("Secure")!, (client, ct) => client.PingAsync(ct), CancellationToken.None));

        Assert.Equal(WorkerFailureCategories.WebApiAuthenticationFailed, exception.Category);
        Assert.Contains("TIA_TEST_WEBAPI_PASSWORD is not set", exception.Message);
        Assert.DoesNotContain("PingAsync", string.Join(",", world.Calls));
    }

    [Fact]
    public async Task Dispose_LogsOutAuthenticatedSessions_AndReleasesEveryClient()
    {
        var world = new FakePlcWebApiWorld();
        var service = world.Service(McpAccessMode.ReadOnly);
        await service.CallAsync(service.Configuration.Find("Secure")!, (client, ct) => client.PingAsync(ct), CancellationToken.None);
        await service.CallAsync(service.Configuration.Find("Line1")!, (client, ct) => client.PingAsync(ct), CancellationToken.None);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, world.Plc("Secure").Logouts);
        Assert.Equal(0, world.Plc("Line1").Logouts);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            service.CallAsync(service.Configuration.Find("Line1")!, (client, ct) => client.PingAsync(ct), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutBeingMapped()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CallAsync(service.Configuration.Find("Line1")!, (client, ct) => client.PingAsync(ct), cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PlcWebApiReadTools.PlcOnlineRead(service, new[] { Op("p", "ping") }, cancel.Token));
    }

    [Fact]
    public async Task CallsToOneTarget_RunOneAtATime_WhileOtherTargetsRunIndependently()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var secure = service.Configuration.Find("Secure")!;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Plc("Secure").Gate = gate;

        var first = service.CallAsync(secure, (client, ct) => client.PingAsync(ct), CancellationToken.None);
        await WaitUntil(() => world.CallsFor("Secure").Contains("Secure:PingAsync"));
        var second = service.CallAsync(secure, (client, ct) => client.VersionAsync(ct), CancellationToken.None);
        Assert.Equal("ping-Line1", await service.CallAsync(service.Configuration.Find("Line1")!, (client, ct) => client.PingAsync(ct), CancellationToken.None));
        await Task.Delay(100);

        // The second call has not reached the client (its login or request would be logged) while the first holds the target.
        Assert.Equal(new[] { "Secure:LoginAsync(operator)", "Secure:PingAsync" }, world.CallsFor("Secure"));
        Assert.False(second.IsCompleted);
        gate.SetResult();
        Assert.Equal("ping-Secure", await first);
        Assert.Equal(4.1, await second);
        Assert.Equal(new[] { "Secure:LoginAsync(operator)", "Secure:PingAsync", "Secure:VersionAsync" }, world.CallsFor("Secure"));
        Assert.Equal(1, world.Plc("Secure").Logins);
    }

    [Fact]
    public async Task WaitingForATarget_IsCancellable()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var line1 = service.Configuration.Find("Line1")!;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Plc("Line1").Gate = gate;
        var first = service.CallAsync(line1, (client, ct) => client.PingAsync(ct), CancellationToken.None);
        await WaitUntil(() => world.CallsFor("Line1").Length == 1);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CallAsync(line1, (client, ct) => client.VersionAsync(ct), cancel.Token));

        gate.SetResult();
        Assert.Equal("ping-Line1", await first);
        Assert.Equal(new[] { "Line1:PingAsync" }, world.CallsFor("Line1"));
    }

    [Fact]
    public async Task ConcurrentFirstCalls_CreateOneClientPerTarget()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var targets = service.Configuration.Targets;

        await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            service.CallAsync(targets[index % targets.Count], (client, ct) => client.PingAsync(ct), CancellationToken.None))));

        Assert.Equal(targets.Count, world.ClientsCreated);
        Assert.Equal(1, world.Plc("Secure").Logins);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task Read_RunsEveryOperationIndependently_InEveryMode()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        var operations = new List<PlcOnlineReadRequest>
        {
            Op("ping", "ping"), Op("version", "version"), Op("permissions", "permissions", "Secure"), Op("mode", "read_operating_mode"),
            Op("selector", "read_mode_selector"), Op("cpu", "read_cpu_type"), Op("runtime", "read_runtime_information"),
            Op("time", "read_system_time"), Op("project", "read_project_information"), Op("browse", "browse_tags"),
            new() { OperationId = "blocks", Operation = "browse_tags", Target = "Line1", Type = "code_blocks" },
            new() { OperationId = "tags", Operation = "read_tags", Target = "Line1", Names = new() { "\"DB_Line\".Speed", "\"DB_Line\".Label" } },
            new() { OperationId = "diag", Operation = "read_diagnostic_buffer", Target = "Line1", Count = 10, Language = "de-DE" },
            Op("alarms", "browse_alarms"), Op("modules", "browse_modules"),
            new() { OperationId = "status", Operation = "read_module_status", Target = "Line1", Hwid = 64 },
            new() { OperationId = "leds", Operation = "read_module_leds", Target = "Line1", Hwid = 64 },
            new() { OperationId = "syslog", Operation = "browse_syslog", Target = "Line1", Count = 5, First = 1 },
        };

        var document = Document(await PlcWebApiReadTools.PlcOnlineRead(service, operations.ToArray()));

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("plc_online_read", document.GetProperty("tool").GetString());
        Assert.Equal("1.0", document.GetProperty("contractVersion").GetString());
        var items = document.GetProperty("batch").GetProperty("operations").EnumerateArray().ToDictionary(item => item.GetProperty("operationId").GetString()!);
        Assert.Equal(operations.Count, items.Count);
        Assert.All(items.Values, item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        Assert.Equal("ping-Line1", items["ping"].GetProperty("result").GetProperty("id").GetString());
        Assert.Equal(4.1, items["version"].GetProperty("result").GetProperty("apiVersion").GetDouble());
        Assert.Contains("write_value", items["permissions"].GetProperty("result").GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("run", items["mode"].GetProperty("result").GetProperty("operatingMode").GetString());
        Assert.Equal("6ES7 516-3AP03-0AB0", items["cpu"].GetProperty("result").GetProperty("orderNumber").GetString());
        Assert.Equal(1.2, items["runtime"].GetProperty("result").GetProperty("cycleTimeCurrentMs").GetDouble());
        Assert.Equal("2026-10-31T12:00:00.0000000Z", items["time"].GetProperty("result").GetProperty("timestamp").GetString());
        Assert.Equal("DB_Line", items["browse"].GetProperty("result").GetProperty("variables")[0].GetProperty("name").GetString());
        Assert.Equal("Main", items["blocks"].GetProperty("result").GetProperty("blocks")[0].GetProperty("name").GetString());
        var tags = items["tags"].GetProperty("result").GetProperty("tags");
        Assert.Equal(1500, tags[0].GetProperty("value").GetInt32());
        // Values are the PLC's JSON as sent: a date-like string stays the string the PLC returned.
        Assert.Equal("2021-04-26T16:53:22Z", tags[1].GetProperty("value").GetString());
        Assert.Equal("de-DE", items["diag"].GetProperty("result").GetProperty("language").GetString());
        Assert.Equal("123456", items["alarms"].GetProperty("result").GetProperty("entries")[0].GetProperty("id").GetString());
        Assert.Equal(64, items["modules"].GetProperty("result").GetProperty("nodes")[0].GetProperty("hwid").GetInt64());
        Assert.Equal("good", items["status"].GetProperty("result").GetProperty("own").GetString());
        Assert.Equal("run_stop", items["leds"].GetProperty("result").GetProperty("leds")[0].GetProperty("type").GetString());
        Assert.Equal(2, items["syslog"].GetProperty("result").GetProperty("countTotal").GetInt64());
        Assert.Contains("Line1:BrowseDiagnosticBufferAsync(de-DE|10)", world.Calls);
        Assert.Contains("Line1:BrowseSyslogAsync(5|1)", world.Calls);
        Assert.Contains("Line1:BrowseAlarmsAsync(en-US||)", world.Calls);
        Assert.Contains("Line1:BrowseVariablesAsync(children|)", world.Calls);
    }

    [Fact]
    public async Task Read_ReportsUnknownTargetsAndFailuresPerItem_AndKeepsGoing()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);
        world.Plc("Line1").Failures["ReadOperatingModeAsync"] = () => new PlcWebApiException(WorkerFailureCategories.WebApiUnreachable,
            "PLC Web API target 'Line1' (192.0.2.10) could not be reached.");

        var document = Document(await PlcWebApiReadTools.PlcOnlineRead(service, new[]
        {
            Op("unknown", "ping", "Line9"), Op("mode", "read_operating_mode"), Op("ping", "ping"),
            new PlcOnlineReadRequest { OperationId = "tags", Operation = "read_tags", Target = "Line1", Names = new() { "\"Nope\".x", "\"DB_Line\".Speed" } },
        }));

        Assert.False(document.GetProperty("success").GetBoolean());
        var items = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal("target_not_found", items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("Configured targets: Line1, Secure, Lab", items[0].GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal("webapi_unreachable", items[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("succeeded", items[2].GetProperty("status").GetString());
        var tags = items[3];
        Assert.Equal("failed", tags.GetProperty("status").GetString());
        Assert.Equal("webapi_rejected", tags.GetProperty("failure").GetProperty("category").GetString());
        var perTag = tags.GetProperty("result").GetProperty("tags");
        Assert.Equal(200, perTag[0].GetProperty("webApiError").GetProperty("code").GetInt32());
        Assert.True(perTag[1].GetProperty("success").GetBoolean());
        Assert.Equal(1, tags.GetProperty("result").GetProperty("read").GetInt32());
    }

    [Fact]
    public async Task Read_WarnsOnEveryItemOfATargetWithoutCertificateValidation()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);

        var document = Document(await PlcWebApiReadTools.PlcOnlineRead(service, new[] { Op("a", "ping", "Lab"), Op("b", "version", "Lab"), Op("c", "ping") }));

        var warning = Assert.Single(document.GetProperty("warnings").EnumerateArray()).GetString();
        Assert.Contains("certificate validation 'none'", warning);
        var items = document.GetProperty("batch").GetProperty("operations");
        Assert.Contains("certificate validation 'none'", Assert.Single(items[0].GetProperty("warnings").EnumerateArray()).GetString());
        Assert.Single(items[1].GetProperty("warnings").EnumerateArray());
        Assert.Empty(items[2].GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Read_RejectsInvalidInputBeforeAnyCall()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly);

        var result = await PlcWebApiReadTools.PlcOnlineRead(service, new[] { Op("a", "read_tags") });

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Document(result).GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(world.Calls);
        Assert.Equal(0, world.ClientsCreated);
    }

    [Fact]
    public async Task Read_WithoutConfiguration_ExplainsHowToConfigure()
    {
        var world = new FakePlcWebApiWorld();
        using var service = world.Service(McpAccessMode.ReadOnly, configuration: PlcWebApiConfiguration.Empty);

        var document = Document(await PlcWebApiReadTools.PlcOnlineRead(service, new[] { Op("a", "ping") }));

        var failure = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("target_not_found", failure.GetProperty("category").GetString());
        Assert.Contains("--plc-webapi-config", failure.GetProperty("message").GetString());
    }
}
