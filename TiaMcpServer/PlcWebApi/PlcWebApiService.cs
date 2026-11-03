using System.Collections.Concurrent;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.PlcWebApi;

/// <summary>
/// The configured targets and one Web API session per target. A target with <c>userEnv</c> logs in
/// once (credentials are read from the named environment variables at login time and never kept),
/// logs in again once when the PLC refuses a single request for missing permission (Web API error 2,
/// HTTP 401/403: the session may have expired), and logs out when the host shuts down. An anonymous
/// target never calls <c>Api.Login</c>: its requests carry no token and run with the rights of the
/// CPU's anonymous web server user. Calls to one target run one at a time (the client library keeps
/// the session token on the shared HTTP client), so a long call such as a restore delays the next
/// call to that target; waiting is cancellable. Different targets run independently.
/// </summary>
public sealed class PlcWebApiService : IDisposable, IAsyncDisposable
{
    /// <summary>Upper bound of one <c>restore_backup</c> item, including the PLC restarts it causes.</summary>
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(5);

    private readonly IPlcWebApiClientFactory _clients;
    private readonly Func<string, string?> _environment;
    private readonly ConcurrentDictionary<string, Lazy<Session>> _sessions = new(StringComparer.Ordinal);
    private int _disposed;

    public PlcWebApiService(
        PlcWebApiConfiguration configuration,
        OperationAccessPolicy accessPolicy,
        IPlcWebApiClientFactory clients,
        IWriteAuditSink audit,
        Func<string, string?>? environment = null,
        TimeProvider? time = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        AccessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _environment = environment ?? Environment.GetEnvironmentVariable;
        Execution = new WriteExecution(new PlcWebApiWriteGate(this), new PlcWebApiAuditSink(audit ?? throw new ArgumentNullException(nameof(audit))),
            PlcWebApiGuardDefinitions.Catalog, time ?? TimeProvider.System);
    }

    public PlcWebApiConfiguration Configuration { get; }

    public OperationAccessPolicy AccessPolicy { get; }

    public McpAccessMode AccessMode => AccessPolicy.Mode;

    /// <summary>The guarded single-call pipeline for plc_online_write: own gate, audit kind and guard catalog.</summary>
    public WriteExecution Execution { get; }

    /// <summary>
    /// Runs one Web API call against <paramref name="target"/>, alone on that target, logging in first
    /// when needed. When <paramref name="retryAfterRelogin"/> is true and the PLC refuses the call for
    /// missing permission on an authenticated session, the session may have expired: the service logs
    /// in again and repeats the call once. Pass true only for a call that is a single request: the PLC
    /// does not execute a request it refuses for missing permission, but a call made of several
    /// requests may already have executed the earlier ones.
    /// </summary>
    public async Task<T> CallAsync<T>(
        PlcWebApiTarget target,
        Func<IPlcWebApiClient, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken,
        bool retryAfterRelogin = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(call);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var session = SessionFor(target);
        await session.Turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || session.Closed)
            {
                session.CloseLocally();
                throw new ObjectDisposedException(nameof(PlcWebApiService));
            }

            await session.EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await call(session.Client, cancellationToken).ConfigureAwait(false);
            }
            catch (PlcWebApiException exception) when (retryAfterRelogin && !target.IsAnonymous
                && exception.Category == WorkerFailureCategories.WebApiPermissionDenied)
            {
                await session.ReloginAsync(cancellationToken).ConfigureAwait(false);
                return await call(session.Client, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            session.Turn.Release();
        }
    }

    /// <summary>The target's credentials, read now from the environment; null for an anonymous target.</summary>
    internal (string User, string Password)? ReadCredentials(PlcWebApiTarget target)
    {
        if (target.IsAnonymous) return null;
        var user = _environment(target.UserEnv!);
        var password = _environment(target.PasswordEnv!);
        if (string.IsNullOrEmpty(user) || password is null)
            throw new PlcWebApiException(WorkerFailureCategories.WebApiAuthenticationFailed,
                $"PLC Web API target '{target.Name}' needs the environment variables {target.UserEnv} and {target.PasswordEnv}, "
                + $"but {(string.IsNullOrEmpty(user) ? target.UserEnv : target.PasswordEnv)} is not set in the MCP server's environment.");
        return (user, password);
    }

    /// <summary>True when the target's credential variables are present (values are never returned).</summary>
    internal bool HasCredentials(PlcWebApiTarget target)
        => target.IsAnonymous || (!string.IsNullOrEmpty(_environment(target.UserEnv!)) && _environment(target.PasswordEnv!) is not null);

    public void Dispose() => DisposeAsync().AsTask().Wait(LogoutTimeout + TimeSpan.FromSeconds(1));

    /// <summary>
    /// Logs every authenticated session out (best effort, bounded) and releases the HTTP clients. A call
    /// still running on a target keeps its turn; that target's client is released without a logout.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var sessions = _sessions.Values.Where(session => session.IsValueCreated).Select(session => session.Value).ToArray();
        using var timeout = new CancellationTokenSource(LogoutTimeout);
        await Task.WhenAll(sessions.Select(session => session.CloseAsync(timeout.Token))).ConfigureAwait(false);
    }

    /// <summary>One session per target name; <see cref="Lazy{T}"/> makes sure only one client is ever created for it.</summary>
    private Session SessionFor(PlcWebApiTarget target)
        => _sessions.GetOrAdd(target.Name, _ => new Lazy<Session>(() => new Session(this, target, _clients.Create(target)),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private sealed class Session
    {
        private readonly PlcWebApiService _owner;
        private readonly PlcWebApiTarget _target;
        private int _closed;

        public Session(PlcWebApiService owner, PlcWebApiTarget target, IPlcWebApiClient client)
        {
            _owner = owner;
            _target = target;
            Client = client;
        }

        public IPlcWebApiClient Client { get; }

        /// <summary>Held for the whole of one call, login and re-login included.</summary>
        public SemaphoreSlim Turn { get; } = new(1, 1);

        public bool Closed => Volatile.Read(ref _closed) != 0;

        /// <summary>Caller holds <see cref="Turn"/>.</summary>
        public async Task EnsureLoggedInAsync(CancellationToken cancellationToken)
        {
            if (_target.IsAnonymous || Client.HasSession) return;
            await LoginAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Caller holds <see cref="Turn"/>.</summary>
        public async Task ReloginAsync(CancellationToken cancellationToken)
        {
            if (Client.HasSession)
            {
                try
                {
                    await Client.LogoutAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (PlcWebApiException)
                {
                    // An expired token cannot log out; LogoutAsync dropped it locally.
                }
            }

            await LoginAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            var turn = false;
            try
            {
                await Turn.WaitAsync(cancellationToken).ConfigureAwait(false);
                turn = true;
                if (!Closed && Client.HasSession) await Client.LogoutAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is PlcWebApiException or OperationCanceledException)
            {
                // Shutdown is best effort; the PLC expires the session on its own.
            }
            finally
            {
                CloseLocally();
                if (turn) Turn.Release();
            }
        }

        public void CloseLocally()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) Client.Dispose();
        }

        private async Task LoginAsync(CancellationToken cancellationToken)
        {
            var credentials = _owner.ReadCredentials(_target)!.Value;
            await Client.LoginAsync(credentials.User, credentials.Password, cancellationToken).ConfigureAwait(false);
        }
    }
}
