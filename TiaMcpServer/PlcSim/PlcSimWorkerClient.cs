using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcSim;

/// <summary>A request/response channel to one PLCSIM worker; the production channel is a persistent process.</summary>
public interface IPlcSimWorkerChannel : IDisposable
{
    Task<WorkerResponse> SendAsync(WorkerRequest request);
}

/// <summary>
/// Typed host facade of the S7-PLCSIM Advanced worker. It is independent of the Openness worker
/// client: it never touches the project binding, and it starts its own worker process lazily on the
/// first PLCSIM call (tool discovery and argument validation never start it). Every request is
/// authorized against the access mode before the process is started or a request is written, and
/// only <c>plcsim_*</c> methods are ever sent. When the worker states that a restart is required
/// (<see cref="WorkerResponse.WorkerRestartRequired"/>), or a probe reports a failed API
/// initialization, the worker is recycled: the next call starts a fresh process, because .NET keeps a
/// failed type initializer for the lifetime of the process that observed it. A missing or unloadable
/// API (<c>plcsim_api_unavailable</c> without that flag) keeps the worker, which retries the load itself.
/// </summary>
public sealed class PlcSimWorkerClient : IDisposable
{
    private const int MaxWarningLines = 20;
    private const int MaxWarningLineChars = 1_000;

    private readonly ILogger<PlcSimWorkerClient>? _logger;
    private readonly string? _workerExecutablePath;
    private readonly Func<IPlcSimWorkerChannel>? _channelFactory;
    private readonly object _channelLock = new();
    private ChannelLease? _channel;
    private bool _disposed;

    public PlcSimWorkerClient(
        OperationAccessPolicy accessPolicy,
        TimeSpan? requestTimeout = null,
        ILogger<PlcSimWorkerClient>? logger = null,
        string? workerExecutablePath = null,
        Func<IPlcSimWorkerChannel>? channelFactory = null)
    {
        AccessPolicy = accessPolicy;
        RequestTimeout = requestTimeout ?? TimeSpan.FromMinutes(WorkerTimeoutParser.DefaultMinutes);
        _logger = logger;
        _workerExecutablePath = workerExecutablePath;
        _channelFactory = channelFactory;
    }

    public OperationAccessPolicy AccessPolicy { get; }

    /// <summary>The per-request worker timeout (the same configured value as the Openness worker).</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>True while a worker channel exists: after the first request that reached the transport, until the worker is recycled.</summary>
    public bool HasStartedWorker
    {
        get
        {
            lock (_channelLock)
                return _channel is not null;
        }
    }

    public async Task<WorkerCallResult> InvokeAsync(string method, PlcSimRequestInfo? request)
    {
        var denial = AccessPolicy.Authorize(method);
        if (denial is not null)
            return denial with { DispatchState = WorkerDispatchState.NotSent };
        if (!PlcSimMethods.IsPlcSimMethod(method))
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"'{method}' is not a PLCSIM worker method.")
                with { DispatchState = WorkerDispatchState.NotSent };

        var observes = OperationPolicyCatalog.GetCapability(method) == OperationCapability.Observe;
        ChannelLease? lease = null;
        var recycle = false;
        try
        {
            lease = AcquireChannel();
            var response = await lease.Channel.SendAsync(new WorkerRequest { Method = method, PlcSim = request }).ConfigureAwait(false);
            recycle = RequiresRestart(method, response);
            var warnings = CapWarnings(response.Warnings);
            if (response.Success)
                return WorkerCallResult.Ok(response.Payload ?? string.Empty, warnings) with { DispatchState = WorkerDispatchState.Sent };

            var category = WorkerFailureCategories.IsKnown(response.FailureCategory)
                ? response.FailureCategory!
                : WorkerFailureCategories.WorkerOperationFailed;
            return WorkerCallResult.Fail(category, response.Error ?? "The PLCSIM worker failed without an error message.", warnings)
                with { DispatchState = WorkerDispatchState.Sent };
        }
        catch (FileNotFoundException exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, exception.Message)
                with { DispatchState = WorkerDispatchState.NotSent };
        }
        catch (Win32Exception exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed,
                $"Failed to launch the PLCSIM worker process ({exception.Message}). Verify that .NET Framework 4.8 is installed, that this is 64-bit Windows, and that the 'plcsim-worker' folder beside the MCP server executable is complete.")
                with { DispatchState = WorkerDispatchState.NotSent };
        }
        catch (TimeoutException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerTimeout, observes
                    ? $"The PLCSIM worker did not answer within {RequestTimeout.TotalMinutes:N0} minutes. No simulation change was requested; the worker was terminated and retrying the read is safe."
                    : $"The PLCSIM worker did not answer within {RequestTimeout.TotalMinutes:N0} minutes; the worker was terminated. The simulated CPU may have changed: inspect it with plcsim_read get_instance before retrying.")
                with { DispatchState = WorkerDispatchState.Unknown };
        }
        catch (PersistentWorkerTransport.WorkerProtocolMismatchException exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, exception.Message)
                with { DispatchState = WorkerDispatchState.NotSent };
        }
        catch (PersistentWorkerTransport.WorkerTransportDisposedException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed,
                "The MCP server is shutting down; the request was not sent to the PLCSIM worker.")
                with { DispatchState = WorkerDispatchState.NotSent };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerCrashed, observes
                    ? "The PLCSIM worker stopped before returning the result. No simulation change was requested; it restarts on the next call and retrying the read is safe."
                    : "The PLCSIM worker stopped before completion was confirmed. The simulated CPU may have changed: inspect it with plcsim_read get_instance before retrying.")
                with { DispatchState = WorkerDispatchState.Unknown };
        }
        finally
        {
            if (lease is not null)
                ReleaseChannel(lease, recycle);
        }
    }

    /// <summary>
    /// A failed API initialization is cached by .NET for the worker process, so only a fresh process can
    /// retry it. The worker says so explicitly (<see cref="WorkerResponse.WorkerRestartRequired"/>, on any
    /// response); a successful probe whose typed payload reports <c>apiInitializationFailed: true</c>
    /// counts too. The failure category alone never triggers a restart.
    /// </summary>
    internal static bool RequiresRestart(string method, WorkerResponse response)
    {
        if (response.WorkerRestartRequired == true)
            return true;
        if (!response.Success || !string.Equals(method, PlcSimMethods.ProbeApi, StringComparison.Ordinal) || string.IsNullOrEmpty(response.Payload))
            return false;
        try
        {
            using var document = JsonDocument.Parse(response.Payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("apiInitializationFailed", out var failed)
                && failed.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private ChannelLease AcquireChannel()
    {
        lock (_channelLock)
        {
            if (_disposed)
                throw new PersistentWorkerTransport.WorkerTransportDisposedException();
            _channel ??= new ChannelLease(_channelFactory?.Invoke() ?? new PersistentChannel(new PersistentWorkerTransport(
                _workerExecutablePath ?? PlcSimWorkerLocator.LocateOrThrow(AppContext.BaseDirectory, FileSystemService.Instance),
                RequestTimeout,
                _logger,
                $"--access-mode {McpAccessModeNames.ToName(AccessPolicy.Mode)}",
                new WorkerHandshake(PlcSimWorkerProtocol.Version, PlcSimWorkerProtocol.RequiredCapabilities, PlcSimWorkerProtocol.DisplayName))));
            _channel.Users++;
            return _channel;
        }
    }

    /// <summary>
    /// Ends one request on <paramref name="lease"/>. A recycle retires the lease (later calls create a new
    /// worker) and disposes its channel once no request is still using it, so a concurrent call is never cut off.
    /// </summary>
    private void ReleaseChannel(ChannelLease lease, bool recycle)
    {
        IPlcSimWorkerChannel? dispose = null;
        lock (_channelLock)
        {
            lease.Users--;
            if (recycle && ReferenceEquals(_channel, lease))
            {
                _channel = null;
                lease.Retired = true;
                _logger?.LogInformation("Recycling the PLCSIM worker: the S7-PLCSIM Advanced API failed to initialize in this process.");
            }

            if (lease.Retired && lease.Users == 0 && !lease.Disposed)
            {
                lease.Disposed = true;
                dispose = lease.Channel;
            }
        }

        dispose?.Dispose();
    }

    private static IReadOnlyList<string> CapWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
            return Array.Empty<string>();
        var lines = warnings.Select(line => line.Trim()).Where(line => line.Length > 0)
            .Select(line => line.Length <= MaxWarningLineChars ? line : line.Substring(0, MaxWarningLineChars - 12) + " [TRUNCATED]")
            .ToList();
        if (lines.Count > MaxWarningLines)
        {
            var dropped = lines.Count - MaxWarningLines;
            lines = lines.Take(MaxWarningLines).ToList();
            lines.Add($"(+{dropped} more worker warnings truncated)");
        }

        return lines;
    }

    public void Dispose()
    {
        lock (_channelLock)
        {
            _disposed = true;
            if (_channel is { Disposed: false } current)
            {
                current.Disposed = true;
                current.Channel.Dispose();
            }

            _channel = null;
        }
    }

    private sealed class ChannelLease
    {
        public ChannelLease(IPlcSimWorkerChannel channel) => Channel = channel;

        public IPlcSimWorkerChannel Channel { get; }

        /// <summary>Requests currently using the channel.</summary>
        public int Users { get; set; }

        /// <summary>No new request uses it; disposed when the last user releases it.</summary>
        public bool Retired { get; set; }

        public bool Disposed { get; set; }
    }

    private sealed class PersistentChannel : IPlcSimWorkerChannel
    {
        private readonly PersistentWorkerTransport _transport;

        public PersistentChannel(PersistentWorkerTransport transport) => _transport = transport;

        public Task<WorkerResponse> SendAsync(WorkerRequest request) => _transport.SendAsync(request);

        public void Dispose() => _transport.Dispose();
    }
}
