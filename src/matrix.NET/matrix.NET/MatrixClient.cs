using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Authenticated access to a homeserver through a logged-in <see cref="MatrixSession"/>, whether
/// just returned by <see cref="MatrixServer.LoginAsync"/> or restored from storage.
/// </summary>
/// <remarks>
/// Creating a client performs no I/O and does not validate the access token; the first request
/// reveals whether the session is still valid. After <see cref="LogoutAsync"/>, or once the
/// homeserver rejects the token for good, the client is unusable (see <see cref="State"/>) and
/// every call throws <see cref="InvalidOperationException"/>. The client holds nothing that
/// needs disposing.
/// </remarks>
public class MatrixClient : ISharedEndpoints
{
    private readonly MatrixTransport _transport;
    private readonly SharedEndpoints _shared;

    // Replaced as a whole when the session changes, e.g. after a token refresh
    private MatrixSession _session;
    private volatile MatrixClientState _state = MatrixClientState.Active;

    /// <summary>
    /// Creates a client using the library's shared <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="session">The session to use.</param>
    /// <param name="options">Client options; defaults apply when omitted.</param>
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
    public MatrixClient(MatrixSession session, Func<HttpClient> httpClientSource, ClientOptions? options = null)
        : this(session, options, (Func<HttpClient>?)httpClientSource)
    {
    }

    private MatrixClient(MatrixSession session, ClientOptions? options, Func<HttpClient>? httpClientSource)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Options = options ?? new ClientOptions();

        var automaticDecompression = Options.AutomaticDecompression;
        _transport = new MatrixTransport(session.Homeserver,
            httpClientSource ?? (() => MatrixTransport.GetSharedClient(automaticDecompression)),
            () => _session.AccessToken);
        _shared = new SharedEndpoints(_transport);
    }

    /// <summary>The lifecycle state of the client.</summary>
    public MatrixClientState State => _state;

    /// <summary>The current session, including any tokens refreshed since the client was created.</summary>
    /// <exception cref="InvalidOperationException">The client has logged out or its session was invalidated.</exception>
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
        InvokeAsync(_shared.GetVersionsAsync, cancellationToken);

    /// <summary>
    /// Asks the homeserver who owns the session's access token.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<WhoAmIResponse> WhoAmIAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(ct => _transport.SendAsync<WhoAmIResponse>(HttpMethod.Get, "_matrix/client/v3/account/whoami",
            AuthRequirement.Required, ct), cancellationToken);

    /// <summary>
    /// Logs out, invalidating the session's tokens and device on the homeserver. Afterwards the
    /// client is unusable; only <see cref="UserId"/> and <see cref="DeviceId"/> remain readable.
    /// </summary>
    /// <remarks>
    /// If the homeserver no longer recognises the token, the session is already gone, so this
    /// counts as success. Other failures, e.g. network errors, throw and leave the client usable,
    /// so the logout can be retried.
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

        _state = MatrixClientState.LoggedOut;
    }

    // Every endpoint goes through here, so state checks and token rejection apply uniformly
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        EnsureUsable();
        try
        {
            return await call(cancellationToken);
        }
        catch (MatrixUnknownTokenException e)
        {
            throw OnTokenRejected(e);
        }
    }

    private Exception OnTokenRejected(MatrixUnknownTokenException rejection)
    {
        var hasRefreshToken = _session.RefreshToken is not null;

        // Placeholder until automatic refresh is implemented
        if (Options.AutoRefreshToken && hasRefreshToken)
            return new NotImplementedException(
                "Automatic token refresh is not implemented yet. Set ClientOptions.AutoRefreshToken to false to disable it.",
                rejection);

        _state = MatrixClientState.Invalidated;
        var message = hasRefreshToken
            ? "The access token is no longer valid and automatic token refresh is disabled. Refresh the session manually or log in again."
            : "The access token is no longer valid and the session has no refresh token. Log in again.";
        if (rejection.ServerMessage is not null)
            message += $" Homeserver: {rejection.ServerMessage}";

        return new MatrixUnknownTokenException(rejection.StatusCode, rejection.ServerMessage, rejection.SoftLogout,
            message, rejection);
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
}