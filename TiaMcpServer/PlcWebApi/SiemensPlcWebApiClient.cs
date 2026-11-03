using System.Collections;
using System.Globalization;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Siemens.Simatic.S7.Webserver.API.Enums;
using Siemens.Simatic.S7.Webserver.API.Exceptions;
using Siemens.Simatic.S7.Webserver.API.Models.Responses;
using Siemens.Simatic.S7.Webserver.API.Services.Backup;
using Siemens.Simatic.S7.Webserver.API.Services.IdGenerator;
using Siemens.Simatic.S7.Webserver.API.Services.RequestHandling;
using Siemens.Simatic.S7.Webserver.API.Services.Ticketing;
using TiaMcpServer.Contracts.Worker;
using SiemensModule = Siemens.Simatic.S7.Webserver.API.Models.Module;

namespace TiaMcpServer.PlcWebApi;

/// <summary>
/// Creates <see cref="SiemensPlcWebApiClient"/>s. Each target gets its own HTTP handler with its
/// own certificate policy; the client library's process-wide <c>ServerCertificateCallback</c> is
/// never touched, so validation is never relaxed globally. The handler factory is replaceable for
/// offline tests only.
/// </summary>
public sealed class SiemensPlcWebApiClientFactory : IPlcWebApiClientFactory
{
    private readonly Func<PlcWebApiTarget, HttpMessageHandler> _handlerFactory;

    public SiemensPlcWebApiClientFactory(Func<PlcWebApiTarget, HttpMessageHandler>? handlerFactory = null)
    {
        _handlerFactory = handlerFactory ?? CreateHandler;
    }

    public IPlcWebApiClient Create(PlcWebApiTarget target) => new SiemensPlcWebApiClient(target, _handlerFactory(target));

    /// <summary>
    /// Direct connection (no proxy: the PLC is addressed on the plant network and credentials must not
    /// pass a proxy), no redirects (the library's default), and the target's certificate validation.
    /// </summary>
    internal static HttpMessageHandler CreateHandler(PlcWebApiTarget target)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var validator = PlcWebApiCertificatePolicy.CreateValidator(target);
        if (validator is not null)
            handler.SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = validator };
        return handler;
    }
}

/// <summary>The per-target TLS server-certificate rule (see <see cref="PlcWebApiCertificateValidation"/>).</summary>
public static class PlcWebApiCertificatePolicy
{
    /// <summary>
    /// Null for <c>system</c> (the platform's normal validation); an exact fingerprint match for
    /// <c>thumbprint</c>; accept-all for <c>none</c>, scoped to this target's handler only.
    /// </summary>
    public static RemoteCertificateValidationCallback? CreateValidator(PlcWebApiTarget target) => target.CertificateValidation switch
    {
        PlcWebApiCertificateValidation.System => null,
        PlcWebApiCertificateValidation.Thumbprint => (_, certificate, _, _) => MatchesPin(certificate, target.Thumbprint),
        PlcWebApiCertificateValidation.None => (_, _, _, _) => true,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target.CertificateValidation, null),
    };

    public static bool MatchesPin(X509Certificate? certificate, string? pin)
    {
        if (certificate is null || pin is null) return false;
        var actual = pin.Length == 64
            ? certificate.GetCertHashString(HashAlgorithmName.SHA256)
            : certificate.GetCertHashString(HashAlgorithmName.SHA1);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual.ToUpperInvariant()), System.Text.Encoding.ASCII.GetBytes(pin));
    }
}

/// <summary>
/// <see cref="IPlcWebApiClient"/> over the official client (<c>ApiHttpClientRequestHandler</c>). The
/// HTTP client is created here, not by <c>ApiStandardServiceFactory</c>, so that the certificate
/// policy, timeout and proxy settings are per target. Every Siemens exception is translated by
/// <see cref="SiemensPlcWebApiErrors"/>; nothing the library throws reaches a caller unmapped.
/// </summary>
public sealed class SiemensPlcWebApiClient : IPlcWebApiClient
{
    private const string AuthTokenHeader = "X-Auth-Token";
    private static readonly SnakeCaseNamingStrategy SnakeCase = new();
    private static readonly TimeSpan RestoreRebootWait = TimeSpan.FromMinutes(3);

    private readonly PlcWebApiTarget _target;
    private readonly HttpClient _httpClient;
    private readonly ApiRequestFactory _requestFactory;
    private readonly ApiHttpClientRequestHandler _handler;
    private volatile bool _hasSession;

    public SiemensPlcWebApiClient(PlcWebApiTarget target, HttpMessageHandler httpHandler)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _httpClient = new HttpClient(httpHandler ?? throw new ArgumentNullException(nameof(httpHandler)), disposeHandler: true)
        {
            BaseAddress = new Uri("https://" + target.Address + "/"),
            Timeout = target.RequestTimeout,
        };
        _requestFactory = new ApiRequestFactory(new GUIDGenerator(), new ApiRequestParameterChecker());
        _handler = new ApiHttpClientRequestHandler(_httpClient, _requestFactory, new ErrorRecordingResponseChecker(),
            new ApiRequestSplitterByBytes());
    }

    public bool HasSession => _hasSession;

    public async Task LoginAsync(string userName, string password, CancellationToken cancellationToken)
    {
        DropSession();
        await Run(async () =>
        {
            await _handler.ApiLoginAsync(userName, password, includeWebApplicationCookie: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken, login: true, secret: password).ConfigureAwait(false);
        _hasSession = true;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Run(async () => (await _handler.ApiLogoutAsync(cancellationToken).ConfigureAwait(false)).Result, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DropSession();
        }
    }

    public void DropSession()
    {
        _hasSession = false;
        _httpClient.DefaultRequestHeaders.Remove(AuthTokenHeader);
    }

    public Task<string> PingAsync(CancellationToken cancellationToken)
        => Run(async () => (await _handler.ApiPingAsync(cancellationToken).ConfigureAwait(false)).Result, cancellationToken);

    public Task<double> VersionAsync(CancellationToken cancellationToken)
        => Run(async () => (await _handler.ApiVersionAsync(cancellationToken).ConfigureAwait(false)).Result, cancellationToken);

    public Task<IReadOnlyList<string>> GetPermissionsAsync(CancellationToken cancellationToken)
        => Run<IReadOnlyList<string>>(async () => ((await _handler.ApiGetPermissionsAsync(cancellationToken).ConfigureAwait(false)).Result
            ?? new List<Siemens.Simatic.S7.Webserver.API.Models.ApiClass>()).Select(permission => permission?.Name ?? string.Empty).ToArray(), cancellationToken);

    public Task<string> ReadOperatingModeAsync(CancellationToken cancellationToken)
        => Run(async () => EnumName((await _handler.PlcReadOperatingModeAsync(ApiPlcRedundancyId.StandardPLC, cancellationToken).ConfigureAwait(false)).Result)!,
            cancellationToken);

    public Task<string> ReadModeSelectorStateAsync(CancellationToken cancellationToken)
        => Run(async () => EnumName((await _handler.PlcReadModeSelectorStateAsync(ApiPlcRedundancyId.StandardPLC, cancellationToken).ConfigureAwait(false))
            .Result?.Mode_Selector) ?? "unknown", cancellationToken);

    public Task<PlcWebApiCpuTypeInfo> ReadCpuTypeAsync(CancellationToken cancellationToken)
        => Run(async () =>
        {
            var result = (await _handler.ApiGetPlcCpuTypeAsync(cancellationToken).ConfigureAwait(false)).Result;
            return new PlcWebApiCpuTypeInfo(result?.Product_Name, result?.Order_Number);
        }, cancellationToken);

    public Task<PlcWebApiRuntimeInfo> ReadRuntimeInformationAsync(CancellationToken cancellationToken)
        => Run(async () =>
        {
            var info = (await _handler.PlcReadRuntimeInformationAsync(cancellationToken).ConfigureAwait(false)).Result;
            var load = info?.Load;
            var cycle = info?.CycleTime;
            return new PlcWebApiRuntimeInfo(
                load?.Actual?.ProgramLoadCyclicProgramObsPercentage, load?.Actual?.ProgramLoadHighPriorityObsPercentage,
                load?.Actual?.CurrentCommunicationLoadPercentage, load?.Configured?.MaxCommunicationLoadPercentage,
                cycle?.Actual?.Shortest.TotalMilliseconds, cycle?.Actual?.Current.TotalMilliseconds, cycle?.Actual?.Longest.TotalMilliseconds,
                cycle?.Configured?.Min.TotalMilliseconds, cycle?.Configured?.Max.TotalMilliseconds);
        }, cancellationToken);

    public Task<DateTime> ReadSystemTimeAsync(CancellationToken cancellationToken)
        => Run(async () => (await _handler.PlcReadSystemTimeAsync(cancellationToken).ConfigureAwait(false)).Result.Timestamp, cancellationToken);

    public Task<PlcWebApiProjectInfo> ReadProjectInformationAsync(CancellationToken cancellationToken)
        => Run(async () =>
        {
            var info = (await _handler.ProjectReadInformationAsync(cancellationToken).ConfigureAwait(false)).Result;
            return new PlcWebApiProjectInfo(info?.ProjectName,
                (info?.Versions ?? new()).Select(version => new PlcWebApiProjectVersionInfo(EnumName(version?.Source), version?.Version)).ToArray());
        }, cancellationToken);

    public Task<IReadOnlyList<PlcWebApiVariableInfo>> BrowseVariablesAsync(string mode, string? variable, CancellationToken cancellationToken)
        => Run<IReadOnlyList<PlcWebApiVariableInfo>>(async () =>
        {
            var browseMode = mode == "var" ? ApiPlcProgramBrowseMode.Var : ApiPlcProgramBrowseMode.Children;
            var result = (await _handler.PlcProgramBrowseAsync(browseMode, variable, cancellationToken).ConfigureAwait(false)).Result;
            return (result ?? new()).Select(data => new PlcWebApiVariableInfo(
                data?.Name, EnumName(data?.Datatype), data?.Has_children, data?.Db_number, data?.Address,
                data?.Area is { } area ? area.ToString() : null, data?.Read_only, data?.Max_length,
                (data?.Array_dimensions ?? new()).Select(dimension => new PlcWebApiArrayDimensionInfo(dimension.Start_index, dimension.Count)).ToArray()))
                .ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<PlcWebApiCodeBlockInfo>> BrowseCodeBlocksAsync(CancellationToken cancellationToken)
        => Run<IReadOnlyList<PlcWebApiCodeBlockInfo>>(async () =>
            ((await _handler.PlcProgramBrowseCodeBlocksAsync(cancellationToken).ConfigureAwait(false)).Result ?? new())
            .Select(block => new PlcWebApiCodeBlockInfo(block?.Name, block?.BlockNumber ?? 0, EnumName(block?.BlockType))).ToArray(),
            cancellationToken);

    /// <summary>
    /// PlcProgram.Read through the client's request factory and <c>SendPostRequestAsync</c>, the same
    /// path <c>PlcProgramReadAsync&lt;T&gt;</c> takes, but the <c>result</c> member is returned as the
    /// PLC sent it instead of being converted by Newtonsoft (which would reinterpret date-like strings).
    /// </summary>
    public Task<JsonElement> ReadVariableAsync(string variable, string? representation, CancellationToken cancellationToken)
        => Run(async () =>
        {
            var request = _requestFactory.GetApiPlcProgramReadRequest(variable, Representation(representation));
            var response = await _handler.SendPostRequestAsync(request, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("result", out var result)
                ? result.Clone()
                : throw new PlcWebApiException(WorkerFailureCategories.WebApiRejected, "The PLC answered PlcProgram.Read without a result member.");
        }, cancellationToken);

    public Task<bool> WriteVariableAsync(string variable, JsonElement value, string? representation, CancellationToken cancellationToken)
        => Run(async () => (await _handler.PlcProgramWriteAsync(variable, ToToken(value), Representation(representation), cancellationToken)
            .ConfigureAwait(false)).Result, cancellationToken);

    public Task<PlcWebApiDiagnosticBufferInfo> BrowseDiagnosticBufferAsync(string language, int? count, CancellationToken cancellationToken)
        => Run(async () =>
        {
            var buffer = (await _handler.ApiDiagnosticBufferBrowseAsync(CultureInfo.GetCultureInfo(language), count is { } c ? (uint)c : null,
                filters: null, cancellationToken: cancellationToken).ConfigureAwait(false)).Result;
            return new PlcWebApiDiagnosticBufferInfo(buffer?.Language, Timestamp(buffer?.Last_Modified), buffer?.Count_Current ?? 0, buffer?.Count_Max ?? 0,
                (buffer?.Entries ?? new()).Select(entry => new PlcWebApiDiagnosticEntryInfo(Timestamp(entry.Timestamp)!, EnumName(entry.Status),
                    entry.Short_Text, entry.Long_Text, entry.Help_Text, entry.Event?.Textlist_Id, entry.Event?.Text_Id)).ToArray());
        }, cancellationToken);

    public Task<PlcWebApiAlarmsInfo> BrowseAlarmsAsync(string language, int? count, string? alarmId, CancellationToken cancellationToken)
        => Run(async () =>
        {
            var alarms = (await _handler.ApiAlarmsBrowseAsync(CultureInfo.GetCultureInfo(language), count, alarmId, filters: null,
                cancellationToken: cancellationToken).ConfigureAwait(false)).Result;
            return new PlcWebApiAlarmsInfo(alarms?.Language, Timestamp(alarms?.Last_Modified), alarms?.Count_Current ?? 0, alarms?.Count_Max ?? 0,
                (alarms?.Entries ?? new()).Select(entry => new PlcWebApiAlarmEntryInfo(entry.Id, entry.Alarm_Number, EnumName(entry.Status),
                    Timestamp(entry.Timestamp)!, entry.Producer, entry.Hwid, EnumName(entry.Acknowledgement?.State),
                    entry.Acknowledgement is null ? null : Timestamp(entry.Acknowledgement.Timestamp), entry.Alarm_Text, entry.Info_Text,
                    entry.Text_Inconsistent)).ToArray());
        }, cancellationToken);

    public Task<bool> AcknowledgeAlarmAsync(string alarmId, CancellationToken cancellationToken)
        => Run(async () => (await _handler.AlarmsAcknowledgeAsync(alarmId, cancellationToken).ConfigureAwait(false)).Result, cancellationToken);

    public Task<IReadOnlyList<PlcWebApiModuleInfo>> BrowseModulesAsync(uint? hwid, string? mode, CancellationToken cancellationToken)
        => Run<IReadOnlyList<PlcWebApiModuleInfo>>(async () =>
        {
            ModulesBrowseMode? browseMode = mode switch { "node" => ModulesBrowseMode.Node, "children" => ModulesBrowseMode.Children, _ => null };
            var result = (await _handler.ModulesBrowseAsync(hwid, browseMode, cancellationToken).ConfigureAwait(false)).Result;
            return (result?.Nodes ?? new()).Select(node => Module(node, 0)).ToArray();
        }, cancellationToken);

    public Task<PlcWebApiModuleStatusInfo> ReadModuleStatusAsync(uint hwid, string? language, CancellationToken cancellationToken)
        => Run(async () =>
        {
            var status = (await _handler.ModulesReadStatusAsync(hwid, language is null ? null : CultureInfo.GetCultureInfo(language), cancellationToken)
                .ConfigureAwait(false)).Result;
            return new PlcWebApiModuleStatusInfo(EnumName(status?.Status?.Own), EnumName(status?.Status?.Subordinate),
                (status?.Messages ?? new()).Select(message => message?.Text ?? string.Empty).ToArray(), status?.Language?.Name);
        }, cancellationToken);

    public Task<IReadOnlyList<PlcWebApiLedInfo>> ReadModuleLedsAsync(uint hwid, CancellationToken cancellationToken)
        => Run<IReadOnlyList<PlcWebApiLedInfo>>(async () =>
            ((await _handler.ModulesReadLedsAsync(hwid, cancellationToken).ConfigureAwait(false)).Result?.Leds ?? new())
            .Select(led => new PlcWebApiLedInfo(EnumName(led.Type), EnumName(led.Status), (led.Colors ?? new()).Select(color => EnumName(color)!).ToArray(),
                led.Period.TotalMilliseconds)).ToArray(), cancellationToken);

    public Task<bool> FlashLedsAsync(uint hwid, CancellationToken cancellationToken)
        => Run(async () => (await _handler.ModulesFlashLedsAsync(hwid, cancellationToken).ConfigureAwait(false)).Result, cancellationToken);

    public Task<PlcWebApiSyslogInfo> BrowseSyslogAsync(int? count, int? first, CancellationToken cancellationToken)
        => Run(async () =>
        {
            var syslog = (await _handler.ApiSyslogBrowseAsync(ApiPlcRedundancyId.StandardPLC, count is { } c ? (uint)c : null,
                first is { } f ? (uint)f : null, cancellationToken).ConfigureAwait(false)).Result;
            return new PlcWebApiSyslogInfo(syslog?.Count_Total ?? 0, syslog?.Count_Lost ?? 0,
                (syslog?.Entries ?? new()).Select(entry => entry?.Raw ?? string.Empty).ToArray());
        }, cancellationToken);

    public Task<bool> RequestChangeOperatingModeAsync(string mode, CancellationToken cancellationToken)
        => Run(async () => (await _handler.PlcRequestChangeOperatingModeAsync(
            mode == "stop" ? ApiPlcOperatingMode.Stop : mode == "run" ? ApiPlcOperatingMode.Run
                : throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only run and stop are supported."),
            ApiPlcRedundancyId.StandardPLC, cancellationToken).ConfigureAwait(false)).Result, cancellationToken);

    /// <summary>
    /// The client formats the timestamp with <c>DateTime.ToString(format)</c> in the current culture
    /// (<c>ApiRequestFactory.GetApiPlcSetSystemTimeRequest</c>), whose calendar can change the year (a Thai
    /// Buddhist or Hijri calendar). The call therefore runs under the invariant culture; the change is local
    /// to this asynchronous flow and restored afterwards.
    /// </summary>
    public Task<bool> SetSystemTimeAsync(DateTime utcTimestamp, CancellationToken cancellationToken)
        => Run(async () =>
        {
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                return (await _handler.PlcSetSystemTimeAsync(DateTime.SpecifyKind(utcTimestamp, DateTimeKind.Utc), cancellationToken)
                    .ConfigureAwait(false)).Result;
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;
            }
        }, cancellationToken);

    public Task<string> RequestBackupAsync(CancellationToken cancellationToken)
        => Run(async () =>
        {
            var ticket = (await _handler.PlcCreateBackupAsync(cancellationToken).ConfigureAwait(false)).Result;
            return string.IsNullOrEmpty(ticket)
                ? throw new PlcWebApiException(WorkerFailureCategories.WebApiRejected, "The PLC answered Plc.CreateBackup without a ticket.")
                : ticket;
        }, cancellationToken);

    public Task<byte[]> DownloadBackupAsync(string ticket, CancellationToken cancellationToken)
        => Run(async () =>
        {
            ArgumentException.ThrowIfNullOrEmpty(ticket);
            try
            {
                var bytes = await _handler.DownloadTicketAsync(ticket, cancellationToken).ConfigureAwait(false);
                return bytes.Length > 0
                    ? bytes
                    : throw new PlcWebApiException(WorkerFailureCategories.WebApiRejected, "The backup ticket download returned no content.");
            }
            finally
            {
                try
                {
                    using var close = new CancellationTokenSource(_target.RequestTimeout);
                    await _handler.ApiCloseTicketAsync(ticket, close.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Best effort: the PLC drops tickets of an ended session; the backup bytes are already here.
                }
            }
        }, cancellationToken);

    /// <summary>
    /// The client's <c>ApiBackupHandler.RestoreBackupAsync</c>: it decides whether the PLC must first be
    /// put into restore mode, uploads the file, waits for each restart (three minutes per wait, the
    /// client default) and logs in again with the same user. It contains blocking waits, so it runs on
    /// the thread pool, bounded by <paramref name="timeout"/>.
    /// </summary>
    public async Task RestoreBackupAsync(string filePath, string userName, string password, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        await Run(async () =>
        {
            var backup = new ApiBackupHandler(_handler, new ApiTicketHandler(_handler));
            try
            {
                await Task.Run(() => backup.RestoreBackupAsync(filePath, userName, password, RestoreRebootWait, bounded.Token), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && (exception is TimeoutException || (exception is OperationCanceledException && bounded.IsCancellationRequested)))
            {
                throw new PlcWebApiException(WorkerFailureCategories.WebApiUnreachable,
                    $"The backup restore on PLC Web API target '{_target.Name}' did not finish within "
                    + (exception is TimeoutException
                        ? $"the client's {RestoreRebootWait.TotalMinutes:0}-minute wait for the PLC to restart"
                        : $"{timeout.TotalMinutes:0} minutes")
                    + ". The PLC may still be restarting or restoring; check it with plc_online_read (ping, read_operating_mode) before doing anything else.");
            }

            return true;
        }, cancellationToken, secret: password).ConfigureAwait(false);
        _hasSession = true;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<T> Run<T>(Func<Task<T>> call, CancellationToken cancellationToken, bool login = false, string? secret = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (PlcWebApiException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw SiemensPlcWebApiErrors.Map(exception, _target, login, secret);
        }
    }

    private static ApiPlcDataRepresentation? Representation(string? representation) => representation switch
    {
        null => null,
        "simple" => ApiPlcDataRepresentation.Simple,
        "raw" => ApiPlcDataRepresentation.Raw,
        _ => throw new ArgumentOutOfRangeException(nameof(representation), representation, "Only simple and raw are supported."),
    };

    /// <summary>The caller's JSON value as a Newtonsoft token, without reinterpreting date-like strings.</summary>
    internal static JToken ToToken(JsonElement value)
    {
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(value.GetRawText()))
        {
            DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
            FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Double,
        };
        return JToken.ReadFrom(reader);
    }

    /// <summary>A client enum value in snake_case (the naming strategy the client library uses for its own enums).</summary>
    internal static string? EnumName<TEnum>(TEnum? value) where TEnum : struct, System.Enum
        => value is { } present ? SnakeCase.GetPropertyName(present.ToString(), hasSpecifiedName: false) : null;

    internal static string? EnumName<TEnum>(TEnum value) where TEnum : struct, System.Enum
        => SnakeCase.GetPropertyName(value.ToString(), hasSpecifiedName: false);

    internal static string? Timestamp(DateTime? value) => PlcWebApiFormat.Timestamp(value);

    private const int MaxModuleDepth = 32;

    private static PlcWebApiModuleInfo Module(SiemensModule node, int depth)
        => new(node.Hwid, node.Name, node.Class, EnumName(node.Type), EnumName(node.SubType),
            (node.Attributes ?? new()).Select(attribute => EnumName(attribute)!).ToArray(), node.HasChildren,
            (node.Parent ?? new()).Select(parent => (long)parent).ToArray(),
            depth >= MaxModuleDepth ? Array.Empty<PlcWebApiModuleInfo>()
                : (node.Children ?? new()).Where(child => child is not null).Select(child => Module(child, depth + 1)).ToArray());

    /// <summary>
    /// The library's own <c>ApiResponseChecker</c>, unchanged, plus a record of the PLC's error code and
    /// message (or HTTP status) on the exception it throws, so the mapping never parses message text.
    /// </summary>
    internal sealed class ErrorRecordingResponseChecker : IApiResponseChecker
    {
        private readonly IApiResponseChecker _inner = new ApiResponseChecker();

        public void CheckHttpResponseForErrors(HttpResponseMessage message, string apiRequestString)
        {
            try
            {
                _inner.CheckHttpResponseForErrors(message, apiRequestString);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                exception.Data[SiemensPlcWebApiErrors.HttpStatusKey] = (int)message.StatusCode;
                throw;
            }
        }

        public void CheckResponseStringForErros(string responseString, string apiRequestString)
        {
            Siemens.Simatic.S7.Webserver.API.Models.ApiError? error = null;
            try
            {
                error = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiErrorModel>(responseString)?.Error;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // The inner checker reports the malformed response itself.
            }

            try
            {
                _inner.CheckResponseStringForErros(responseString, apiRequestString);
            }
            catch (Exception exception) when (error is not null && exception is not OutOfMemoryException)
            {
                exception.Data[SiemensPlcWebApiErrors.CodeKey] = (int)error.Code;
                exception.Data[SiemensPlcWebApiErrors.MessageKey] = error.Message;
                throw;
            }
        }
    }
}

/// <summary>
/// Translates the client library's exceptions (see its <c>ApiErrorModel.ThrowAccordingException</c> and
/// <c>ApiResponseChecker</c>) into <see cref="PlcWebApiException"/> categories. The decision uses the
/// recorded Web API error code, the HTTP status and exception types, never message text. The outer
/// message of a library exception is its fixed description; the inner <c>ApiException</c> (which can
/// carry the request text) and <c>InvalidHttpRequestException</c> messages are never surfaced.
/// </summary>
public static class SiemensPlcWebApiErrors
{
    internal const string CodeKey = "TiaMcpServer.WebApi.Code";
    internal const string MessageKey = "TiaMcpServer.WebApi.Message";
    internal const string HttpStatusKey = "TiaMcpServer.WebApi.HttpStatus";
    private const int MaxMessageChars = 1000;

    /// <summary>
    /// Maps a client exception to a failure category and a message. When <paramref name="secret"/> (the
    /// password sent with the call) occurs anywhere in the message or the PLC's error text, the whole
    /// message is replaced by a fixed text with only the error code: blanking the occurrences instead
    /// would show where a short password occurs and so reveal it.
    /// </summary>
    public static PlcWebApiException Map(Exception exception, PlcWebApiTarget target, bool login = false, string? secret = null)
    {
        var mapped = MapCore(exception, target, login);
        if (string.IsNullOrEmpty(secret) || !Mentions(mapped, secret)) return mapped;
        var error = mapped.Error is null ? null : mapped.Error with { PlcMessage = null };
        var code = error?.Code is { } webApiCode ? $" with Web API error {webApiCode}{(error.Name is null ? string.Empty : $" ({error.Name})")}"
            : error?.HttpStatus is { } status ? $" with HTTP {status}" : string.Empty;
        return new PlcWebApiException(mapped.Category,
            $"{(login ? "Api.Login" : "The Web API call")} on PLC Web API target '{target.Name}' failed{code}. "
            + "The details are withheld because they could reveal the credential.", error);
    }

    private static bool Mentions(PlcWebApiException mapped, string secret)
        => mapped.Message.Contains(secret, StringComparison.Ordinal)
           || mapped.Error?.PlcMessage?.Contains(secret, StringComparison.Ordinal) == true;

    private static PlcWebApiException MapCore(Exception exception, PlcWebApiTarget target, bool login)
    {
        if (exception is PlcWebApiException known) return known;
        var where = $"PLC Web API target '{target.Name}' ({target.Address})";

        if (Recorded(exception, CodeKey) is int code)
        {
            var name = System.Enum.IsDefined(typeof(ApiErrorCode), code) ? ((ApiErrorCode)code).ToString() : null;
            var plcMessage = Truncate(exception.Data[MessageKey] as string);
            var error = new PlcWebApiErrorInfo(code, name, plcMessage, null);
            var description = Describe(exception);
            var detail = $"Web API error {code}{(name is null ? string.Empty : $" ({name})")}{(plcMessage is null ? string.Empty : $": {plcMessage}")}"
                + (description is null ? string.Empty : $". {description}");
            switch ((ApiErrorCode)code)
            {
                case ApiErrorCode.LoginFailed:
                case ApiErrorCode.PasswordExpired:
                case ApiErrorCode.InfrastructureError when login:
                    return new PlcWebApiException(WorkerFailureCategories.WebApiAuthenticationFailed,
                        $"Api.Login on {where} failed: {detail}. Check the user and password named by the target's userEnv/passwordEnv and the user's web server rights in TIA Portal.", error);
                case ApiErrorCode.PermissionDenied:
                    return login
                        ? new PlcWebApiException(WorkerFailureCategories.WebApiAuthenticationFailed, $"Api.Login on {where} was refused: {detail}.", error)
                        : new PlcWebApiException(WorkerFailureCategories.WebApiPermissionDenied,
                            $"{where} refused the call for the {(target.IsAnonymous ? "anonymous user" : "configured user")}: {detail}. "
                            + "Grant the right in the CPU's web server user management (TIA Portal) and download the hardware configuration.", error);
                default:
                    return new PlcWebApiException(WorkerFailureCategories.WebApiRejected, $"{where} rejected the call: {detail}.", error);
            }
        }

        if (Recorded(exception, HttpStatusKey) is int recordedStatus)
            return HttpStatus(where, recordedStatus);

        if (exception is UnauthorizedAccessException)
            return new PlcWebApiException(login ? WorkerFailureCategories.WebApiAuthenticationFailed : WorkerFailureCategories.WebApiPermissionDenied,
                $"{where} refused the call: {Truncate(exception.Message)}");

        foreach (var link in Chain(exception))
        {
            switch (link)
            {
                case HttpRequestException { StatusCode: { } status }:
                    return HttpStatus(where, (int)status);
                case HttpRequestException http:
                    return Chain(http).Any(inner => inner is AuthenticationException)
                        ? new PlcWebApiException(WorkerFailureCategories.WebApiUnreachable,
                            $"The TLS connection to {where} failed: the server certificate was not accepted under certificate validation "
                            + $"'{target.CertificateValidationName}'{(target.CertificateValidation == PlcWebApiCertificateValidation.Thumbprint ? " (the presented certificate does not match the pinned fingerprint)" : string.Empty)}. "
                            + "Install the PLC certificate (or its CA) in the trusted store, or pin it with validation 'thumbprint'.")
                        : new PlcWebApiException(WorkerFailureCategories.WebApiUnreachable,
                            $"{where} could not be reached{(http.HttpRequestError == HttpRequestError.Unknown ? string.Empty : $" ({http.HttpRequestError})")}: {Truncate(http.Message)}. "
                            + "Check the address, the network path, and that the web server is enabled on the CPU.");
                case OperationCanceledException or TimeoutException:
                    return new PlcWebApiException(WorkerFailureCategories.WebApiUnreachable,
                        $"{where} did not answer within the configured request timeout of {target.RequestTimeout.TotalSeconds:0} s.");
            }
        }

        if (exception is Newtonsoft.Json.JsonException or JsonException)
            return new PlcWebApiException(WorkerFailureCategories.WebApiRejected,
                $"{where} returned a response the client library could not read as the expected Web API result.");

        if (exception is ArgumentException or FileNotFoundException or DirectoryNotFoundException or CultureNotFoundException)
            return new PlcWebApiException(WorkerFailureCategories.ValidationError, $"{where}: {Truncate(exception.Message)}");

        // The client's own exception types without a recorded PLC error: local parameter checks (thrown before
        // anything is sent) and helper failures such as an incomplete ticket. Their message is a fixed description.
        if (exception.GetType().Namespace == typeof(ApiException).Namespace && exception is not (ApiException or InvalidHttpRequestException))
            return new PlcWebApiException(WorkerFailureCategories.WebApiRejected,
                $"The client library stopped the call to {where}: {Truncate(exception.Message)}");

        return new PlcWebApiException(WorkerFailureCategories.WebApiRejected,
            $"The Web API call to {where} failed unexpectedly ({exception.GetType().Name}).");
    }

    private static PlcWebApiException HttpStatus(string where, int status)
        => new(status is 401 or 403 ? WorkerFailureCategories.WebApiPermissionDenied : WorkerFailureCategories.WebApiRejected,
            $"{where} answered HTTP {status}.", new PlcWebApiErrorInfo(null, null, null, status));

    /// <summary>The library exception's fixed description; null for types whose message carries the request text.</summary>
    private static string? Describe(Exception exception)
        => exception is ApiException or InvalidHttpRequestException ? null : Truncate(exception.Message);

    private static object? Recorded(Exception exception, string key)
        => Chain(exception).Select(link => link.Data is IDictionary data && data.Contains(key) ? data[key] : null).FirstOrDefault(value => value is not null);

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            yield return current;
            current = current.InnerException;
        }
    }

    private static string? Truncate(string? text)
        => text is null ? null : text.Length <= MaxMessageChars ? text : text[..MaxMessageChars] + "...";
}
