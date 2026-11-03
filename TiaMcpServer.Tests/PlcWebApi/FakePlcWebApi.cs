using System.Collections.Concurrent;
using System.Text.Json;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcWebApi;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Tests.PlcWebApi;

/// <summary>
/// In-memory stand-in for every configured PLC: per target state (operating mode, tags, alarms,
/// modules), an ordered call log, login/logout counters and scripted failures. It implements the
/// Siemens-free <see cref="IPlcWebApiClient"/> seam, so no network or Siemens type is involved.
/// </summary>
internal sealed class FakePlcWebApiWorld : IPlcWebApiClientFactory
{
    public const string User = "operator";
    public const string Password = "s3cr3t-pw!";

    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentDictionary<string, FakePlc> Plcs { get; } = new(StringComparer.Ordinal);

    public int ClientsCreated;

    public FakePlc Plc(string target) => Plcs.GetOrAdd(target, _ => new FakePlc());

    public IPlcWebApiClient Create(PlcWebApiTarget target)
    {
        Interlocked.Increment(ref ClientsCreated);
        return new FakePlcWebApiClient(this, target);
    }

    public string[] CallsFor(string target) => Calls.Where(call => call.StartsWith(target + ":", StringComparison.Ordinal)).ToArray();

    public static PlcWebApiConfiguration Configuration(string? sourcePath = "/fixture/plc-webapi.json") => new(sourcePath, new[]
    {
        new PlcWebApiTarget("Line1", "192.0.2.10", null, null, PlcWebApiCertificateValidation.System, null, TimeSpan.FromSeconds(30)),
        new PlcWebApiTarget("Secure", "192.0.2.20", "TIA_TEST_WEBAPI_USER", "TIA_TEST_WEBAPI_PASSWORD", PlcWebApiCertificateValidation.Thumbprint,
            new string('A', 40), TimeSpan.FromSeconds(30)),
        new PlcWebApiTarget("Lab", "192.0.2.30", null, null, PlcWebApiCertificateValidation.None, null, TimeSpan.FromSeconds(30)),
    });

    public static string? Environment(string name) => name switch
    {
        "TIA_TEST_WEBAPI_USER" => User,
        "TIA_TEST_WEBAPI_PASSWORD" => Password,
        _ => null,
    };

    public PlcWebApiService Service(McpAccessMode mode, IWriteAuditSink? audit = null, PlcWebApiConfiguration? configuration = null,
        Func<string, string?>? environment = null)
        => new(configuration ?? Configuration(), new OperationAccessPolicy(mode), this, audit ?? new RecordingAuditSink(), environment ?? Environment);
}

internal sealed class RecordingAuditSink : IWriteAuditSink
{
    public List<WriteAuditRecord> Records { get; } = new();

    public void Append(WriteAuditRecord record)
    {
        lock (Records) Records.Add(record);
    }
}

/// <summary>Scriptable state of one fake controller.</summary>
internal sealed class FakePlc
{
    public string OperatingMode { get; set; } = "run";

    public string ModeSelector { get; set; } = "run";

    public DateTime SystemTime { get; set; } = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    public ConcurrentDictionary<string, string> Tags { get; } = new(StringComparer.Ordinal)
    {
        ["\"DB_Line\".Speed"] = "1500",
        ["\"DB_Line\".Running"] = "true",
        ["\"DB_Line\".Label"] = "\"2021-04-26T16:53:22Z\"",
    };

    public List<string> AcknowledgedAlarms { get; } = new();

    public List<uint> FlashedModules { get; } = new();

    public byte[] Backup { get; set; } = "FAKE-S7-BACKUP"u8.ToArray();

    public List<(string Path, string User)> Restores { get; } = new();

    public int Logins;

    public int Logouts;

    public bool SessionExpired { get; set; }

    /// <summary>Method name (as the interface names it, e.g. "PingAsync") to the exception it throws next.</summary>
    public ConcurrentDictionary<string, Func<PlcWebApiException>> Failures { get; } = new(StringComparer.Ordinal);

    /// <summary>Calls that block until released (cancellation tests).</summary>
    public TaskCompletionSource? Gate { get; set; }
}

internal sealed class FakePlcWebApiClient : IPlcWebApiClient
{
    private readonly FakePlcWebApiWorld _world;
    private readonly PlcWebApiTarget _target;
    private bool _session;

    public FakePlcWebApiClient(FakePlcWebApiWorld world, PlcWebApiTarget target)
    {
        _world = world;
        _target = target;
    }

    public bool Disposed { get; private set; }

    private FakePlc Plc => _world.Plc(_target.Name);

    public bool HasSession => _session;

    public Task LoginAsync(string userName, string password, CancellationToken cancellationToken)
    {
        Record("LoginAsync", userName);
        Fail("LoginAsync");
        if (userName != FakePlcWebApiWorld.User || password != FakePlcWebApiWorld.Password)
            throw new PlcWebApiException(WorkerFailureCategories.WebApiAuthenticationFailed, "Api.Login failed: Web API error 100 (LoginFailed).",
                new PlcWebApiErrorInfo(100, "LoginFailed", "Login failed", null));
        Interlocked.Increment(ref Plc.Logins);
        Plc.SessionExpired = false;
        _session = true;
        return Task.CompletedTask;
    }

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        Record("LogoutAsync");
        _session = false;
        Interlocked.Increment(ref Plc.Logouts);
        return Task.CompletedTask;
    }

    public void DropSession() => _session = false;

    public Task<string> PingAsync(CancellationToken cancellationToken) => Call("PingAsync", () => "ping-" + _target.Name);

    public Task<double> VersionAsync(CancellationToken cancellationToken) => Call("VersionAsync", () => 4.1);

    public Task<IReadOnlyList<string>> GetPermissionsAsync(CancellationToken cancellationToken)
        => Call<IReadOnlyList<string>>("GetPermissionsAsync", () => new[] { "read_diagnostics", "read_value", "write_value", "change_operating_mode" });

    public Task<string> ReadOperatingModeAsync(CancellationToken cancellationToken) => Call("ReadOperatingModeAsync", () => Plc.OperatingMode);

    public Task<string> ReadModeSelectorStateAsync(CancellationToken cancellationToken) => Call("ReadModeSelectorStateAsync", () => Plc.ModeSelector);

    public Task<PlcWebApiCpuTypeInfo> ReadCpuTypeAsync(CancellationToken cancellationToken)
        => Call("ReadCpuTypeAsync", () => new PlcWebApiCpuTypeInfo("CPU 1516-3 PN/DP", "6ES7 516-3AP03-0AB0"));

    public Task<PlcWebApiRuntimeInfo> ReadRuntimeInformationAsync(CancellationToken cancellationToken)
        => Call("ReadRuntimeInformationAsync", () => new PlcWebApiRuntimeInfo(12, 3, 7, 50, 0.5, 1.2, 4.8, 1, 150));

    public Task<DateTime> ReadSystemTimeAsync(CancellationToken cancellationToken) => Call("ReadSystemTimeAsync", () => Plc.SystemTime);

    public Task<PlcWebApiProjectInfo> ReadProjectInformationAsync(CancellationToken cancellationToken)
        => Call("ReadProjectInformationAsync", () => new PlcWebApiProjectInfo("Line", new[] { new PlcWebApiProjectVersionInfo("project", "V1.0") }));

    public Task<IReadOnlyList<PlcWebApiVariableInfo>> BrowseVariablesAsync(string mode, string? variable, CancellationToken cancellationToken)
        => Call<IReadOnlyList<PlcWebApiVariableInfo>>("BrowseVariablesAsync", () => new[]
        {
            new PlcWebApiVariableInfo("DB_Line", "data_block", true, 1, null, null, false, null, Array.Empty<PlcWebApiArrayDimensionInfo>()),
        }, $"{mode}|{variable}");

    public Task<IReadOnlyList<PlcWebApiCodeBlockInfo>> BrowseCodeBlocksAsync(CancellationToken cancellationToken)
        => Call<IReadOnlyList<PlcWebApiCodeBlockInfo>>("BrowseCodeBlocksAsync", () => new[] { new PlcWebApiCodeBlockInfo("Main", 1, "ob") });

    public Task<JsonElement> ReadVariableAsync(string variable, string? representation, CancellationToken cancellationToken)
        => Call("ReadVariableAsync", () => Plc.Tags.TryGetValue(variable, out var json)
            ? JsonDocument.Parse(json).RootElement.Clone()
            : throw new PlcWebApiException(WorkerFailureCategories.WebApiRejected, $"Web API error 200 (AddresDoesNotExist) for {variable}.",
                new PlcWebApiErrorInfo(200, "AddresDoesNotExist", "Address does not exist", null)), variable);

    public Task<bool> WriteVariableAsync(string variable, JsonElement value, string? representation, CancellationToken cancellationToken)
        => Call("WriteVariableAsync", () =>
        {
            if (!Plc.Tags.ContainsKey(variable))
                throw new PlcWebApiException(WorkerFailureCategories.WebApiRejected, $"Web API error 200 (AddresDoesNotExist) for {variable}.",
                    new PlcWebApiErrorInfo(200, "AddresDoesNotExist", "Address does not exist", null));
            Plc.Tags[variable] = value.GetRawText();
            return true;
        }, $"{variable}={value.GetRawText()}");

    public Task<PlcWebApiDiagnosticBufferInfo> BrowseDiagnosticBufferAsync(string language, int? count, CancellationToken cancellationToken)
        => Call("BrowseDiagnosticBufferAsync", () => new PlcWebApiDiagnosticBufferInfo(language, "2026-10-31T12:00:00.0000000Z", 1, 3200, new[]
        {
            new PlcWebApiDiagnosticEntryInfo("2026-10-31T11:59:00.0000000Z", "incoming", "Mode transition", "STOP to RUN", null, 1, 2),
        }), $"{language}|{count}");

    public Task<PlcWebApiAlarmsInfo> BrowseAlarmsAsync(string language, int? count, string? alarmId, CancellationToken cancellationToken)
        => Call("BrowseAlarmsAsync", () => new PlcWebApiAlarmsInfo(language, null, 1, 1000, new[]
        {
            new PlcWebApiAlarmEntryInfo("123456", 7, "incoming", "2026-10-31T11:00:00.0000000Z", "alarming", 64, "not_acknowledged", null, "Motor overload", null, false),
        }), $"{language}|{count}|{alarmId}");

    public Task<bool> AcknowledgeAlarmAsync(string alarmId, CancellationToken cancellationToken)
        => Call("AcknowledgeAlarmAsync", () => { Plc.AcknowledgedAlarms.Add(alarmId); return true; }, alarmId);

    public Task<IReadOnlyList<PlcWebApiModuleInfo>> BrowseModulesAsync(uint? hwid, string? mode, CancellationToken cancellationToken)
        => Call<IReadOnlyList<PlcWebApiModuleInfo>>("BrowseModulesAsync", () => new[]
        {
            new PlcWebApiModuleInfo(64, "PLC_1", "plc", "module", "central_module", new[] { "led_flashing" }, false, Array.Empty<long>(), Array.Empty<PlcWebApiModuleInfo>()),
        }, $"{hwid}|{mode}");

    public Task<PlcWebApiModuleStatusInfo> ReadModuleStatusAsync(uint hwid, string? language, CancellationToken cancellationToken)
        => Call("ReadModuleStatusAsync", () => new PlcWebApiModuleStatusInfo("good", "good", Array.Empty<string>(), language), $"{hwid}|{language}");

    public Task<IReadOnlyList<PlcWebApiLedInfo>> ReadModuleLedsAsync(uint hwid, CancellationToken cancellationToken)
        => Call<IReadOnlyList<PlcWebApiLedInfo>>("ReadModuleLedsAsync", () => new[] { new PlcWebApiLedInfo("run_stop", "on", new[] { "green" }, null) }, $"{hwid}");

    public Task<bool> FlashLedsAsync(uint hwid, CancellationToken cancellationToken)
        => Call("FlashLedsAsync", () => { Plc.FlashedModules.Add(hwid); return true; }, $"{hwid}");

    public Task<PlcWebApiSyslogInfo> BrowseSyslogAsync(int? count, int? first, CancellationToken cancellationToken)
        => Call("BrowseSyslogAsync", () => new PlcWebApiSyslogInfo(2, 0, new[] { "<14>1 2026-10-31T12:00:00Z PLC_1 - - - started" }), $"{count}|{first}");

    public Task<bool> RequestChangeOperatingModeAsync(string mode, CancellationToken cancellationToken)
        => Call("RequestChangeOperatingModeAsync", () => { Plc.OperatingMode = mode; return true; }, mode);

    public Task<bool> SetSystemTimeAsync(DateTime utcTimestamp, CancellationToken cancellationToken)
        => Call("SetSystemTimeAsync", () => { Plc.SystemTime = utcTimestamp; return true; }, utcTimestamp.ToString("O"));

    public Task<string> RequestBackupAsync(CancellationToken cancellationToken) => Call("RequestBackupAsync", () => "ticket-1");

    public Task<byte[]> DownloadBackupAsync(string ticket, CancellationToken cancellationToken)
        => Call("DownloadBackupAsync", () => ticket == "ticket-1" ? Plc.Backup : throw new InvalidOperationException("unknown ticket"), ticket);

    public Task RestoreBackupAsync(string filePath, string userName, string password, TimeSpan timeout, CancellationToken cancellationToken)
        => Call("RestoreBackupAsync", () =>
        {
            if (password != FakePlcWebApiWorld.Password) throw new InvalidOperationException("restore received the wrong password");
            Plc.Restores.Add((filePath, userName));
            Plc.OperatingMode = "run";
            return true;
        }, filePath);

    public void Dispose() => Disposed = true;

    private async Task<T> Call<T>(string method, Func<T> body, string? detail = null)
    {
        Record(method, detail);
        if (Plc.Gate is { } gate) await gate.Task.ConfigureAwait(false);
        if (!_target.IsAnonymous && (!_session || Plc.SessionExpired))
            throw new PlcWebApiException(WorkerFailureCategories.WebApiPermissionDenied, "Web API error 2 (PermissionDenied).",
                new PlcWebApiErrorInfo(2, "PermissionDenied", "Permission denied", null));
        Fail(method);
        return body();
    }

    private void Fail(string method)
    {
        if (Plc.Failures.TryRemove(method, out var failure)) throw failure();
    }

    private void Record(string method, string? detail = null)
        => _world.Calls.Enqueue($"{_target.Name}:{method}{(detail is null ? string.Empty : "(" + detail + ")")}");
}
