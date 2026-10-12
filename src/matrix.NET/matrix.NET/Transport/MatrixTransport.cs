using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// The single path every request to a homeserver goes through: URI building, JSON,
/// authentication and error mapping.
/// </summary>
internal sealed class MatrixTransport
{
    // Resolves only the source-generated library types, never through reflection (D29)
    internal static JsonSerializerOptions JsonOptions => MatrixJsonContext.Instance.Options;

    internal static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));

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
    private readonly Func<VersionsResponse?>? _cachedVersions;
    private readonly RateLimitRetry _rateLimitRetry;
    private readonly TimeSpan _waitReportThreshold;
    private readonly TimeProvider _time;

    /// <param name="homeserver">Base URL of the homeserver; may include a path.</param>
    /// <param name="clientSource">
    /// Called once per request. Defaults to a library-wide shared client.
    /// </param>
    /// <param name="accessTokenSource">
    /// Called once per request that may carry a token. Null for unauthenticated use.
    /// </param>
    /// <param name="cachedVersions">
    /// Returns the cached <c>/versions</c>, if any, to explain why an endpoint seems to be missing.
    /// Never fetches (D26).
    /// </param>
    /// <param name="rateLimitRetry">How rate-limited requests are retried; the defaults when omitted (D37).</param>
    /// <param name="waitReportThreshold">
    /// How long a call may take before its waits are reported (D38); the default when omitted.
    /// </param>
    /// <param name="time">Times calls for reporting their waits; the system clock when omitted.</param>
    public MatrixTransport(Uri homeserver, Func<HttpClient>? clientSource = null,
        Func<string?>? accessTokenSource = null, Func<VersionsResponse?>? cachedVersions = null,
        RateLimitRetry? rateLimitRetry = null, TimeSpan? waitReportThreshold = null, TimeProvider? time = null)
    {
        Homeserver = WithTrailingSlash(homeserver);
        _clientSource = clientSource ?? (() => SharedClient);
        _accessTokenSource = accessTokenSource;
        _cachedVersions = cachedVersions;
        _rateLimitRetry = rateLimitRetry ?? RateLimitRetry.Default;
        _waitReportThreshold = waitReportThreshold ?? WaitReporter.DefaultThreshold;
        _time = time ?? TimeProvider.System;
    }

    public Uri Homeserver { get; }

    /// <summary>
    /// Starts a public call, which reports its waits to <paramref name="progress"/> if there is one (D38).
    /// Pass the result to every request the call sends.
    /// </summary>
    public WaitReporter StartCall(IProgress<RequestWait>? progress) =>
        WaitReporter.Start(progress, _waitReportThreshold, _time);

    /// <summary>
    /// Sends a request and deserialises the response, retrying it if the homeserver refuses it for rate
    /// limiting (D37). <paramref name="timeout"/> applies to each attempt, overrides
    /// <see cref="DefaultTimeout"/>, and <see cref="Timeout.InfiniteTimeSpan"/> disables it. A timeout
    /// throws <see cref="TaskCanceledException"/> with an inner <see cref="TimeoutException"/>.
    /// <paramref name="waits"/>, from <see cref="StartCall"/>, is told about the waits (D38); without it,
    /// the request is a call of its own that reports nothing. <paramref name="retryRateLimited"/> turns the
    /// retrying off for callers that retry themselves, so waits are not taken in two layers (D34).
    /// </summary>
    public Task<TResponse> SendAsync<TResponse>(Endpoint endpoint, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null, WaitReporter? waits = null, bool retryRateLimited = true) =>
        SendCoreAsync<TResponse>(endpoint, null, null, timeout, waits, retryRateLimited, cancellationToken);

    /// <inheritdoc cref="SendAsync{TResponse}"/>
    public Task<TResponse> SendAsync<TRequest, TResponse>(Endpoint endpoint, TRequest body,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null, WaitReporter? waits = null) =>
        SendCoreAsync<TResponse>(endpoint, body, typeof(TRequest), timeout, waits, true, cancellationToken);

    private async Task<TResponse> SendCoreAsync<TResponse>(Endpoint endpoint, object? body, Type? bodyType,
        TimeSpan? timeout, WaitReporter? waits, bool retryRateLimited, CancellationToken cancellationToken)
    {
        waits ??= StartCall(null);
        for (var retries = 0;; retries++)
        {
            try
            {
                return await SendOnceAsync<TResponse>(endpoint, body, bodyType, timeout, waits, cancellationToken);
            }
            catch (MatrixException e) when (retryRateLimited && _rateLimitRetry.GetWait(e, retries, waits) is { } wait)
            {
                waits.RateLimited(wait);
                await _rateLimitRetry.Delay(wait, cancellationToken);
            }
        }
    }

    private async Task<TResponse> SendOnceAsync<TResponse>(Endpoint endpoint, object? body, Type? bodyType,
        TimeSpan? timeout, WaitReporter waits, CancellationToken cancellationToken)
    {
        // Only a request that does more than read can leave a cancelled call's outcome unknown
        using var awaiting = waits.AwaitResponse(mayTakeEffect: endpoint.Method != HttpMethod.Get);
        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        try
        {
            // Requests are built from their parts rather than reused, so they can be rebuilt
            // when retrying after a rate limit or a token refresh
            using var request = CreateRequest(endpoint, body, bodyType);
            using var response = await _clientSource().SendAsync(request, timeoutSource.Token);
            await EnsureSuccessAsync(endpoint, response, request.Headers.Authorization?.Parameter, timeoutSource.Token);

            return await response.Content.ReadFromJsonAsync(TypeInfo<TResponse>(), timeoutSource.Token)
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
        if (endpoint.HasParameters)
            throw new InvalidOperationException($"{endpoint} has path parameters that were not bound.");

        var request = new HttpRequestMessage(endpoint.Method, new Uri(Homeserver, endpoint.PathAndQuery));

        if (bodyType is not null)
            request.Content = JsonContent.Create(body, JsonOptions.GetTypeInfo(bodyType));

        var accessToken = endpoint.Auth == AuthRequirement.None ? null : _accessTokenSource?.Invoke();
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        else if (endpoint.Auth == AuthRequirement.Required)
            throw new InvalidOperationException($"{endpoint} requires an access token, but none is available.");

        return request;
    }

    private async Task EnsureSuccessAsync(Endpoint endpoint, HttpResponseMessage response, string? sentAccessToken,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        ErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync(TypeInfo<ErrorResponse>(), cancellationToken);
        }
        catch (JsonException)
        {
            // Not a Matrix error body, e.g. an HTML page from a reverse proxy
        }

        var errorCode = error?.Errcode ?? MatrixErrorCodes.Unknown;
        var softLogout = error?.SoftLogout ?? false;
        var retryAfter = GetRetryAfter(response, error);
        throw errorCode switch
        {
            MatrixErrorCodes.UnknownToken => new MatrixUnknownTokenException(response.StatusCode, error?.Error, softLogout)
            {
                RejectedAccessToken = sentAccessToken,
                RetryAfter = retryAfter
            },
            MatrixErrorCodes.UserLocked => new MatrixUserLockedException(response.StatusCode, error?.Error, softLogout)
            {
                RetryAfter = retryAfter
            },
            _ when IsMissingEndpoint(endpoint, response.StatusCode, errorCode) =>
                new MatrixException(response.StatusCode, errorCode, error?.Error,
                    ExplainMissing(endpoint.Feature!, error?.Error)) { RetryAfter = retryAfter },
            _ => new MatrixException(response.StatusCode, errorCode, error?.Error) { RetryAfter = retryAfter }
        };
    }

    // How the spec says a homeserver answers an endpoint it does not know. Only endpoints added after
    // the minimum version can be missing for that reason, so only they get an explanation (D26)
    private static bool IsMissingEndpoint(Endpoint endpoint, HttpStatusCode statusCode, string errorCode) =>
        endpoint.Feature is not null && errorCode == MatrixErrorCodes.Unrecognized &&
        statusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed;

    private string ExplainMissing(MatrixFeature feature, string? serverMessage)
    {
        var message = feature.ExplainMissing(_cachedVersions?.Invoke());
        return serverMessage is null ? message : $"{message} Homeserver: {serverMessage}";
    }

    // The header is the current way (spec v1.10); retry_after_ms is the deprecated one older
    // homeservers send instead. A header that does not parse is ignored rather than failing the error
    internal static TimeSpan? GetRetryAfter(HttpResponseMessage response, ErrorResponse? error)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return ClampRetryAfter(delta.TotalMilliseconds);
        if (header?.Date is { } date)
            return ClampRetryAfter((date - DateTimeOffset.UtcNow).TotalMilliseconds);
        if (error?.RetryAfterMs is { } milliseconds)
            return ClampRetryAfter(milliseconds);

        return null;
    }

    // A date in the past means "now"; an absurdly large value must not overflow TimeSpan
    private static TimeSpan ClampRetryAfter(double milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0, TimeSpan.MaxValue.TotalMilliseconds - 1));

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

    // RetryAfterMs is a double so a fractional value from a lenient homeserver does not discard the
    // whole error body
    internal record ErrorResponse(string Errcode, string? Error, bool? SoftLogout, double? RetryAfterMs = null);
}