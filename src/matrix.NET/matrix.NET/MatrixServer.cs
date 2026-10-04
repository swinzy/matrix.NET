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

        var automaticDecompression = Options.AutomaticDecompression;
        _transport = new MatrixTransport(homeserver,
            httpClientSource ?? (() => MatrixTransport.GetSharedClient(automaticDecompression)));
        _shared = new SharedEndpoints(_transport);
    }

    /// <summary>The options this server was created with.</summary>
    public ServerOptions Options { get; }

    /// <summary>
    /// Gets the specification versions and experimental features the homeserver supports.
    /// </summary>
    /// <remarks>
    /// Sent without an access token. Homeservers may advertise some experimental features only
    /// to logged-in users, so a <see cref="MatrixClient"/> may see more.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<VersionsResponse> GetVersionsAsync(CancellationToken cancellationToken = default) =>
        _shared.GetVersionsAsync(cancellationToken);

    public async Task<List<LoginFlow>> GetSupportedLoginTypesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _transport.SendAsync<LoginFlowsResponse>(Endpoints.LoginFlows, cancellationToken);
        return response.Flows ?? [];
    }

    /// <summary>
    /// Logs in and returns the new session. The app is responsible for saving it (D23).
    /// </summary>
    /// <param name="request">Login type and credentials.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<MatrixSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _transport.SendAsync<LoginRequest, LoginResponse>(Endpoints.Login, request,
            cancellationToken);

        return new MatrixSession
        {
            Homeserver = _transport.Homeserver,
            UserId = response.UserId,
            DeviceId = response.DeviceId,
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            // The spec gives a relative lifetime, which is meaningless once persisted (D11)
            ExpiresAt = response.ExpiresInMs is { } lifetime ? DateTimeOffset.UtcNow.AddMilliseconds(lifetime) : null
        };
    }

    internal record LoginFlowsResponse(List<LoginFlow>? Flows);
}