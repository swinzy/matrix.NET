using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Authenticated access to a homeserver through a logged-in <see cref="MatrixSession"/>, whether
/// just returned by <see cref="MatrixServer.LoginAsync"/> or restored from storage.
/// </summary>
/// <remarks>
/// Creating a client performs no I/O and does not validate the access token; the first request
/// reveals whether the session is still valid.
/// </remarks>
public class MatrixClient : ISharedEndpoints
{
    private readonly MatrixTransport _transport;
    private readonly SharedEndpoints _shared;

    // Replaced as a whole when the session changes, e.g. after a token refresh
    private MatrixSession _session;

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

    /// <summary>The current session, including any tokens refreshed since the client was created.</summary>
    public MatrixSession Session => _session;

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
        _shared.GetVersionsAsync(cancellationToken);

    /// <summary>
    /// Asks the homeserver who owns the session's access token.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<WhoAmIResponse> WhoAmIAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync<WhoAmIResponse>(HttpMethod.Get, "_matrix/client/v3/account/whoami",
            AuthRequirement.Required, cancellationToken);
}