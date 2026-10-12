using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

public class MatrixServer : ISharedEndpoints
{
    private readonly MatrixTransport _transport;
    private readonly SharedEndpoints _shared;

    /// <summary>
    /// Creates a server using the library's shared <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="homeserver">Base URL of the homeserver.</param>
    /// <param name="options">Server options; defaults apply when omitted.</param>
    public MatrixServer(Uri homeserver, ServerOptions? options = null)
        : this(homeserver, options, httpClientSource: null)
    {
    }

    /// <summary>
    /// Creates a server whose requests use <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="homeserver">Base URL of the homeserver.</param>
    /// <param name="httpClient">A long-lived client, owned and disposed by the caller.</param>
    /// <param name="options">Server options; defaults apply when omitted.</param>
    public MatrixServer(Uri homeserver, HttpClient httpClient, ServerOptions? options = null)
        : this(homeserver, options, httpClientSource: () => httpClient)
    {
    }

    /// <summary>
    /// Creates a server whose requests use an <see cref="HttpClient"/> obtained from
    /// <paramref name="httpClientSource"/> for every request.
    /// </summary>
    /// <param name="homeserver">Base URL of the homeserver.</param>
    /// <param name="httpClientSource">
    /// Called once per request. It must return a long-lived client or one managed by
    /// <c>IHttpClientFactory</c>, e.g. <c>() => factory.CreateClient("matrix")</c>.
    /// Never create a new client in it, such as <c>() => new HttpClient()</c>: every request would
    /// then open new connections, which is slow and can exhaust sockets under load.
    /// </param>
    /// <param name="options">Server options; defaults apply when omitted.</param>
    public MatrixServer(Uri homeserver, Func<HttpClient> httpClientSource, ServerOptions? options = null)
        : this(homeserver, options, (Func<HttpClient>?)httpClientSource)
    {
    }

    private MatrixServer(Uri homeserver, ServerOptions? options, Func<HttpClient>? httpClientSource)
    {
        ArgumentNullException.ThrowIfNull(homeserver);

        Options = options ?? new ServerOptions();
        VersionsCache.ValidateLifetime(Options.VersionsCacheLifetime, nameof(options));
        RateLimitRetry.Validate(Options.MaxRateLimitRetries, Options.MaxRateLimitWait, nameof(options));
        WaitReporter.ValidateThreshold(Options.WaitReportThreshold, nameof(options));

        var automaticDecompression = Options.AutomaticDecompression;
        _shared = new SharedEndpoints(FetchVersionsAsync, Options.VersionsCacheLifetime, Options.TimeProvider,
            StartCall);
        _transport = new MatrixTransport(homeserver,
            httpClientSource ?? (() => MatrixTransport.GetSharedClient(automaticDecompression)),
            cachedVersions: () => _shared.Versions.Cached, rateLimitRetry: Options.CreateRateLimitRetry(),
            waitReportThreshold: Options.WaitReportThreshold, time: Options.TimeProvider);
    }

    /// <summary>The options this server was created with.</summary>
    public ServerOptions Options { get; }

    /// <summary>
    /// Gets the specification versions and experimental features the homeserver supports, from the
    /// cache if it holds them and they have not expired (D25); otherwise fetches them first.
    /// </summary>
    /// <remarks>
    /// Sent without an access token. Homeservers may advertise some experimental features only
    /// to logged-in users, so a <see cref="MatrixClient"/> may see more. Concurrent calls share one
    /// request, and a failed request leaves the cache as it was.
    /// </remarks>
    /// <param name="progress">Told when the call waits longer than <see cref="ServerOptions.WaitReportThreshold"/> (D38).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<VersionsResponse> GetVersionsAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default) =>
        _shared.GetVersionsAsync(progress, cancellationToken);

    /// <summary>
    /// Fetches the specification versions and experimental features now, whatever the cache holds,
    /// and caches them (D25). Use it to pay for the request when the app chooses, e.g. at start-up.
    /// </summary>
    /// <param name="progress">Told when the call waits longer than <see cref="ServerOptions.WaitReportThreshold"/> (D38).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<VersionsResponse> RefreshVersionsAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default) =>
        _shared.RefreshVersionsAsync(progress, cancellationToken);

    /// <summary>
    /// Whether the homeserver advertises <paramref name="feature"/>, e.g. to hide or disable it before
    /// the user tries it (D26). Uses the cached versions, fetching them if needed as
    /// <see cref="GetVersionsAsync"/> does.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> is not proof that the feature is missing: a homeserver may ship it before
    /// advertising the version that contains it. To be certain, call the endpoint and handle the error.
    /// Homeservers may advertise some features only to logged-in users, so a <see cref="MatrixClient"/>
    /// may answer <see langword="true"/> where this answers <see langword="false"/>.
    /// </remarks>
    /// <param name="feature">A feature, e.g. <see cref="MatrixFeature.TokenRefresh"/>.</param>
    /// <param name="progress">Told when the call waits longer than <see cref="ServerOptions.WaitReportThreshold"/> (D38).</param>
    /// <param name="cancellationToken">Cancels the request, if one is needed.</param>
    public Task<bool> SupportsAsync(MatrixFeature feature, IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default) =>
        _shared.SupportsAsync(feature, progress, cancellationToken);

    /// <summary>
    /// Gets the login types the homeserver supports (<c>GET /login</c>), e.g. to choose which login form
    /// to show.
    /// </summary>
    /// <param name="progress">Told when the call waits longer than <see cref="ServerOptions.WaitReportThreshold"/> (D38).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<List<LoginFlow>> GetSupportedLoginTypesAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _transport.SendAsync<LoginFlowsResponse>(Endpoints.LoginFlows, cancellationToken,
            waits: StartCall(progress));
        return response.Flows ?? [];
    }

    /// <summary>
    /// Finds the homeserver of a user ID or server name through <c>/.well-known/matrix/client</c>, and
    /// checks that it answers <c>GET /versions</c> (D36). Meant for the login screen; the spec says
    /// discovery should only be done at login.
    /// </summary>
    /// <remarks>
    /// The lookup goes to the server name's hostname over HTTPS, without any port, as the spec says:
    /// for <c>@alice:example.org:8448</c>, <c>https://example.org/.well-known/matrix/client</c>.
    /// </remarks>
    /// <param name="userIdOrServerName">A user ID, e.g. <c>@alice:example.org</c>, or a server name, e.g. <c>example.org</c>.</param>
    /// <param name="progress">
    /// Told when the call waits longer than three seconds (D38); the threshold cannot be changed, as no
    /// options apply to discovery.
    /// </param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>
    /// The homeserver found, or <see langword="null"/> if the server name publishes no discovery
    /// information (404). What to do then is the app's choice, e.g. asking for the homeserver URL, or
    /// trying <c>https://</c> and the server name as one.
    /// </returns>
    /// <exception cref="ArgumentException">The input is not a user ID or server name.</exception>
    /// <exception cref="MatrixDiscoveryException">
    /// Discovery failed; <see cref="MatrixDiscoveryException.Kind"/> says whether to ask the user for
    /// the homeserver URL or to stop.
    /// </exception>
    public static Task<ServerDiscovery?> DiscoverAsync(string userIdOrServerName,
        IProgress<RequestWait>? progress = null, CancellationToken cancellationToken = default) =>
        Discovery.DiscoverAsync(userIdOrServerName, null, progress, cancellationToken);

    /// <inheritdoc cref="DiscoverAsync(string, IProgress{RequestWait}?, CancellationToken)"/>
    /// <param name="userIdOrServerName">A user ID, e.g. <c>@alice:example.org</c>, or a server name, e.g. <c>example.org</c>.</param>
    /// <param name="httpClient">A long-lived client for both requests, owned and disposed by the caller.</param>
    /// <param name="progress">
    /// Told when the call waits longer than three seconds (D38); the threshold cannot be changed, as no
    /// options apply to discovery.
    /// </param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    public static Task<ServerDiscovery?> DiscoverAsync(string userIdOrServerName, HttpClient httpClient,
        IProgress<RequestWait>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return Discovery.DiscoverAsync(userIdOrServerName, () => httpClient, progress, cancellationToken);
    }

    /// <summary>
    /// Logs in and returns the new session, with any discovery information the homeserver sent. The
    /// app is responsible for saving the session (D23).
    /// </summary>
    /// <param name="request">Login type and credentials.</param>
    /// <param name="progress">Told when the call waits longer than <see cref="ServerOptions.WaitReportThreshold"/> (D38).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<LoginResult> LoginAsync(LoginRequest request, IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _transport.SendAsync<LoginRequest, LoginResponse>(Endpoints.Login, request,
            cancellationToken, waits: StartCall(progress));

        var session = new MatrixSession
        {
            Homeserver = _transport.Homeserver,
            UserId = response.UserId,
            DeviceId = response.DeviceId,
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            // The spec gives a relative lifetime, which is meaningless once persisted (D11)
            ExpiresAt = response.ExpiresInMs is { } lifetime ? DateTimeOffset.UtcNow.AddMilliseconds(lifetime) : null
        };
        return new LoginResult(session, DiscoveryInformation.TryRead(response.WellKnown));
    }

    private WaitReporter? StartCall(IProgress<RequestWait>? progress) => _transport.StartCall(progress);

    private Task<VersionsResponse> FetchVersionsAsync(WaitReporter? waits, CancellationToken cancellationToken) =>
        _transport.SendAsync<VersionsResponse>(Endpoints.Versions, cancellationToken, waits: waits);

    internal record LoginFlowsResponse(List<LoginFlow>? Flows);
}