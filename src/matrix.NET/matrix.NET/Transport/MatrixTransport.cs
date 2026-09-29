using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// The single path every request to a homeserver goes through: URI building, JSON,
/// authentication and error mapping.
/// </summary>
internal sealed class MatrixTransport
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Shared across the library; pooled connections are recycled so DNS changes are picked up
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

    private readonly Func<HttpClient> _clientSource;
    private readonly Func<string?>? _accessTokenSource;

    /// <param name="homeserver">Base URL of the homeserver; may include a path.</param>
    /// <param name="clientSource">
    /// Called once per request. Defaults to a library-wide shared client.
    /// </param>
    /// <param name="accessTokenSource">
    /// Called once per request that may carry a token. Null for unauthenticated use.
    /// </param>
    public MatrixTransport(Uri homeserver, Func<HttpClient>? clientSource = null,
        Func<string?>? accessTokenSource = null)
    {
        Homeserver = WithTrailingSlash(homeserver);
        _clientSource = clientSource ?? (() => SharedClient);
        _accessTokenSource = accessTokenSource;
    }

    public Uri Homeserver { get; }

    public Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, AuthRequirement auth,
        CancellationToken cancellationToken = default) =>
        SendCoreAsync<TResponse>(method, path, null, null, auth, cancellationToken);

    public Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest body,
        AuthRequirement auth, CancellationToken cancellationToken = default) =>
        SendCoreAsync<TResponse>(method, path, body, typeof(TRequest), auth, cancellationToken);

    private async Task<TResponse> SendCoreAsync<TResponse>(HttpMethod method, string path, object? body,
        Type? bodyType, AuthRequirement auth, CancellationToken cancellationToken)
    {
        // Requests are built from their parts rather than reused, so they can be rebuilt
        // when retrying after a token refresh
        using var request = CreateRequest(method, path, body, bodyType, auth);
        using var response = await _clientSource().SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
               ?? throw new JsonException($"The homeserver returned an empty {typeof(TResponse).Name}.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body, Type? bodyType,
        AuthRequirement auth)
    {
        var request = new HttpRequestMessage(method, new Uri(Homeserver, path));

        if (bodyType is not null)
            request.Content = JsonContent.Create(body, bodyType, options: JsonOptions);

        var accessToken = auth == AuthRequirement.None ? null : _accessTokenSource?.Invoke();
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        else if (auth == AuthRequirement.Required)
            throw new InvalidOperationException($"{method} {path} requires an access token, but none is available.");

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        ErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // Not a Matrix error body, e.g. an HTML page from a reverse proxy
        }

        throw new MatrixException(response.StatusCode, error?.Errcode ?? MatrixErrorCodes.Unknown, error?.Error);
    }

    // Without a trailing slash, relative URIs would replace the last path segment
    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private record ErrorResponse(string Errcode, string? Error);
}