// Ported from Czarnak/siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki), siemens_docs_mcp/client.py;
// reviewed and rewritten in C#, endpoints re-checked against the live sites on 2026-11-03. See THIRD_PARTY_NOTICES.md.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.SiemensDocs;

/// <summary>A failed documentation request; <see cref="Category"/> is a <see cref="WorkerFailureCategories"/> value.</summary>
public sealed class SiemensDocsException : Exception
{
    public SiemensDocsException(string category, string message, int? status = null) : base(message)
    {
        Category = category;
        Status = status;
    }

    public string Category { get; }

    public int? Status { get; }
}

/// <summary>
/// HTTP access to the Fluid Topics REST API of the allowed Siemens documentation hosts. Every request
/// goes to <c>https://&lt;allowed host&gt;</c> plus a fixed API path built from validated identifiers:
/// the caller never supplies a URL that is fetched. Redirects are not followed (a redirect is
/// <c>docs_rejected</c>), responses are read up to a size cap, every request has a timeout, and
/// requests to one host are spaced by the configured minimum interval. Before the first API call to
/// a host the client opens a Fluid Topics web session (<c>GET /internal/api/webapp/authentication/session</c>)
/// whose cookies the handler keeps, as the reference implementation does; on 2026-11-03 the public
/// endpoints also answered without it, so a failed session request is not fatal. A 401/403 opens a
/// new session and repeats the request once; a 429 or 5xx waits (Retry-After, at most 10 s) and
/// repeats it once.
/// </summary>
public sealed class SiemensDocsHttp : IDisposable
{
    public const string SessionPath = "/internal/api/webapp/authentication/session";
    public const long DefaultMaxBytes = 8L * 1024 * 1024;

    /// <summary>The full publication list of docs.tia.siemens.cloud was 16.6 MB on 2026-11-03.</summary>
    public const long MapsMaxBytes = 64L * 1024 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MapsTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultRetryWait = TimeSpan.FromSeconds(2);

    private static readonly string UserAgent = "TiaMcpServer-siemens-docs/1.0 (+https://github.com/Birger90/tia-portal-mcp-server)";

    private readonly HttpClient _http;
    private readonly SiemensDocsOptions _options;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Dictionary<string, HostState> _hosts = new(StringComparer.Ordinal);

    public SiemensDocsHttp(SiemensDocsOptions options, HttpMessageHandler? handler = null, TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? TimeProvider.System;
        _delay = delay ?? ((wait, ct) => Task.Delay(wait, _time, ct));
        _http = new HttpClient(handler ?? CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var host in options.Hosts) _hosts[host] = new HostState();
    }

    /// <summary>The production handler: TLS validation by the system, no redirects, gzip/brotli, cookies kept per host.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = true,
        CookieContainer = new CookieContainer(),
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    public SiemensDocsOptions Options => _options;

    public Task<T> GetJsonAsync<T>(string host, string path, CancellationToken cancellationToken, long maxBytes = DefaultMaxBytes, TimeSpan? timeout = null)
        => SendAsync(host, () => Request(HttpMethod.Get, host, path, null), response => ReadJsonAsync<T>(response, host, path, maxBytes, cancellationToken),
            timeout ?? DefaultTimeout, cancellationToken);

    public Task<string> GetTextAsync(string host, string path, CancellationToken cancellationToken, long maxBytes = DefaultMaxBytes)
        => SendAsync(host, () => Request(HttpMethod.Get, host, path, null), response => ReadTextAsync(response, host, path, maxBytes, cancellationToken),
            DefaultTimeout, cancellationToken);

    public Task<T> PostJsonAsync<T>(string host, string path, object body, CancellationToken cancellationToken, long maxBytes = DefaultMaxBytes)
    {
        var json = JsonSerializer.Serialize(body, SiemensDocsJson.Options);
        return SendAsync(host, () => Request(HttpMethod.Post, host, path, json), response => ReadJsonAsync<T>(response, host, path, maxBytes, cancellationToken),
            DefaultTimeout, cancellationToken);
    }

    public void Dispose() => _http.Dispose();

    private HttpRequestMessage Request(HttpMethod method, string host, string path, string? json)
    {
        if (!_options.IsAllowed(host))
            throw new SiemensDocsException(WorkerFailureCategories.ValidationError, $"Host '{host}' is not allowed. Allowed hosts: {string.Join(", ", _options.Hosts)}.");
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException("Documentation API paths are absolute paths on the host.");
        var request = new HttpRequestMessage(method, new Uri($"https://{host}{path}", UriKind.Absolute));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<T> SendAsync<T>(string host, Func<HttpRequestMessage> build, Func<HttpResponseMessage, Task<T>> read, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var state = _hosts.TryGetValue(host, out var known) ? known
            : throw new SiemensDocsException(WorkerFailureCategories.ValidationError, $"Host '{host}' is not allowed. Allowed hosts: {string.Join(", ", _options.Hosts)}.");
        await EnsureSessionAsync(host, state, cancellationToken).ConfigureAwait(false);
        var response = await SendOnceAsync(host, state, build, timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                state.SessionOpen = false;
                await EnsureSessionAsync(host, state, cancellationToken).ConfigureAwait(false);
                response = await SendOnceAsync(host, state, build, timeout, cancellationToken).ConfigureAwait(false);
            }
            else if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                var wait = RetryWait(response);
                response.Dispose();
                await _delay(wait, cancellationToken).ConfigureAwait(false);
                response = await SendOnceAsync(host, state, build, timeout, cancellationToken).ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
                throw Rejected(host, build, response);
            return await read(response).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static SiemensDocsException Rejected(string host, Func<HttpRequestMessage> build, HttpResponseMessage response)
    {
        using var request = build();
        var status = (int)response.StatusCode;
        var what = status is >= 300 and < 400
            ? "redirected (redirects are not followed)"
            : $"answered HTTP {status} {response.ReasonPhrase}".TrimEnd();
        var category = response.StatusCode == HttpStatusCode.NotFound ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.DocsRejected;
        return new SiemensDocsException(category, $"{request.Method} https://{host}{request.RequestUri!.PathAndQuery} {what}.", status);
    }

    private async Task EnsureSessionAsync(string host, HostState state, CancellationToken cancellationToken)
    {
        if (state.SessionOpen) return;
        try
        {
            using var response = await SendOnceAsync(host, state, () => Request(HttpMethod.Get, host, SessionPath, null), DefaultTimeout, cancellationToken)
                .ConfigureAwait(false);
            // Live 2026-11-03: 200 {"sessionAuthenticated":false,...} with load-balancer cookies. Any answer counts as
            // tried; the API call that follows decides whether the host accepts the request.
        }
        catch (SiemensDocsException)
        {
            // Unreachable here means the API call below fails with the same honest category.
        }

        state.SessionOpen = true;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(string host, HostState state, Func<HttpRequestMessage> build, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await state.Turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = state.Last + _options.MinInterval - _time.GetUtcNow();
            if (state.Last != DateTimeOffset.MinValue && wait > TimeSpan.Zero) await _delay(wait, cancellationToken).ConfigureAwait(false);
            state.Last = _time.GetUtcNow();
        }
        finally
        {
            state.Turn.Release();
        }

        using var request = build();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SiemensDocsException(WorkerFailureCategories.DocsUnreachable,
                $"{request.Method} https://{host}{request.RequestUri!.PathAndQuery} timed out after {(int)timeout.TotalSeconds} s.");
        }
        catch (HttpRequestException exception)
        {
            throw new SiemensDocsException(WorkerFailureCategories.DocsUnreachable,
                $"Could not reach https://{host}: {exception.Message}");
        }
    }

    private static TimeSpan RetryWait(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        var wait = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultRetryWait);
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        return wait > MaxRetryWait ? MaxRetryWait : wait;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string host, string path, long maxBytes, CancellationToken ct)
    {
        await using var stream = await CappedStreamAsync(response, host, path, maxBytes, ct).ConfigureAwait(false);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, SiemensDocsJson.Options, ct).ConfigureAwait(false)
                   ?? throw Invalid(host, path, "the response was JSON null");
        }
        catch (JsonException exception)
        {
            throw Invalid(host, path, $"the response is not the expected JSON ({exception.Message.Split('.')[0]})");
        }
        catch (SizeCapExceededException)
        {
            throw TooLarge(host, path, maxBytes);
        }
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, string host, string path, long maxBytes, CancellationToken ct)
    {
        await using var stream = await CappedStreamAsync(response, host, path, maxBytes, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        try
        {
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }
        catch (SizeCapExceededException)
        {
            throw TooLarge(host, path, maxBytes);
        }

        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // Unknown charset: the hosts send UTF-8.
            }
        }

        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static async Task<Stream> CappedStreamAsync(HttpResponseMessage response, string host, string path, long maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } length && length > maxBytes) throw TooLarge(host, path, maxBytes);
        var inner = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new CappedReadStream(inner, maxBytes);
    }

    private static SiemensDocsException TooLarge(string host, string path, long maxBytes)
        => new(WorkerFailureCategories.DocsResponseInvalid, $"The response of https://{host}{path} exceeds the size cap of {maxBytes / (1024 * 1024)} MiB.");

    internal static SiemensDocsException Invalid(string host, string path, string what)
        => new(WorkerFailureCategories.DocsResponseInvalid, $"Unexpected answer from https://{host}{path}: {what}. The site may have changed.");

    private sealed class HostState
    {
        public SemaphoreSlim Turn { get; } = new(1, 1);

        public DateTimeOffset Last { get; set; } = DateTimeOffset.MinValue;

        public volatile bool SessionOpen;
    }

    private sealed class SizeCapExceededException : IOException
    {
    }

    /// <summary>Reads at most <c>max</c> bytes (after decompression), then throws.</summary>
    private sealed class CappedReadStream(Stream inner, long max) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            _read += read;
            if (_read > max) throw new SizeCapExceededException();
            return read;
        }
    }
}

internal static class SiemensDocsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
