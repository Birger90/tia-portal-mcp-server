using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcWebApi;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

/// <summary>
/// The production adapter over the official Siemens client library, exercised offline: a scripted
/// HttpMessageHandler answers the JSON-RPC requests the real ApiHttpClientRequestHandler sends, so
/// request shapes, response parsing and the exception mapping are the library's own.
/// </summary>
public sealed class SiemensPlcWebApiClientTests
{
    private static readonly PlcWebApiTarget Target = new("Line1", "192.0.2.10", "U", "P", PlcWebApiCertificateValidation.System, null, TimeSpan.FromSeconds(7));

    private sealed class ScriptedPlc : HttpMessageHandler
    {
        public List<(string Path, string? Method, JsonElement? Body, string? Token)> Requests { get; } = new();

        public Func<string, JsonElement, object?> Results { get; set; } = (_, _) => true;

        public Func<string, JsonElement, (int Code, string Message)?> Errors { get; set; } = (_, _) => null;

        public Func<HttpRequestMessage, Exception?> Throw { get; set; } = _ => null;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public byte[] TicketContent { get; set; } = Encoding.ASCII.GetBytes("BACKUP-BYTES");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.TryGetValues("X-Auth-Token", out var values) ? values.Single() : null;
            if (Throw(request) is { } exception) throw exception;
            if (request.RequestUri!.AbsolutePath == "/api/ticket")
            {
                Requests.Add((request.RequestUri.PathAndQuery, null, null, token));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TicketContent) };
            }

            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            var method = body.GetProperty("method").GetString()!;
            Requests.Add((request.RequestUri.AbsolutePath, method, body, token));
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status) { ReasonPhrase = "scripted" };
            var id = body.GetProperty("id").GetString();
            var parameters = body.TryGetProperty("params", out var p) ? p : default;
            var response = Errors(method, parameters) is { } error
                ? new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new { code = error.Code, message = error.Message } }
                : new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = Results(method, parameters) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }

    private static (SiemensPlcWebApiClient Client, ScriptedPlc Plc) Create(PlcWebApiTarget? target = null)
    {
        var plc = new ScriptedPlc();
        return ((SiemensPlcWebApiClient)new SiemensPlcWebApiClientFactory(_ => plc).Create(target ?? Target), plc);
    }

    [Fact]
    public async Task Reads_SendTheWebApiMethods_AndParseTheirResults()
    {
        var (client, plc) = Create();
        plc.Results = (method, _) => method switch
        {
            "Api.Ping" => "B6D8A9F3",
            "Api.Version" => 4.1,
            "Api.GetPermissions" => new[] { new { name = "read_diagnostics" }, new { name = "change_operating_mode" } },
            "Plc.ReadOperatingMode" => "stop_fwupdate",
            "Plc.ReadModeSelectorState" => new { mode_selector = "no_switch" },
            "Plc.ReadCpuType" => new { product_name = "CPU 1516-3 PN/DP", order_number = "6ES7 516-3AP03-0AB0" },
            "Plc.ReadSystemTime" => new { timestamp = "2026-10-31T12:34:56.789Z" },
            "Project.ReadInformation" => new { project_name = "Line", versions = Array.Empty<object>() },
            "Syslog.Browse" => new { entries = new[] { new { raw = "<14>1 boot" } }, count_total = 9, count_lost = 1 },
            _ => throw new InvalidOperationException(method),
        };

        Assert.Equal("B6D8A9F3", await client.PingAsync(default));
        Assert.Equal(4.1, await client.VersionAsync(default));
        Assert.Equal(new[] { "read_diagnostics", "change_operating_mode" }, await client.GetPermissionsAsync(default));
        Assert.Equal("stop_fwupdate", await client.ReadOperatingModeAsync(default));
        Assert.Equal("no_switch", await client.ReadModeSelectorStateAsync(default));
        Assert.Equal(new PlcWebApiCpuTypeInfo("CPU 1516-3 PN/DP", "6ES7 516-3AP03-0AB0"), await client.ReadCpuTypeAsync(default));
        Assert.Equal("2026-10-31T12:34:56.7890000Z", PlcWebApiFormat.Timestamp(await client.ReadSystemTimeAsync(default)));
        Assert.Equal("Line", (await client.ReadProjectInformationAsync(default)).ProjectName);
        var syslog = await client.BrowseSyslogAsync(5, 2, default);
        Assert.Equal(new[] { "<14>1 boot" }, syslog.Entries);
        Assert.Equal(9, syslog.CountTotal);

        Assert.All(plc.Requests, request => Assert.Equal("/api/jsonrpc", request.Path));
        Assert.Equal(new[] { "Api.Ping", "Api.Version", "Api.GetPermissions", "Plc.ReadOperatingMode", "Plc.ReadModeSelectorState", "Plc.ReadCpuType",
            "Plc.ReadSystemTime", "Project.ReadInformation", "Syslog.Browse" }, plc.Requests.Select(request => request.Method));
        Assert.All(plc.Requests, request => Assert.Equal("2.0", request.Body!.Value.GetProperty("jsonrpc").GetString()));
        var syslogParams = plc.Requests[^1].Body!.Value.GetProperty("params");
        Assert.Equal(5, syslogParams.GetProperty("count").GetInt32());
        Assert.Equal(2, syslogParams.GetProperty("first").GetInt32());
    }

    [Fact]
    public async Task Login_SetsTheSessionToken_LogoutDropsIt_AndThePasswordNeverLeaksIntoErrors()
    {
        var (client, plc) = Create();
        plc.Results = (method, _) => method switch { "Api.Login" => new { token = "TOKEN-123" }, "Api.Logout" => true, _ => "x" };

        await client.LoginAsync("operator", "pw-secret", default);
        await client.PingAsync(default);
        Assert.True(client.HasSession);
        await client.LogoutAsync(default);
        await client.PingAsync(default);

        Assert.Equal("Api.Login", plc.Requests[0].Method);
        Assert.Equal("operator", plc.Requests[0].Body!.Value.GetProperty("params").GetProperty("user").GetString());
        Assert.Null(plc.Requests[0].Token);
        Assert.Equal("TOKEN-123", plc.Requests[1].Token);
        Assert.Equal("Api.Logout", plc.Requests[2].Method);
        Assert.Null(plc.Requests[3].Token);
        Assert.False(client.HasSession);

        plc.Errors = (method, _) => method == "Api.Login" ? (100, "Login failed") : null;
        var failed = await Assert.ThrowsAsync<PlcWebApiException>(() => client.LoginAsync("operator", "pw-secret", default));
        Assert.Equal(WorkerFailureCategories.WebApiAuthenticationFailed, failed.Category);
        Assert.Equal(100, failed.Error!.Code);
        Assert.Equal("LoginFailed", failed.Error.Name);
        Assert.DoesNotContain("pw-secret", failed.Message);
        Assert.False(client.HasSession);
    }

    [Theory]
    [InlineData(2, "Permission denied", WorkerFailureCategories.WebApiPermissionDenied, "PermissionDenied")]
    [InlineData(1004, "PLC not in stop", WorkerFailureCategories.WebApiRejected, "PLCNotInStop")]
    [InlineData(200, "Address does not exist", WorkerFailureCategories.WebApiRejected, "AddresDoesNotExist")]
    [InlineData(-32601, "Method not found", WorkerFailureCategories.WebApiRejected, "MethodNotFound")]
    [InlineData(9999, "Future error", WorkerFailureCategories.WebApiRejected, null)]
    public async Task ErrorResponses_MapByCode_AndKeepThePlcMessage(int code, string message, string category, string? name)
    {
        var (client, plc) = Create();
        plc.Errors = (_, _) => (code, message);

        var exception = await Assert.ThrowsAsync<PlcWebApiException>(() => client.RequestChangeOperatingModeAsync("stop", default));

        Assert.Equal(category, exception.Category);
        Assert.Equal(code, exception.Error!.Code);
        Assert.Equal(name, exception.Error.Name);
        Assert.Equal(message, exception.Error.PlcMessage);
        Assert.Contains($"Web API error {code}", exception.Message);
        Assert.Contains(message, exception.Message);
        // The request text (here the requested mode) is never echoed through the inner ApiException.
        Assert.DoesNotContain("Plc.RequestChangeOperatingMode\"", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, WorkerFailureCategories.WebApiRejected)]
    [InlineData(HttpStatusCode.Forbidden, WorkerFailureCategories.WebApiPermissionDenied)]
    [InlineData(HttpStatusCode.Unauthorized, WorkerFailureCategories.WebApiPermissionDenied)]
    public async Task HttpStatusErrors_CarryTheStatus(HttpStatusCode status, string category)
    {
        var (client, plc) = Create();
        plc.Status = status;

        var exception = await Assert.ThrowsAsync<PlcWebApiException>(() => client.PingAsync(default));

        Assert.Equal(category, exception.Category);
        Assert.Equal((int)status, exception.Error!.HttpStatus);
        Assert.Contains($"HTTP {(int)status}", exception.Message);
    }

    [Fact]
    public async Task TransportFailures_AreUnreachable_AndTimeoutsNameTheConfiguredTimeout()
    {
        var (client, plc) = Create();
        plc.Throw = _ => new HttpRequestException("Connection refused (192.0.2.10:443)", new IOException("refused"), statusCode: null);
        var refused = await Assert.ThrowsAsync<PlcWebApiException>(() => client.PingAsync(default));
        Assert.Equal(WorkerFailureCategories.WebApiUnreachable, refused.Category);
        Assert.Contains("could not be reached", refused.Message);

        plc.Throw = _ => new HttpRequestException("The SSL connection could not be established", new AuthenticationException("remote certificate is invalid"));
        var tls = await Assert.ThrowsAsync<PlcWebApiException>(() => client.PingAsync(default));
        Assert.Equal(WorkerFailureCategories.WebApiUnreachable, tls.Category);
        Assert.Contains("server certificate was not accepted under certificate validation 'system'", tls.Message);

        plc.Throw = _ => new TaskCanceledException("timeout", new TimeoutException());
        var timeout = await Assert.ThrowsAsync<PlcWebApiException>(() => client.PingAsync(default));
        Assert.Equal(WorkerFailureCategories.WebApiUnreachable, timeout.Category);
        Assert.Contains("request timeout of 7 s", timeout.Message);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        plc.Throw = _ => null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(cancel.Token));
    }

    [Fact]
    public async Task ReadAndWriteVariables_KeepThePlcAndCallerJsonUnchanged()
    {
        var (client, plc) = Create();
        plc.Results = (method, parameters) => method == "PlcProgram.Read"
            ? parameters.GetProperty("var").GetString() switch
            {
                "\"DB\".When" => "2021-04-26T16:53:22Z",
                "\"DB\".Big" => JsonDocument.Parse("18446744073709551615").RootElement,
                _ => new { year = 2026 },
            }
            : true;

        Assert.Equal("2021-04-26T16:53:22Z", (await client.ReadVariableAsync("\"DB\".When", null, default)).GetString());
        Assert.Equal("18446744073709551615", (await client.ReadVariableAsync("\"DB\".Big", "raw", default)).GetRawText());
        Assert.Equal(2026, (await client.ReadVariableAsync("\"DB\".Dtl", null, default)).GetProperty("year").GetInt32());
        Assert.True(await client.WriteVariableAsync("\"DB\".Count", JsonDocument.Parse("2").RootElement, null, default));
        Assert.True(await client.WriteVariableAsync("\"DB\".Label", JsonDocument.Parse("\"2021-04-26T16:53:22Z\"").RootElement, "simple", default));

        Assert.Equal("raw", plc.Requests[1].Body!.Value.GetProperty("params").GetProperty("mode").GetString());
        Assert.False(plc.Requests[0].Body!.Value.GetProperty("params").TryGetProperty("mode", out _));
        var count = plc.Requests[3].Body!.Value.GetProperty("params");
        Assert.Equal("PlcProgram.Write", plc.Requests[3].Method);
        Assert.Equal("2", count.GetProperty("value").GetRawText());
        Assert.Equal("\"2021-04-26T16:53:22Z\"", plc.Requests[4].Body!.Value.GetProperty("params").GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task ControlCalls_SendTheExactParameters()
    {
        var (client, plc) = Create();
        plc.Results = (method, _) => method == "Plc.CreateBackup" ? new string('t', 28) : true;

        Assert.True(await client.RequestChangeOperatingModeAsync("stop", default));
        Assert.True(await client.SetSystemTimeAsync(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), default));
        Assert.True(await client.AcknowledgeAlarmAsync("8462536", default));
        Assert.True(await client.FlashLedsAsync(64, default));
        var ticket = await client.RequestBackupAsync(default);
        Assert.Equal("BACKUP-BYTES", Encoding.ASCII.GetString(await client.DownloadBackupAsync(ticket, default)));

        Assert.Equal("stop", plc.Requests[0].Body!.Value.GetProperty("params").GetProperty("mode").GetString());
        Assert.Equal("2026-10-31T12:00:00.000Z", plc.Requests[1].Body!.Value.GetProperty("params").GetProperty("timestamp").GetString());
        Assert.Equal("8462536", plc.Requests[2].Body!.Value.GetProperty("params").GetProperty("id").GetString());
        Assert.Equal(64, plc.Requests[3].Body!.Value.GetProperty("params").GetProperty("hwid").GetInt32());
        Assert.Equal(new[] { "Plc.RequestChangeOperatingMode", "Plc.SetSystemTime", "Alarms.Acknowledge", "Modules.FlashLeds", "Plc.CreateBackup", null, "Api.CloseTicket" },
            plc.Requests.Select(request => request.Method));
        Assert.Equal("/api/ticket?id=" + new string('t', 28), plc.Requests[5].Path);
        Assert.Equal(new string('t', 28), plc.Requests[6].Body!.Value.GetProperty("params").GetProperty("id").GetString());

        var invalid = await Assert.ThrowsAsync<PlcWebApiException>(() => client.RequestChangeOperatingModeAsync("hold", default));
        Assert.Equal(WorkerFailureCategories.ValidationError, invalid.Category);
    }

    [Fact]
    public async Task DiagnosticsAndAlarms_ParseTheLibraryModels()
    {
        var (client, plc) = Create();
        plc.Results = (method, parameters) => method switch
        {
            "DiagnosticBuffer.Browse" => new
            {
                language = parameters.GetProperty("language").GetString(), count_current = 1, count_max = 3200,
                entries = new[] { new { timestamp = "2026-10-31T11:59:00Z", status = "incoming", short_text = "Mode transition", long_text = "STOP to RUN", @event = new { textlist_id = 1, text_id = 2 } } },
            },
            "Alarms.Browse" => new
            {
                language = "en-US", count_current = 1, count_max = 1000,
                entries = new[] { new { id = "8462536", alarm_number = 7, timestamp = "2026-10-31T11:00:00Z", producer = "alarming", alarm_text = "Motor overload" } },
            },
            _ => throw new InvalidOperationException(method),
        };

        var buffer = await client.BrowseDiagnosticBufferAsync("de-DE", 10, default);
        var alarms = await client.BrowseAlarmsAsync("en-US", null, "8462536", default);

        Assert.Equal("de-DE", buffer.Language);
        Assert.Equal("Mode transition", Assert.Single(buffer.Entries).ShortText);
        Assert.Equal(1, buffer.Entries[0].TextlistId);
        Assert.Equal(10, plc.Requests[0].Body!.Value.GetProperty("params").GetProperty("count").GetInt32());
        Assert.Equal("Motor overload", Assert.Single(alarms.Entries).AlarmText);
        Assert.Equal("8462536", plc.Requests[1].Body!.Value.GetProperty("params").GetProperty("alarm_id").GetString());
    }

    [Fact]
    public void CertificatePolicy_IsPerTarget_AndPinsExactlyOneCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=plc-1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var pinned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var other = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
        var sha1 = Target with { CertificateValidation = PlcWebApiCertificateValidation.Thumbprint, Thumbprint = pinned.Thumbprint };
        var sha256 = Target with
        {
            CertificateValidation = PlcWebApiCertificateValidation.Thumbprint,
            Thumbprint = Convert.ToHexString(SHA256.HashData(pinned.RawData)),
        };
        var none = Target with { CertificateValidation = PlcWebApiCertificateValidation.None };

        Assert.Null(PlcWebApiCertificatePolicy.CreateValidator(Target));
        foreach (var target in new[] { sha1, sha256 })
        {
            var validator = PlcWebApiCertificatePolicy.CreateValidator(target)!;
            Assert.True(validator(this, pinned, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
            Assert.False(validator(this, other, null, SslPolicyErrors.None));
            Assert.False(validator(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        }

        Assert.True(PlcWebApiCertificatePolicy.CreateValidator(none)!(this, other, null, SslPolicyErrors.RemoteCertificateChainErrors));
        // Validation is never relaxed through the client library's process-wide callback.
        Assert.Null(Siemens.Simatic.S7.Webserver.API.Services.ServerCertificateCallback.CertificateCallback);
        var handler = Assert.IsType<SocketsHttpHandler>(SiemensPlcWebApiClientFactory.CreateHandler(sha1));
        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.NotNull(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Null(Assert.IsType<SocketsHttpHandler>(SiemensPlcWebApiClientFactory.CreateHandler(Target)).SslOptions.RemoteCertificateValidationCallback);
    }

    [Fact]
    public void ErrorMapping_ScrubsTheSecret_AndNeverEchoesTheGenericApiExceptionText()
    {
        var scrubbed = SiemensPlcWebApiErrors.Map(new InvalidOperationException("boom pw-secret"), Target, secret: "pw-secret");
        Assert.DoesNotContain("pw-secret", scrubbed.Message);
        Assert.Equal(WorkerFailureCategories.WebApiRejected, scrubbed.Category);

        var generic = new Siemens.Simatic.S7.Webserver.API.Exceptions.ApiException(
            new Siemens.Simatic.S7.Webserver.API.Models.Responses.ApiErrorModel(), "{\"method\":\"PlcProgram.Write\",\"params\":{\"value\":42}}");
        var mapped = SiemensPlcWebApiErrors.Map(generic, Target);
        Assert.DoesNotContain("PlcProgram.Write", mapped.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public async Task SetSystemTime_SendsTheGregorianUtcTimestamp_UnderAnyCurrentCulture(string name)
    {
        var (client, plc) = Create();
        var culture = new System.Globalization.CultureInfo(name);
        var previous = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
        try
        {
            Assert.True(await client.SetSystemTimeAsync(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), default));

            Assert.Equal("2026-10-31T12:00:00.000Z", Assert.Single(plc.Requests).Body!.Value.GetProperty("params").GetProperty("timestamp").GetString());
            Assert.Same(culture, System.Globalization.CultureInfo.CurrentCulture);
            Assert.Same(culture, System.Globalization.CultureInfo.CurrentUICulture);
        }
        finally
        {
            (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Fact]
    public void SetSystemTime_CultureTest_UsesCalendarsThatChangeTheYear()
    {
        // Guards the theory above: under these cultures the library's culture-dependent formatting would
        // not write 2026 (when the runtime has full globalization data).
        if (System.Globalization.CultureInfo.GetCultureInfo("th-TH").Calendar is not System.Globalization.ThaiBuddhistCalendar) return;
        Assert.Equal("2569", new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc).ToString("yyyy", System.Globalization.CultureInfo.GetCultureInfo("th-TH")));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData("Login")]
    [InlineData("pw-secret")]
    public async Task LoginError_MentioningThePassword_IsReplacedByAFixedTextWithTheCode(string password)
    {
        var (client, plc) = Create();
        plc.Errors = (method, _) => method == "Api.Login" ? (100, $"Login failed: password '{password}' is wrong") : null;

        var failed = await Assert.ThrowsAsync<PlcWebApiException>(() => client.LoginAsync("operator", password, default));

        Assert.Equal(WorkerFailureCategories.WebApiAuthenticationFailed, failed.Category);
        Assert.Equal("Api.Login on PLC Web API target 'Line1' failed with Web API error 100 (LoginFailed). "
            + "The details are withheld because they could reveal the credential.", failed.Message);
        Assert.Equal(new PlcWebApiErrorInfo(100, "LoginFailed", null, null), failed.Error);
        Assert.DoesNotContain("***", failed.Message);
    }

    [Fact]
    public async Task LoginError_NotMentioningThePassword_KeepsThePlcText()
    {
        var (client, plc) = Create();
        plc.Errors = (method, _) => method == "Api.Login" ? (100, "Login failed") : null;

        var failed = await Assert.ThrowsAsync<PlcWebApiException>(() => client.LoginAsync("operator", "zz-not-in-it", default));

        Assert.Contains("Web API error 100 (LoginFailed): Login failed", failed.Message);
        Assert.Equal("Login failed", failed.Error!.PlcMessage);
    }
}
