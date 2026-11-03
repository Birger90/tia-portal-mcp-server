using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcWebApi;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

public sealed class PlcWebApiWriteTests : IDisposable
{
    private readonly TempAuditDirectory _files = new();
    private readonly FakePlcWebApiWorld _world = new();
    private readonly RecordingAuditSink _audit = new();

    public PlcWebApiWriteTests() => Directory.CreateDirectory(_files.Path);

    public void Dispose() => _files.Dispose();

    private PlcWebApiService Service(McpAccessMode mode) => _world.Service(mode, _audit);

    private static PlcOnlineWriteRequest Stop(string id = "stop", string target = "Line1")
        => new() { OperationId = id, Operation = "change_operating_mode", Target = target, OperatingMode = "stop" };

    private static PlcOnlineWriteRequest WriteTags(string id, string target, params (string Name, string Json)[] tags)
        => new()
        {
            OperationId = id, Operation = "write_tags", Target = target,
            Tags = tags.Select(tag => new PlcOnlineTagWrite { Name = tag.Name, Value = JsonDocument.Parse(tag.Json).RootElement.Clone() }).ToList(),
        };

    private static UserConfirmation Answer(string action, Action<string>? onPrompt = null) => new(true, (request, _) =>
    {
        onPrompt?.Invoke(request.Message);
        return ValueTask.FromResult(new ElicitResult
        {
            Action = action,
            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
        });
    }, TimeSpan.FromSeconds(5));

    private static async Task<JsonElement> Run(PlcWebApiService service, IReadOnlyList<PlcOnlineWriteRequest> items, bool dryRun = false,
        UserConfirmation? confirmation = null, bool expectError = false)
    {
        var result = await service.Execution.RunAsync(new PlcWebApiWriteDomain(service), new WriteCall<PlcOnlineWriteRequest>(items, dryRun),
            new WriteConfirmationContext(confirmation), PlcWebApiWriteGate.Strategy);
        var violations = StructuredContractInspector.FindViolations(result);
        Assert.True(violations.Count == 0, string.Join("; ", violations));
        Assert.Equal(expectError, result.IsError == true);
        return result.StructuredContent!.Value;
    }

    private static string[] GuardIds(JsonElement document)
        => document.GetProperty("guards").EnumerateArray().Select(guard => guard.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task DryRun_PreviewsEffectsAndGuards_WithoutContactingAnyPlc()
    {
        using var service = Service(McpAccessMode.ReadWrite);

        var document = await Run(service, new[] { Stop(), WriteTags("speed", "Lab", ("\"DB_Line\".Speed", "2")) }, dryRun: true);

        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { "online_changes_real_controller", "operating_mode_stop" }, GuardIds(document));
        Assert.All(document.GetProperty("guards").EnumerateArray(), guard => Assert.False(guard.GetProperty("acknowledged").GetBoolean()));
        var effects = document.GetProperty("effects");
        Assert.Equal("Plc.RequestChangeOperatingMode", effects[0].GetProperty("effect").GetProperty("apiMethod").GetString());
        Assert.Equal("192.0.2.10", effects[0].GetProperty("effect").GetProperty("address").GetString());
        Assert.Equal(2, effects[1].GetProperty("effect").GetProperty("tags")[0].GetProperty("value").GetInt32());
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("certificate validation 'none'"));
        Assert.Equal(JsonValueKind.Null, document.GetProperty("batch").ValueKind);
        Assert.Empty(_world.Calls);
        Assert.Equal(0, _world.ClientsCreated);
        var record = Assert.Single(_audit.Records);
        Assert.Equal("plc_online", record.RecordKind);
        Assert.Null(record.Binding);
        Assert.Null(record.ProjectPath);
        Assert.Equal("preview", record.Phase);
    }

    [Fact]
    public async Task ReadWrite_AsksOnceForTheWholeCall_AndAppliesInOrder()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        var prompts = new List<string>();

        var document = await Run(service, new[]
        {
            Stop(), WriteTags("speed", "Line1", ("\"DB_Line\".Speed", "2"), ("\"DB_Line\".Running", "false")),
            new PlcOnlineWriteRequest { OperationId = "ack", Operation = "acknowledge_alarm", Target = "Secure", AlarmId = "123456" },
        }, confirmation: Answer("accept", prompts.Add));

        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var prompt = Assert.Single(prompts);
        Assert.Contains("3 online change(s)", prompt);
        Assert.Contains("request operating mode STOP", prompt);
        Assert.Contains("STOP is requested for \"Line1\" (\"192.0.2.10\")", prompt);
        Assert.StartsWith("This call changes 3 item(s) online", prompt);
        Assert.All(document.GetProperty("guards").EnumerateArray(), guard => Assert.True(guard.GetProperty("acknowledged").GetBoolean()));
        Assert.Equal("stop", _world.Plc("Line1").OperatingMode);
        Assert.Equal("2", _world.Plc("Line1").Tags["\"DB_Line\".Speed"]);
        Assert.Equal(new[] { "123456" }, _world.Plc("Secure").AcknowledgedAlarms);
        Assert.Equal(new[]
        {
            "Line1:RequestChangeOperatingModeAsync(stop)", "Line1:WriteVariableAsync(\"DB_Line\".Speed=2)",
            "Line1:WriteVariableAsync(\"DB_Line\".Running=false)", "Secure:LoginAsync(operator)", "Secure:AcknowledgeAlarmAsync(123456)",
        }, _world.Calls.ToArray());
        var record = Assert.Single(_audit.Records);
        Assert.Equal("user", record.Confirmation.By);
        Assert.Equal("confirmed", record.Confirmation.Outcome);
        Assert.All(record.Guards, guard => Assert.Equal("user", guard.SatisfiedBy));
        Assert.DoesNotContain(FakePlcWebApiWorld.Password, record.ResponseText);
        Assert.DoesNotContain(FakePlcWebApiWorld.Password, record.RequestedOperations!.Value.GetRawText());
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task ReadWrite_RefusedConfirmation_ChangesNothing(string action)
    {
        using var service = Service(McpAccessMode.ReadWrite);

        var document = await Run(service, new[] { Stop() }, confirmation: Answer(action), expectError: true);

        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(_world.Calls);
        Assert.Equal("run", _world.Plc("Line1").OperatingMode);
    }

    [Fact]
    public async Task ReadWrite_WithoutElicitationSupport_IsDenied()
    {
        using var service = Service(McpAccessMode.ReadWrite);

        var result = await PlcWebApiWriteTools.PlcOnlineWrite(service, new[] { Stop() }, server: null);

        Assert.True(result.IsError);
        var document = result.StructuredContent!.Value;
        Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("elicitation", document.GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(_world.Calls);
    }

    [Fact]
    public async Task Full_RunsUnderPolicy_AndStopsAtTheFirstFailure()
    {
        using var service = Service(McpAccessMode.Full);

        var document = await Run(service, new[]
        {
            WriteTags("tags", "Line1", ("\"DB_Line\".Speed", "7"), ("\"Nope\".x", "1"), ("\"DB_Line\".Running", "false")),
            Stop("later"),
        });

        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        var items = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal("webapi_rejected", items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("1 earlier tag(s) stay written", items[0].GetProperty("failure").GetProperty("message").GetString());
        var evidence = items[0].GetProperty("result");
        Assert.Equal(1, evidence.GetProperty("written").GetInt32());
        Assert.Equal(200, evidence.GetProperty("tags")[1].GetProperty("webApiError").GetProperty("code").GetInt32());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
        Assert.Contains("partial_write_no_rollback", GuardIds(document));
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("Inspect it with plc_online_read"));
        Assert.Equal("run", _world.Plc("Line1").OperatingMode);
        Assert.Equal("7", _world.Plc("Line1").Tags["\"DB_Line\".Speed"]);
        Assert.Equal("true", _world.Plc("Line1").Tags["\"DB_Line\".Running"]);
        var record = Assert.Single(_audit.Records);
        Assert.Equal("policy", record.Confirmation.By);
        Assert.Equal(new[] { "failed", "skipped" }, record.Items.Select(item => item.Status));
    }

    [Fact]
    public async Task UnknownTarget_FailsTheWholeCallBeforeAnyChange()
    {
        using var service = Service(McpAccessMode.Full);

        var document = await Run(service, new[] { Stop(), Stop("other", "Nowhere") }, expectError: true);

        Assert.Equal("error", document.GetProperty("phase").GetString());
        Assert.Equal("target_not_found", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(_world.Calls);
    }

    [Fact]
    public async Task OnlyStopFiresTheStopGuard_AndEveryCallFiresTheRealControllerGuard()
    {
        using var service = Service(McpAccessMode.Full);
        var run = new PlcOnlineWriteRequest { OperationId = "run", Operation = "change_operating_mode", Target = "Line1", OperatingMode = "run" };
        var flash = new PlcOnlineWriteRequest { OperationId = "flash", Operation = "flash_leds", Target = "Line1", Hwid = 64 };
        var time = new PlcOnlineWriteRequest { OperationId = "time", Operation = "set_system_time", Target = "Line1", Timestamp = "2026-11-01T08:30:00+01:00" };

        var document = await Run(service, new[] { run, flash, time }, dryRun: true);

        Assert.Equal(new[] { "online_changes_real_controller" }, GuardIds(document));
        Assert.True(Assert.Single(document.GetProperty("guards").EnumerateArray()).GetProperty("acknowledged").GetBoolean());
        Assert.Equal("2026-11-01T07:30:00.000Z", document.GetProperty("effects")[2].GetProperty("effect").GetProperty("timestamp").GetString());

        var applied = await Run(service, new[] { run, flash, time });
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(new uint[] { 64 }, _world.Plc("Line1").FlashedModules);
        Assert.Equal(new DateTime(2026, 11, 1, 7, 30, 0, DateTimeKind.Utc), _world.Plc("Line1").SystemTime);
        Assert.Equal(DateTimeKind.Utc, _world.Plc("Line1").SystemTime.Kind);
    }

    [Fact]
    public async Task CreateBackup_WritesTheFile_AndRefusesToOverwriteUnlessAsked()
    {
        using var service = Service(McpAccessMode.Full);
        var path = Path.Combine(_files.Path, "line1.s7backup");
        var create = new PlcOnlineWriteRequest { OperationId = "backup", Operation = "create_backup", Target = "Line1", Path = path };

        var document = await Run(service, new[] { create });

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("FAKE-S7-BACKUP"u8.ToArray(), File.ReadAllBytes(path));
        var result = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(Convert.ToHexString(SHA256.HashData("FAKE-S7-BACKUP"u8.ToArray())).ToLowerInvariant(), result.GetProperty("sha256").GetString());
        Assert.False(result.GetProperty("replaced").GetBoolean());
        Assert.Empty(Directory.GetFiles(_files.Path, "*.tmp"));

        var refused = await Run(service, new[] { create }, expectError: true);
        Assert.Equal("validation_error", refused.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("already exists; set overwrite:true", refused.GetProperty("error").GetProperty("message").GetString());

        _world.Plc("Line1").Backup = new byte[] { 1, 2, 3 };
        create.Overwrite = true;
        var replaced = await Run(service, new[] { create });
        Assert.True(replaced.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("replaced").GetBoolean());
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));

        var missingFolder = new PlcOnlineWriteRequest { OperationId = "b", Operation = "create_backup", Target = "Line1", Path = Path.Combine(_files.Path, "no", "x.bin") };
        Assert.Contains("does not exist", (await Run(service, new[] { missingFolder }, expectError: true)).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task RestoreBackup_PinsTheFile_FiresItsGuard_AndNeedsAConfiguredUser()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        var path = Path.Combine(_files.Path, "restore.s7backup");
        File.WriteAllBytes(path, new byte[] { 9, 8, 7, 6 });
        var restore = new PlcOnlineWriteRequest { OperationId = "restore", Operation = "restore_backup", Target = "Secure", Path = path };

        var preview = await Run(service, new[] { restore }, dryRun: true);
        Assert.Equal(new[] { "online_changes_real_controller", "restore_backup_replaces_program" }, GuardIds(preview));
        var file = preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("file");
        Assert.Equal(4, file.GetProperty("bytes").GetInt64());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 9, 8, 7, 6 })).ToLowerInvariant(), file.GetProperty("sha256").GetString());

        var applied = await Run(service, new[] { restore }, confirmation: Answer("accept"));
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal((path, FakePlcWebApiWorld.User), Assert.Single(_world.Plc("Secure").Restores));
        Assert.DoesNotContain(FakePlcWebApiWorld.Password, applied.GetRawText());

        var anonymous = new PlcOnlineWriteRequest { OperationId = "restore", Operation = "restore_backup", Target = "Line1", Path = path };
        var refused = await Run(service, new[] { anonymous }, dryRun: true, expectError: true);
        Assert.Contains("needs a target with userEnv/passwordEnv", refused.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task RestoreBackup_FileChangedWhileThePromptWasOpen_IsRefused()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        var path = Path.Combine(_files.Path, "restore.s7backup");
        File.WriteAllBytes(path, new byte[] { 1 });
        var restore = new PlcOnlineWriteRequest { OperationId = "restore", Operation = "restore_backup", Target = "Secure", Path = path };

        var document = await Run(service, new[] { restore }, confirmation: Answer("accept", _ => File.WriteAllBytes(path, new byte[] { 2, 2 })),
            expectError: true);

        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Equal("binding_conflict", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("consequences changed while confirmation was pending", document.GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(_world.Plc("Secure").Restores);
        Assert.DoesNotContain(_world.Calls, call => call.Contains("RestoreBackupAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreBackup_HelperFailure_WarnsThatThePlcMayAlreadyHaveChanged()
    {
        using var service = Service(McpAccessMode.Full);
        var path = Path.Combine(_files.Path, "restore.s7backup");
        File.WriteAllBytes(path, new byte[] { 5 });
        _world.Plc("Secure").Failures["RestoreBackupAsync"] = () => new PlcWebApiException(WorkerFailureCategories.WebApiUnreachable,
            "The backup restore did not finish within 10 minutes.");

        var document = await Run(service, new[] { new PlcOnlineWriteRequest { OperationId = "r", Operation = "restore_backup", Target = "Secure", Path = path } });

        var failure = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("webapi_unreachable", failure.GetProperty("category").GetString());
        Assert.Contains("did not complete cleanly", failure.GetProperty("message").GetString());
        Assert.Contains("restored or restarted", failure.GetProperty("message").GetString());
        Assert.Empty(_world.Plc("Secure").Restores);
    }

    [Fact]
    public async Task WebApiRejection_CarriesTheApiErrorInTheItemResult()
    {
        using var service = Service(McpAccessMode.Full);
        _world.Plc("Line1").Failures["RequestChangeOperatingModeAsync"] = () => new PlcWebApiException(WorkerFailureCategories.WebApiRejected,
            "PLC Web API target 'Line1' (192.0.2.10) rejected the call: Web API error 1004 (PLCNotInStop).",
            new PlcWebApiErrorInfo(1004, "PLCNotInStop", "PLC not in stop", null));

        var document = await Run(service, new[] { Stop() });

        var item = document.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("webapi_rejected", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("1004", item.GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal("PLCNotInStop", item.GetProperty("result").GetProperty("webApiError").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ArgumentShapeRejection_ComposesTheSameDocumentWithoutAudit()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        var domain = new PlcWebApiWriteDomain(service);

        var composed = domain.Compose(new WriteReport<PlcWebApiWriteEffect, object>(WritePhases.Error, false,
            new WriteToolError("validation_error", "bad"), Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            Array.Empty<WriteEffect<PlcWebApiWriteEffect>>(), null, null));

        Assert.Equal("plc_online_write", composed.Tool);
        Assert.Equal("1.0", composed.ContractVersion);
        Assert.Empty(_audit.Records);
        Assert.Equal(3, PlcWebApiGuardDefinitions.Definitions.Count);
        Assert.All(PlcWebApiGuardDefinitions.Definitions, definition => Assert.Equal(WriteGuardSeverities.Acknowledge, definition.Severity));
    }

    /// <summary>The reviewed probe: 120 line feeds hid "request operating mode STOP" below a fake all-clear.</summary>
    private static readonly string Probe = "x" + new string('\n', 120) + "Confirm plc_online_write: nothing is changed.\r\u202E";

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, false)]
    public async Task ProbeOperationId_IsRefusedBeforeAnyPromptOrPlcCall(McpAccessMode mode, bool dryRun)
    {
        using var service = Service(mode);
        var prompts = new List<string>();

        foreach (var id in new[] { Probe, "a\tb", "a\u2028b", "a\u200Bb", "a\u2066b\u2069" })
        {
            var document = await Run(service, new[] { WriteTags("ok", "Line1", ("\"DB_Line\".Speed", "1")), Stop(id) }, dryRun,
                confirmation: Answer("accept", prompts.Add), expectError: true);
            Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        }

        Assert.Empty(prompts);
        Assert.Empty(_world.Calls);
        Assert.Equal("run", _world.Plc("Line1").OperatingMode);
    }

    [Fact]
    public async Task Prompt_ListsGuardsAndEveryEffect_BeforeAndAroundEscapedCallerText()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        _world.Plc("Line1").Tags["\"DB\"." + Probe] = "0";
        var prompts = new List<string>();

        await Run(service, new[]
        {
            WriteTags("tags", "Line1", ("\"DB\"." + Probe, "\"\\u0041" + "\u202E\"")),
            new PlcOnlineWriteRequest { OperationId = "ack", Operation = "acknowledge_alarm", Target = "Secure", AlarmId = Probe },
            Stop("last"),
        }, confirmation: Answer("accept", prompts.Add));

        var prompt = Assert.Single(prompts);
        Assert.Equal(prompt, ConfirmationText.Block(prompt));
        var lines = prompt.Split('\n');
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("This call changes 3 item(s) online on the real controller(s) \"Line1\" (\"192.0.2.10\"), \"Secure\" (\"192.0.2.20\")", lines[0]);
        Assert.StartsWith("STOP is requested for \"Line1\" (\"192.0.2.10\")", lines[1]);
        Assert.StartsWith("Confirm plc_online_write: 3 online change(s)", lines[2]);
        Assert.StartsWith("1. write 1 tag(s) (simple): \"\\\"DB\\\".x\\n\\n", lines[3]);
        Assert.EndsWith("= \"\\u0041\\u202E\" on target \"Line1\" at \"192.0.2.10\" (PlcProgram.Write); operationId \"tags\"", lines[3]);
        Assert.StartsWith("2. acknowledge alarm \"x\\n\\n", lines[4]);
        Assert.Equal("3. request operating mode STOP on target \"Line1\" at \"192.0.2.10\" (Plc.RequestChangeOperatingMode); operationId \"last\"", lines[5]);
    }

    [Fact]
    public async Task Prompt_ShowsTagValuesInFullUpToTheBound_OtherwiseLengthAndSha256()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        var shown = JsonSerializer.Serialize(new string('s', PlcWebApiWriteDomain.MaxPromptValueChars - 2));
        var hashed = JsonSerializer.Serialize(new string('h', 2000));
        _world.Plc("Line1").Tags["\"DB_Line\".Text"] = "\"\"";
        var prompts = new List<string>();

        var document = await Run(service, new[] { WriteTags("tags", "Line1", ("\"DB_Line\".Label", shown), ("\"DB_Line\".Text", hashed)) },
            confirmation: Answer("accept", prompts.Add));

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var prompt = Assert.Single(prompts);
        Assert.Contains("\"\\\"DB_Line\\\".Label\" = " + shown + ",", prompt);
        var sha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hashed))).ToLowerInvariant();
        Assert.Contains($"\"\\\"DB_Line\\\".Text\" = <JSON value of {hashed.Length} characters, sha256 {sha256}>", prompt);
        Assert.Equal(hashed, _world.Plc("Line1").Tags["\"DB_Line\".Text"]);
    }

    [Fact]
    public async Task CreateBackup_ExpiredSession_RepeatsOnlyThePlcCreateBackupRequest()
    {
        using var service = Service(McpAccessMode.Full);
        var target = service.Configuration.Find("Secure")!;
        await service.CallAsync(target, (client, ct) => client.PingAsync(ct), default);
        _world.Plc("Secure").SessionExpired = true;
        var path = Path.Combine(_files.Path, "secure.s7backup");

        var document = await Run(service, new[] { new PlcOnlineWriteRequest { OperationId = "b", Operation = "create_backup", Target = "Secure", Path = path } });

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("FAKE-S7-BACKUP"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(new[] { "RequestBackupAsync", "LogoutAsync", "LoginAsync(operator)", "RequestBackupAsync", "DownloadBackupAsync(ticket-1)" },
            _world.CallsFor("Secure").Skip(2).Select(call => call["Secure:".Length..]));
    }

    [Fact]
    public async Task CreateBackup_RefusedTicketDownload_IsNotRepeatedAndWritesNoFile()
    {
        using var service = Service(McpAccessMode.Full);
        _world.Plc("Secure").Failures["DownloadBackupAsync"] = () => new PlcWebApiException(WorkerFailureCategories.WebApiPermissionDenied,
            "Web API error 2 (PermissionDenied).", new PlcWebApiErrorInfo(2, "PermissionDenied", "Permission denied", null));
        var path = Path.Combine(_files.Path, "secure.s7backup");

        var document = await Run(service, new[] { new PlcOnlineWriteRequest { OperationId = "b", Operation = "create_backup", Target = "Secure", Path = path } });

        var failure = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("webapi_permission_denied", failure.GetProperty("category").GetString());
        Assert.Equal(new[] { "LoginAsync(operator)", "RequestBackupAsync", "DownloadBackupAsync(ticket-1)" },
            _world.CallsFor("Secure").Select(call => call["Secure:".Length..]));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_files.Path));
    }

    [Fact]
    public async Task RestoreBackup_OversizedFile_IsRefusedByItsLength()
    {
        using var service = Service(McpAccessMode.ReadWrite);
        var path = Path.Combine(_files.Path, "huge.s7backup");
        using (var stream = File.Create(path)) stream.SetLength(PlcWebApiWriteDomain.MaxRestoreBytes + 1);

        var document = await Run(service, new[] { new PlcOnlineWriteRequest { OperationId = "r", Operation = "restore_backup", Target = "Secure", Path = path } },
            dryRun: true, expectError: true);

        Assert.Contains($"exceeds {PlcWebApiWriteDomain.MaxRestoreBytes} bytes", document.GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(_world.Calls);
    }
}
