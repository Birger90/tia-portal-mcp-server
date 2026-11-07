using System.Collections.Concurrent;
using System.Net;
using System.Text;
using TiaMcpServer.SiemensDocs;
using TiaMcpServer.Safety;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Tests.SiemensDocs;

/// <summary>
/// The recorded (trimmed) live responses of docs.tia.siemens.cloud and docs.industrial-operations-x.siemens.cloud
/// from 2026-11-03, embedded in the test assembly, and an <see cref="HttpMessageHandler"/> that serves them by exact
/// method, host and path+query. No test reaches the network.
/// <para>
/// Provenance (curl from the cloud container, 2026-11-03, all HTTP 200 unless noted): session.json =
/// GET /internal/api/webapp/authentication/session; maps-tia.json / maps-iox.json = GET /api/khub/maps (16.6 MB / 10.5 MB,
/// reduced to a few publications and the metadata keys the reader uses); map-openness-v21.json / map-iox-hsc.json =
/// GET /api/khub/maps/{id}; pages-openness-v21.json = GET /api/khub/maps/gpR5ZkKnLSuzVoGX1ovoKg/pages (branches pruned);
/// pages-iox-multiroot.json = GET /api/khub/maps/YnqSGjg18sxz1X1czYtfcA/pages (whole); content-*.html =
/// GET /api/khub/maps/{id}/topics/{cid}/content?target=DESIGNED_READER (object list cut after its first data table);
/// search-*.json = POST /api/khub/clustered-search (clusters, entries and metadata reduced); search-empty.json = the
/// answer to an unknown locale; error-404.json = GET /api/khub/maps/doesNotExist123 (HTTP 404).
/// </para>
/// </summary>
internal static class SiemensDocsFixtures
{
    public const string Tia = SiemensDocsOptions.TiaHost;
    public const string Iox = SiemensDocsOptions.IoxHost;
    public const string OpennessV21 = "gpR5ZkKnLSuzVoGX1ovoKg";
    public const string OpennessV21De = "bfAoag5ie5daeR1DWAbipQ";
    public const string Connecting = "DP7NDA58oox~t1~oZJkHQQ";
    public const string ConnectingTocId = "7g593pqgubN9AV3Dqy9FQQ";
    public const string ObjectList = "6KC67KZF3fLSGEA_9eMd1Q";
    public const string IoxHsc = "YnqSGjg18sxz1X1czYtfcA";
    public const string IoxHscOverview = "~_uoVimLbbt51ONhNyeIHw";
    public const string OpennessV21Pretty = "en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows";

    public static string Text(string name)
    {
        using var stream = typeof(SiemensDocsFixtures).Assembly.GetManifestResourceStream("TiaMcpServer.SiemensDocsFixtures." + name)
                           ?? throw new InvalidOperationException($"Missing embedded fixture {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A handler answering every endpoint the reader uses from the fixtures; unknown requests get a 404 like the hosts.</summary>
    public static FixtureHandler Standard()
    {
        var handler = new FixtureHandler();
        foreach (var host in new[] { Tia, Iox }) handler.Json("GET", host, SiemensDocsHttp.SessionPath, "session.json");
        handler.Json("GET", Tia, "/api/khub/maps", "maps-tia.json");
        handler.Json("GET", Iox, "/api/khub/maps", "maps-iox.json");
        handler.Json("GET", Tia, $"/api/khub/maps/{OpennessV21}", "map-openness-v21.json");
        handler.Json("GET", Iox, $"/api/khub/maps/{IoxHsc}", "map-iox-hsc.json");
        handler.Json("GET", Tia, $"/api/khub/maps/{OpennessV21}/pages", "pages-openness-v21.json");
        handler.Json("GET", Iox, $"/api/khub/maps/{IoxHsc}/pages", "pages-iox-multiroot.json");
        handler.Html("GET", Tia, $"/api/khub/maps/{OpennessV21}/topics/{Connecting}/content?target=DESIGNED_READER", "content-tia-connecting.html");
        handler.Html("GET", Tia, $"/api/khub/maps/{OpennessV21}/topics/{ObjectList}/content?target=DESIGNED_READER", "content-tia-object-list.html");
        handler.Html("GET", Iox, $"/api/khub/maps/{IoxHsc}/topics/{IoxHscOverview}/content?target=DESIGNED_READER", "content-iox-hsc-overview.html");
        handler.Json("POST", Tia, "/api/khub/clustered-search", "search-tia-exportasdocuments.json");
        handler.Json("POST", Iox, "/api/khub/clustered-search", "search-iox-axunit.json");
        return handler;
    }

    public static SiemensDocsService Service(FixtureHandler? handler = null, SiemensDocsOptions? options = null, McpAccessMode mode = McpAccessMode.ReadOnly)
        => new(options ?? SiemensDocsOptions.Default, new OperationAccessPolicy(mode), handler ?? Standard(), delay: (_, _) => Task.CompletedTask);

    internal sealed record Recorded(string Method, string Host, string PathAndQuery, string? Body);

    internal sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

        public ConcurrentQueue<Recorded> Requests { get; } = new();

        public IReadOnlyList<Recorded> List => Requests.ToArray();

        public void Json(string method, string host, string pathAndQuery, string fixture)
            => Route(method, host, pathAndQuery, _ => Response(HttpStatusCode.OK, Text(fixture), "application/json"));

        public void Html(string method, string host, string pathAndQuery, string fixture)
            => Route(method, host, pathAndQuery, _ => Response(HttpStatusCode.OK, Text(fixture), "text/html"));

        public void Route(string method, string host, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> answer)
            => _routes[Key(method, host, pathAndQuery)] = answer;

        public static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var uri = request.RequestUri!;
            Requests.Enqueue(new Recorded(request.Method.Method, uri.Host, uri.PathAndQuery, body));
            if (uri.Scheme != "https") throw new InvalidOperationException("The reader sent a non-https request.");
            return _routes.TryGetValue(Key(request.Method.Method, uri.Host, uri.PathAndQuery), out var answer)
                ? answer(request)
                : Response(HttpStatusCode.NotFound, Text("error-404.json"), "application/json");
        }

        private static string Key(string method, string host, string pathAndQuery) => method + " " + host + pathAndQuery;
    }
}
