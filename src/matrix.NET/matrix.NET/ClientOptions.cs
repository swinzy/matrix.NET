using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Options for a <see cref="MatrixClient"/>.
/// </summary>
public sealed class ClientOptions
{
    /// <summary>
    /// Whether the library refreshes the access token itself when the homeserver rejects it and
    /// the session has a refresh token. Turn it off to manage refresh in the app. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AutoRefreshToken { get; init; } = true;

    /// <summary>
    /// Saves sessions the library refreshed, before their tokens are used. Required when
    /// <see cref="AutoRefreshToken"/> is on and the session has a refresh token; use
    /// <see cref="DiscardingSessionRefreshHandler"/> if losing refreshed tokens on restart is
    /// acceptable.
    /// </summary>
    public ISessionRefreshHandler? SessionRefreshHandler { get; init; }

    /// <summary>
    /// Whether compressed responses (GZip, Deflate, Brotli) are requested and decompressed
    /// automatically. Only applies when the client uses the library's shared
    /// <see cref="HttpClient"/>; a caller-supplied client keeps its own settings. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AutomaticDecompression { get; init; } = true;

    /// <summary>
    /// How long the homeserver's <c>GET /versions</c> is cached before the next call that needs it fetches
    /// it again (D25). Defaults to one day. <see cref="TimeSpan.Zero"/> fetches it every time;
    /// <see cref="Timeout.InfiniteTimeSpan"/> keeps it until <see cref="MatrixClient.RefreshVersionsAsync"/>
    /// is called, so the app decides when to pay for the request.
    /// </summary>
    public TimeSpan VersionsCacheLifetime { get; init; } = VersionsCache.DefaultLifetime;

    /// <summary>
    /// How many times a request the homeserver refused for rate limiting (429, <c>M_LIMIT_EXCEEDED</c>) is
    /// retried before its <see cref="MatrixException"/> is thrown (D37). Defaults to 3; 0 turns retrying
    /// off. A refused request was not executed, so retrying is safe for every endpoint.
    /// </summary>
    public int MaxRateLimitRetries { get; init; } = RateLimitRetry.DefaultMaxRetries;

    /// <summary>
    /// The longest the library waits before a retry (D37). If the homeserver asks for a longer wait,
    /// the exception is thrown at once, with <see cref="MatrixException.RetryAfter"/> saying how long,
    /// so the app decides. Defaults to one minute.
    /// </summary>
    public TimeSpan MaxRateLimitWait { get; init; } = RateLimitRetry.DefaultMaxWait;

    // Replaced by tests, to expire the cache without waiting
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    // Replaced by tests, so rate-limit retries do not really wait
    internal Func<TimeSpan, CancellationToken, Task> RateLimitDelay { get; init; } = Task.Delay;

    internal RateLimitRetry CreateRateLimitRetry() => new(MaxRateLimitRetries, MaxRateLimitWait, RateLimitDelay);
}
