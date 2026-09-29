using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

public class MatrixServer
{
    private readonly MatrixTransport _transport;

    public MatrixServer(Uri homeserver)
    {
        _transport = new MatrixTransport(homeserver);
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
    }

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