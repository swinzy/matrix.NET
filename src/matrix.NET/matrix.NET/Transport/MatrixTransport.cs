using System.Net;
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

    /// <summary>
    /// Applied to each request unless overridden; matches <see cref="HttpClient.Timeout"/>'s default.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    // Shared across the library. Timeouts are applied per request instead, so long-polling
    // requests can use their own
    internal static readonly HttpClient SharedClient = CreateSharedClient(automaticDecompression: true);

    // Only created if someone opts out of decompression
    private static readonly Lazy<HttpClient> SharedClientWithoutDecompression =
        new(() => CreateSharedClient(automaticDecompression: false));

    internal static HttpClient GetSharedClient(bool automaticDecompression) =>
        automaticDecompression ? SharedClient : SharedClientWithoutDecompression.Value;

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

    /// <summary>
    /// Sends a request and deserialises the response. <paramref name="timeout"/> overrides
    /// <see cref="DefaultTimeout"/>, and <see cref="Timeout.InfiniteTimeSpan"/> disables it. A timeout
    /// throws <see cref="TaskCanceledException"/> with an inner <see cref="TimeoutException"/>.
    /// </summary>
    public Task<TResponse> SendAsync<TResponse>(Endpoint endpoint, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null) =>
        SendCoreAsync<TResponse>(endpoint, null, null, timeout, cancellationToken);

    /// <inheritdoc cref="SendAsync{TResponse}"/>
    public Task<TResponse> SendAsync<TRequest, TResponse>(Endpoint endpoint, TRequest body,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null) =>
        SendCoreAsync<TResponse>(endpoint, body, typeof(TRequest), timeout, cancellationToken);

    private async Task<TResponse> SendCoreAsync<TResponse>(Endpoint endpoint, object? body, Type? bodyType,
        TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        try
        {
            // Requests are built from their parts rather than reused, so they can be rebuilt
            // when retrying after a token refresh
            using var request = CreateRequest(endpoint, body, bodyType);
            using var response = await _clientSource().SendAsync(request, timeoutSource.Token);
            await EnsureSuccessAsync(response, request.Headers.Authorization?.Parameter, timeoutSource.Token);

            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, timeoutSource.Token)
                   ?? throw new JsonException($"The homeserver returned an empty {typeof(TResponse).Name}.");
        }
        catch (OperationCanceledException e) when (timeoutSource.IsCancellationRequested &&
                                                   !cancellationToken.IsCancellationRequested)
        {
            // Same shape as HttpClient's own timeout, so existing handling keeps working
            throw new TaskCanceledException($"{endpoint} timed out after {effectiveTimeout.TotalSeconds} seconds.",
                new TimeoutException(e.Message, e));
        }
    }

    private HttpRequestMessage CreateRequest(Endpoint endpoint, object? body, Type? bodyType)
    {
        var request = new HttpRequestMessage(endpoint.Method, new Uri(Homeserver, endpoint.Path));

        if (bodyType is not null)
            request.Content = JsonContent.Create(body, bodyType, options: JsonOptions);

        var accessToken = endpoint.Auth == AuthRequirement.None ? null : _accessTokenSource?.Invoke();
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        else if (endpoint.Auth == AuthRequirement.Required)
            throw new InvalidOperationException($"{endpoint} requires an access token, but none is available.");

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string? sentAccessToken,
        CancellationToken cancellationToken)
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

        var errorCode = error?.Errcode ?? MatrixErrorCodes.Unknown;
        var softLogout = error?.SoftLogout ?? false;
        throw errorCode switch
        {
            MatrixErrorCodes.UnknownToken => new MatrixUnknownTokenException(response.StatusCode, error?.Error, softLogout)
            {
                RejectedAccessToken = sentAccessToken
            },
            MatrixErrorCodes.UserLocked => new MatrixUserLockedException(response.StatusCode, error?.Error, softLogout),
            _ => new MatrixException(response.StatusCode, errorCode, error?.Error)
        };
    }

    private static HttpClient CreateSharedClient(bool automaticDecompression) =>
        new(CreateSharedHandler(automaticDecompression)) { Timeout = Timeout.InfiniteTimeSpan };

    internal static SocketsHttpHandler CreateSharedHandler(bool automaticDecompression) => new()
    {
        // Recycle pooled connections so DNS changes are picked up
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        // One cookie container would be shared by every account and homeserver; the Matrix API
        // does not use cookies, but reverse proxies may set them
        UseCookies = false,
        AutomaticDecompression = automaticDecompression ? DecompressionMethods.All : DecompressionMethods.None
    };

    // Without a trailing slash, relative URIs would replace the last path segment
    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private record ErrorResponse(string Errcode, string? Error, bool? SoftLogout);
}