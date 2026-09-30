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
    /// <param name="automaticDecompression">
    /// Whether compressed responses (GZip, Deflate, Brotli) are requested and decompressed
    /// automatically. Turn it off only if you need the raw encoded bytes, e.g. when forwarding
    /// responses unchanged.
    /// </param>
    public MatrixServer(Uri homeserver, bool automaticDecompression = true)
    {
        _transport = new MatrixTransport(homeserver, () => MatrixTransport.GetSharedClient(automaticDecompression));
        _shared = new SharedEndpoints(_transport);
    }

    public MatrixServer(Uri homeserver, HttpClient client)
        : this(homeserver, () => client)
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
    public MatrixServer(Uri homeserver, Func<HttpClient> httpClientSource)
    {
        _transport = new MatrixTransport(homeserver, httpClientSource);
        _shared = new SharedEndpoints(_transport);
    }

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
        var response = await _transport.SendAsync<LoginFlowsResponse>(HttpMethod.Get,
            "_matrix/client/v3/login", AuthRequirement.None, cancellationToken);
        return response.Flows ?? [];
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        _transport.SendAsync<LoginRequest, LoginResponse>(HttpMethod.Post,
            "_matrix/client/v3/login", request, AuthRequirement.None, cancellationToken);

    private record LoginFlowsResponse(List<LoginFlow>? Flows);
}