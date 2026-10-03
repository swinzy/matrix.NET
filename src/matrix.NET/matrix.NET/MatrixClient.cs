using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Authenticated access to a homeserver through a logged-in <see cref="MatrixSession"/>, whether
/// just returned by <see cref="MatrixServer.LoginAsync"/> or restored from storage.
/// </summary>
/// <remarks>
/// Creating a client performs no I/O and does not validate the access token; the first request
/// reveals whether the session is still valid. A rejected access token is refreshed automatically
/// when the session has a refresh token (see <see cref="ClientOptions.AutoRefreshToken"/>). After
/// <see cref="LogoutAsync"/>, or once the homeserver rejects the token for good, the client is unusable (see <see cref="State"/>) and
/// every call throws <see cref="InvalidOperationException"/>. The client holds nothing that
/// needs disposing.
/// </remarks>
public class MatrixClient : ISharedEndpoints
{
    private readonly MatrixTransport _transport;
    private readonly SharedEndpoints _shared;

    // Replaced as a whole when the session changes, e.g. after a token refresh
    private volatile MatrixSession _session;
    private volatile MatrixClientState _state = MatrixClientState.Active;
    private readonly Lock _stateLock = new();

    // Lets requests rejected for the same token share one refresh
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private const string RefreshFailedMessage = "The access token expired and could not be refreshed. Log in again.";

    /// <summary>
    /// Creates a client using the library's shared <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="session">The session to use.</param>
    /// <param name="options">Client options; defaults apply when omitted.</param>
    /// <exception cref="ArgumentException">
    /// Automatic token refresh is on and the session has a refresh token, but
    /// <see cref="ClientOptions.SessionRefreshHandler"/> is not set.
    /// </exception>
    public MatrixClient(MatrixSession session, ClientOptions? options = null)
        : this(session, options, httpClientSource: null)
    {
    }

    /// <summary>
    /// Creates a client whose requests use <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="session">The session to use.</param>
    /// <param name="httpClient">A long-lived client, owned and disposed by the caller.</param>
    /// <param name="options">Client options; defaults apply when omitted.</param>
    /// <exception cref="ArgumentException">
    /// Automatic token refresh is on and the session has a refresh token, but
    /// <see cref="ClientOptions.SessionRefreshHandler"/> is not set.
    /// </exception>
    public MatrixClient(MatrixSession session, HttpClient httpClient, ClientOptions? options = null)
        : this(session, options, httpClientSource: () => httpClient)
    {
    }

    /// <summary>
    /// Creates a client whose requests use an <see cref="HttpClient"/> obtained from
    /// <paramref name="httpClientSource"/> for every request.
    /// </summary>
    /// <param name="session">The session to use.</param>
    /// <param name="httpClientSource">
    /// Called once per request. It must return a long-lived client or one managed by
    /// <c>IHttpClientFactory</c>, e.g. <c>() => factory.CreateClient("matrix")</c>.
    /// Never create a new client in it, such as <c>() => new HttpClient()</c>: every request would
    /// then open new connections, which is slow and can exhaust sockets under load.
    /// </param>
    /// <param name="options">Client options; defaults apply when omitted.</param>
    /// <exception cref="ArgumentException">
    /// Automatic token refresh is on and the session has a refresh token, but
    /// <see cref="ClientOptions.SessionRefreshHandler"/> is not set.
    /// </exception>
    public MatrixClient(MatrixSession session, Func<HttpClient> httpClientSource, ClientOptions? options = null)
        : this(session, options, (Func<HttpClient>?)httpClientSource)
    {
    }

    private MatrixClient(MatrixSession session, ClientOptions? options, Func<HttpClient>? httpClientSource)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Options = options ?? new ClientOptions();

        // Refreshed tokens that are used before being saved are lost for good (D23)
        if (Options.AutoRefreshToken && session.RefreshToken is not null && Options.SessionRefreshHandler is null)
            throw new ArgumentException(
                "Automatic token refresh is on and the session has a refresh token, but no " +
                "ClientOptions.SessionRefreshHandler is set to save refreshed sessions. Set one, or set " +
                "ClientOptions.AutoRefreshToken to false to manage refresh in the app.", nameof(options));

        var automaticDecompression = Options.AutomaticDecompression;
        _transport = new MatrixTransport(session.Homeserver,
            httpClientSource ?? (() => MatrixTransport.GetSharedClient(automaticDecompression)),
            () => _session.AccessToken);
        _shared = new SharedEndpoints(_transport);
    }

    /// <summary>The lifecycle state of the client.</summary>
    public MatrixClientState State => _state;

    /// <summary>
    /// Raised once for each change of <see cref="State"/>, after the state has changed and before
    /// the triggering call returns or throws. Meant for UI and other work that does not affect
    /// correctness; never use it to persist refreshed tokens.
    /// </summary>
    /// <remarks>
    /// Handlers run synchronously on the thread that completed the request, typically a thread
    /// pool thread, so UI apps must marshal to their UI thread. Handlers must catch and handle
    /// their own exceptions: anything a handler throws is swallowed, so it cannot hide the
    /// library's own result or error. An <c>async</c> handler must wrap its whole body in
    /// <c>try</c>/<c>catch</c>, because exceptions thrown after its first <c>await</c> cannot be
    /// caught by the library and may crash the process.
    /// </remarks>
    public event EventHandler<SessionChangedEventArgs>? SessionChanged;

    /// <summary>The current session, including any tokens refreshed since the client was created.</summary>
    /// <exception cref="InvalidOperationException">The client has logged out or its session was invalidated.</exception>
    /// <remarks>Remains available while the account is locked, as the tokens stay valid.</remarks>
    public MatrixSession Session
    {
        get
        {
            EnsureUsable();
            return _session;
        }
    }

    /// <summary>The user this client acts as.</summary>
    public string UserId => _session.UserId;

    /// <summary>The device this client acts as.</summary>
    public string DeviceId => _session.DeviceId;

    /// <summary>The options this client was created with.</summary>
    public ClientOptions Options { get; }

    /// <inheritdoc cref="MatrixServer.GetVersionsAsync"/>
    /// <remarks>
    /// Sent with the session's access token, so experimental features the homeserver offers only to
    /// logged-in users are included.
    /// </remarks>
    public Task<VersionsResponse> GetVersionsAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(_shared.GetVersionsAsync, AuthRequirement.Optional, cancellationToken);

    /// <summary>
    /// Asks the homeserver who owns the session's access token.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<WhoAmIResponse> WhoAmIAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(ct => _transport.SendAsync<WhoAmIResponse>(HttpMethod.Get, "_matrix/client/v3/account/whoami",
            AuthRequirement.Required, ct), AuthRequirement.Required, cancellationToken);

    /// <summary>
    /// Logs out, invalidating the session's tokens and device on the homeserver. Afterwards the
    /// client is unusable; only <see cref="UserId"/> and <see cref="DeviceId"/> remain readable.
    /// </summary>
    /// <remarks>
    /// If the homeserver no longer recognises the token, the session is already gone, so this
    /// counts as success. Other failures, e.g. network errors, throw and leave the client usable,
    /// so the logout can be retried. Logging out also works while the account is locked.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="InvalidOperationException">The client has already logged out or been invalidated.</exception>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        try
        {
            await _transport.SendAsync<EmptyResponse>(HttpMethod.Post, "_matrix/client/v3/logout",
                AuthRequirement.Required, cancellationToken);
        }
        catch (MatrixUnknownTokenException)
        {
            // The token is already invalid, which is what logging out achieves
        }

        TryChangeState(MatrixClientState.LoggedOut, new SessionChangedEventArgs(SessionChangeKind.LoggedOut));
    }

    // Every endpoint goes through here, so state checks, token refresh and locking apply uniformly
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, AuthRequirement auth,
        CancellationToken cancellationToken)
    {
        EnsureUsable();
        T result;
        try
        {
            result = await SendWithRefreshAsync(call, cancellationToken);
        }
        catch (MatrixUserLockedException)
        {
            TryChangeState(MatrixClientState.Locked, new SessionChangedEventArgs(SessionChangeKind.Locked),
                MatrixClientState.Active);
            throw;
        }

        // Only an endpoint that requires a token proves the lock is lifted; an optional one may have
        // been answered without looking at the token
        if (auth == AuthRequirement.Required)
            TryChangeState(MatrixClientState.Active, new SessionChangedEventArgs(SessionChangeKind.Unlocked),
                MatrixClientState.Locked);

        return result;
    }

    // Each request may refresh once itself. A rejection after that ends the session. Retrying with a
    // token another request refreshed does not use up that chance, so the outcome does not depend on
    // whether this request was sent before or after the other refresh. The loop stops: every pass that
    // does not refresh here follows a real refresh by another request.
    private async Task<T> SendWithRefreshAsync<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        var refreshed = false;
        for (;;)
        {
            try
            {
                return await call(cancellationToken);
            }
            catch (MatrixUnknownTokenException e)
            {
                if (refreshed)
                    throw Invalidate(e, RefreshFailedMessage);
                refreshed = await RefreshAsync(e, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Refreshes the session after the homeserver rejected an access token, or throws what the caller
    /// should see if that is impossible. Returns <see langword="false"/> without refreshing if another
    /// request has already replaced the rejected token.
    /// </summary>
    private async Task<bool> RefreshAsync(MatrixUnknownTokenException rejection, CancellationToken cancellationToken)
    {
        if (_session.RefreshToken is null)
            throw Invalidate(rejection,
                "The access token is no longer valid and the session has no refresh token. Log in again.");
        if (!Options.AutoRefreshToken)
            throw Invalidate(rejection,
                "The access token is no longer valid and automatic token refresh is disabled. Refresh the session manually or log in again.");

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed, or failed to, while this one waited. The token is
            // compared with the one the request actually sent, which the transport read itself, so a
            // refresh landing just before sending cannot be mistaken for one that replaced it
            if (_state == MatrixClientState.Invalidated)
                throw Invalidate(rejection, RefreshFailedMessage);
            EnsureUsable();
            if (_session.AccessToken != rejection.RejectedAccessToken)
                return false;

            var refreshed = await RequestRefreshAsync(_session, cancellationToken);
            // The homeserver revokes the old refresh token once the new tokens are used, so they must
            // be saved first. If saving throws, they are never used and the old refresh token stays valid
            await Options.SessionRefreshHandler!.OnSessionRefreshedAsync(refreshed, cancellationToken);
            _session = refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }

        if (_state is not (MatrixClientState.LoggedOut or MatrixClientState.Invalidated))
            RaiseSessionChanged(new SessionChangedEventArgs(SessionChangeKind.TokensRefreshed));
        return true;
    }

    private async Task<MatrixSession> RequestRefreshAsync(MatrixSession session, CancellationToken cancellationToken)
    {
        RefreshResponse response;
        try
        {
            response = await _transport.SendAsync<RefreshRequest, RefreshResponse>(HttpMethod.Post,
                "_matrix/client/v3/refresh", new RefreshRequest(session.RefreshToken!), AuthRequirement.None,
                cancellationToken);
        }
        catch (MatrixUnknownTokenException e)
        {
            // The refresh token is unknown or already used. Unlike a network error, this is final
            throw Invalidate(e, RefreshFailedMessage);
        }

        return session with
        {
            AccessToken = response.AccessToken,
            // Without a new refresh token, the old one stays usable
            RefreshToken = response.RefreshToken ?? session.RefreshToken,
            // The spec gives a relative lifetime, which is meaningless once persisted (D11)
            ExpiresAt = response.ExpiresInMs is { } lifetime ? DateTimeOffset.UtcNow.AddMilliseconds(lifetime) : null
        };
    }

    /// <summary>
    /// Ends the session after a final token rejection and returns the exception to throw, carrying
    /// <paramref name="message"/> followed by the homeserver's own text.
    /// </summary>
    private MatrixUnknownTokenException Invalidate(MatrixUnknownTokenException rejection, string message)
    {
        TryChangeState(MatrixClientState.Invalidated,
            new SessionChangedEventArgs(SessionChangeKind.Invalidated, rejection.SoftLogout));
        if (rejection.ServerMessage is not null)
            message += $" Homeserver: {rejection.ServerMessage}";

        return new MatrixUnknownTokenException(rejection.StatusCode, rejection.ServerMessage, rejection.SoftLogout,
            message, rejection);
    }

    /// <summary>
    /// Moves to <paramref name="to"/> and raises <see cref="SessionChanged"/>, unless the session has
    /// already ended or, when <paramref name="from"/> is given, the current state differs from it.
    /// Returns whether the state changed, so each change is announced exactly once.
    /// </summary>
    private bool TryChangeState(MatrixClientState to, SessionChangedEventArgs change, MatrixClientState? from = null)
    {
        lock (_stateLock)
        {
            var current = _state;
            if (current is MatrixClientState.LoggedOut or MatrixClientState.Invalidated || current == to ||
                (from is not null && current != from))
                return false;

            _state = to;
        }

        RaiseSessionChanged(change);
        return true;
    }

    private void RaiseSessionChanged(SessionChangedEventArgs change)
    {
        if (SessionChanged is not { } handlers)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<SessionChangedEventArgs>>())
        {
            try
            {
                handler(this, change);
            }
            catch
            {
                // Handlers must handle their own errors; see SessionChanged
            }
        }
    }

    private void EnsureUsable()
    {
        switch (_state)
        {
            case MatrixClientState.LoggedOut:
                throw new InvalidOperationException(
                    "The client has logged out. Log in again and create a new MatrixClient.");
            case MatrixClientState.Invalidated:
                throw new InvalidOperationException(
                    "The session is no longer valid. Log in again and create a new MatrixClient.");
        }
    }

    private record EmptyResponse;

    private record RefreshRequest(string RefreshToken);

    private class RefreshResponse
    {
        public required string AccessToken { get; init; }
        public string? RefreshToken { get; init; }
        public long? ExpiresInMs { get; init; }
    }
}