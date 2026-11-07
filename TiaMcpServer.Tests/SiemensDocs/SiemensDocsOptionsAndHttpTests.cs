using System.Net;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.SiemensDocs;
using Xunit;

namespace TiaMcpServer.Tests.SiemensDocs;

public sealed class SiemensDocsOptionsTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values)
        => name => values.FirstOrDefault(value => value.Name == name).Value;

    [Fact]
    public void Defaults_AreTheTwoSiemensHosts_EnUs_And300Ms()
    {
        var (options, error) = SiemensDocsOptions.Resolve(Array.Empty<string>(), Env());
        Assert.Null(error);
        Assert.Equal(new[] { SiemensDocsOptions.TiaHost, SiemensDocsOptions.IoxHost }, options!.Hosts);
        Assert.Equal("en-US", options.Locale);
        Assert.Equal(TimeSpan.FromMilliseconds(300), options.MinInterval);
        Assert.Equal(SiemensDocsOptions.TiaHost, options.DefaultHost);
    }

    [Fact]
    public void CommandLine_WinsOverEnvironment_AndTheListReplacesTheDefaults()
    {
        var (options, error) = SiemensDocsOptions.Resolve(new[] { "--docs-hosts", "Docs.TIA.siemens.cloud", "--docs-locale=de-de", "--docs-min-interval-ms", "1000" },
            Env(("TIA_MCP_DOCS_HOSTS", "docs.industrial-operations-x.siemens.cloud"), ("TIA_MCP_DOCS_LOCALE", "fr-FR")));
        Assert.Null(error);
        Assert.Equal(new[] { SiemensDocsOptions.TiaHost }, options!.Hosts);
        Assert.Equal("de-DE", options.Locale);
        Assert.Equal(TimeSpan.FromSeconds(1), options.MinInterval);
        Assert.False(options.IsAllowed(SiemensDocsOptions.IoxHost));
    }

    [Theory]
    [InlineData("https://docs.tia.siemens.cloud")]
    [InlineData("docs.tia.siemens.cloud:8443")]
    [InlineData("docs.tia.siemens.cloud/r")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("localhost")]
    [InlineData("intranet")]
    [InlineData("user@docs.tia.siemens.cloud")]
    [InlineData(" , ")]
    public void Hosts_ThatAreNotPlainDnsNames_RefuseStartup(string value)
    {
        var (options, error) = SiemensDocsOptions.Resolve(new[] { "--docs-hosts", value }, Env());
        Assert.Null(options);
        Assert.Contains("--docs-hosts", error);
    }

    [Theory]
    [InlineData("--docs-min-interval-ms", "-1")]
    [InlineData("--docs-min-interval-ms", "60001")]
    [InlineData("--docs-min-interval-ms", "0.5")]
    [InlineData("--docs-locale", "english")]
    public void InvalidValues_RefuseStartup(string flag, string value)
    {
        var (options, error) = SiemensDocsOptions.Resolve(new[] { flag, value }, Env());
        Assert.Null(options);
        Assert.Contains(flag, error);
    }

    [Fact]
    public void InvalidEnvironmentValue_NamesTheVariable()
    {
        var (_, error) = SiemensDocsOptions.Resolve(Array.Empty<string>(), Env(("TIA_MCP_DOCS_HOSTS", "http://x")));
        Assert.Contains("TIA_MCP_DOCS_HOSTS", error);
    }

    [Theory]
    [InlineData("en-us", "en-US")]
    [InlineData("DE-de", "de-DE")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("en", null)]
    [InlineData("en_US", null)]
    [InlineData("en-US-x", null)]
    public void Locale_IsNormalizedToTheHostsSpelling(string value, string? expected)
        => Assert.Equal(expected, SiemensDocsOptions.NormalizeLocale(value));
}

public sealed class SiemensDocsHttpTests
{
    private static readonly SiemensDocsOptions NoInterval = new(new[] { SiemensDocsFixtures.Tia, SiemensDocsFixtures.Iox }, "en-US", TimeSpan.Zero);

    private static (SiemensDocsHttp Http, SiemensDocsFixtures.FixtureHandler Handler, List<TimeSpan> Delays) Create(SiemensDocsOptions? options = null,
        TimeProvider? time = null)
    {
        var handler = SiemensDocsFixtures.Standard();
        var delays = new List<TimeSpan>();
        return (new SiemensDocsHttp(options ?? NoInterval, handler, time, (wait, _) =>
        {
            delays.Add(wait);
            return Task.CompletedTask;
        }), handler, delays);
    }

    [Fact]
    public async Task FirstCall_OpensTheWebSessionOnce_ThenCallsTheApi()
    {
        var (http, handler, _) = Create();
        await http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}", default);
        await http.GetJsonAsync<RawPages>(SiemensDocsFixtures.Tia, $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}/pages", default);
        Assert.Equal(new[] { SiemensDocsHttp.SessionPath, $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}", $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}/pages" },
            handler.List.Select(request => request.PathAndQuery));
        Assert.All(handler.List, request => Assert.Equal(SiemensDocsFixtures.Tia, request.Host));
    }

    [Fact]
    public async Task FailedSessionRequest_IsNotFatal_TheApiCallDecides()
    {
        // Live 2026-11-03: the public endpoints answered without the session cookie.
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, SiemensDocsHttp.SessionPath,
            _ => SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.InternalServerError, "{}", "application/json"));
        var map = await http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}", default);
        Assert.Equal(SiemensDocsFixtures.OpennessV21, map.Id);
    }

    [Fact]
    public async Task Unauthorized_OpensANewSession_AndRepeatsOnce()
    {
        var (http, handler, _) = Create();
        var calls = 0;
        handler.Route("GET", SiemensDocsFixtures.Tia, "/api/khub/maps/x", _ => ++calls == 1
            ? SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.Unauthorized, "{}", "application/json")
            : SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.OK, """{"id":"x"}""", "application/json"));
        var map = await http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", default);
        Assert.Equal("x", map.Id);
        Assert.Equal(new[] { SiemensDocsHttp.SessionPath, "/api/khub/maps/x", SiemensDocsHttp.SessionPath, "/api/khub/maps/x" },
            handler.List.Select(request => request.PathAndQuery));
    }

    [Fact]
    public async Task TooManyRequests_WaitsRetryAfterCappedAt10s_AndRepeatsOnce()
    {
        var (http, handler, delays) = Create();
        var calls = 0;
        handler.Route("GET", SiemensDocsFixtures.Tia, "/api/khub/maps/x", _ =>
        {
            if (++calls > 1) return SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.OK, """{"id":"x"}""", "application/json");
            var busy = SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.TooManyRequests, "{}", "application/json");
            busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return busy;
        });
        await http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", default);
        Assert.Equal(new[] { TimeSpan.FromSeconds(10) }, delays);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ServerErrorTwice_IsDocsRejected_WithTheStatus()
    {
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, "/api/khub/maps/x",
            _ => SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.BadGateway, "<html>secret</html>", "text/html"));
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", default));
        Assert.Equal(WorkerFailureCategories.DocsRejected, failure.Category);
        Assert.Equal(502, failure.Status);
        Assert.Contains("HTTP 502", failure.Message);
        Assert.DoesNotContain("secret", failure.Message);
    }

    [Fact]
    public async Task NotFound_IsTargetNotFound()
    {
        var (http, _, _) = Create();
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/doesNotExist123", default));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, failure.Category);
    }

    [Fact]
    public async Task Redirect_IsNotFollowed()
    {
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, "/api/khub/maps/x", _ =>
        {
            var redirect = SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.Found, string.Empty, "text/plain");
            redirect.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return redirect;
        });
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", default));
        Assert.Equal(WorkerFailureCategories.DocsRejected, failure.Category);
        Assert.Contains("redirects are not followed", failure.Message);
        Assert.DoesNotContain(handler.List, request => request.Host != SiemensDocsFixtures.Tia);
    }

    [Fact]
    public void ProductionHandler_DoesNotFollowRedirects()
        => Assert.False(((SocketsHttpHandler)SiemensDocsHttp.CreateHandler()).AllowAutoRedirect);

    [Fact]
    public async Task HostOutsideTheAllowlist_IsRefusedBeforeAnyRequest()
    {
        var (http, handler, _) = Create();
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<RawMap>("example.com", "/api/khub/maps", default));
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
        Assert.Empty(handler.List);
    }

    [Fact]
    public async Task ResponseOverTheSizeCap_IsDocsResponseInvalid()
    {
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, "/big", _ => SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.OK, new string('x', 5000), "text/html"));
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetTextAsync(SiemensDocsFixtures.Tia, "/big", default, maxBytes: 1024));
        Assert.Equal(WorkerFailureCategories.DocsResponseInvalid, failure.Category);
        Assert.Contains("size cap", failure.Message);
    }

    [Fact]
    public async Task ResponseOverTheSizeCap_WithoutContentLength_IsStoppedWhileReading()
    {
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, "/big", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnknownLengthStream(new byte[5000])),
        });
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/big", default, maxBytes: 1024));
        Assert.Equal(WorkerFailureCategories.DocsResponseInvalid, failure.Category);
    }

    [Fact]
    public async Task WrongJsonShape_IsDocsResponseInvalid_WithoutEchoingTheBody()
    {
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, "/api/khub/maps", _ => SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.OK, """{"payload":"secret"}""", "application/json"));
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<List<RawMap?>>(SiemensDocsFixtures.Tia, "/api/khub/maps", default));
        Assert.Equal(WorkerFailureCategories.DocsResponseInvalid, failure.Category);
        Assert.DoesNotContain("secret", failure.Message);
    }

    [Fact]
    public async Task NetworkFailure_IsDocsUnreachable()
    {
        var (http, handler, _) = Create();
        handler.Route("GET", SiemensDocsFixtures.Tia, "/api/khub/maps/x", _ => throw new HttpRequestException("No such host is known."));
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() => http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", default));
        Assert.Equal(WorkerFailureCategories.DocsUnreachable, failure.Category);
    }

    [Fact]
    public async Task Timeout_IsDocsUnreachable_CallerCancellationStaysCancellation()
    {
        var handler = new HangingHandler();
        using var http = new SiemensDocsHttp(NoInterval, handler);
        var failure = await Assert.ThrowsAsync<SiemensDocsException>(() =>
            http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", default, timeout: TimeSpan.FromMilliseconds(50)));
        Assert.Equal(WorkerFailureCategories.DocsUnreachable, failure.Category);
        Assert.Contains("timed out", failure.Message);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SiemensDocsHttp(NoInterval, new HangingHandler()).GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, "/api/khub/maps/x", cancel.Token));
    }

    [Fact]
    public async Task RequestsToOneHost_AreSpacedByTheMinimumInterval()
    {
        var time = new ManualTime();
        var options = new SiemensDocsOptions(new[] { SiemensDocsFixtures.Tia, SiemensDocsFixtures.Iox }, "en-US", TimeSpan.FromMilliseconds(300));
        var (http, _, delays) = Create(options, time);
        await http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}", default); // session + map
        time.Advance(TimeSpan.FromMilliseconds(100));
        await http.GetJsonAsync<RawMap>(SiemensDocsFixtures.Tia, $"/api/khub/maps/{SiemensDocsFixtures.OpennessV21}", default);
        // The map request right after the session request waits the whole interval, the next one the remaining 200 ms.
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(200) }, delays);
        await http.GetJsonAsync<RawPages>(SiemensDocsFixtures.Iox, $"/api/khub/maps/{SiemensDocsFixtures.IoxHsc}/pages", default);
        Assert.Equal(3, delays.Count); // another host has its own spacing: only its own session → pages gap
    }

    [Fact]
    public async Task Requests_IdentifyTheServer_AndPostJson()
    {
        var (http, handler, _) = Create();
        await http.PostJsonAsync<RawSearch>(SiemensDocsFixtures.Tia, "/api/khub/clustered-search", new { query = "x" }, default);
        var post = handler.List.Last();
        Assert.Equal("POST", post.Method);
        Assert.Equal("""{"query":"x"}""", post.Body);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == SiemensDocsHttp.SessionPath)
                return SiemensDocsFixtures.FixtureHandler.Response(HttpStatusCode.OK, SiemensDocsFixtures.Text("session.json"), "application/json");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
