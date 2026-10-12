using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Options for a <see cref="MatrixServer"/>.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Whether compressed responses (GZip, Deflate, Brotli) are requested and decompressed
    /// automatically. Turn it off only if you need the raw encoded bytes, e.g. when forwarding
    /// responses unchanged. Only applies when the server uses the library's shared
    /// <see cref="HttpClient"/>; a caller-supplied client keeps its own settings. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AutomaticDecompression { get; init; } = true;

    /// <summary>
    /// How long the homeserver's <c>GET /versions</c> is cached before the next call that needs it fetches
    /// it again (D25). Defaults to one day. <see cref="TimeSpan.Zero"/> fetches it every time;
    /// <see cref="Timeout.InfiniteTimeSpan"/> keeps it until <see cref="MatrixServer.RefreshVersionsAsync"/>
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
    /// so the app decides. Defaults to one minute. A call that cannot report its waits is also held to
    /// <see cref="MaxUnreportedRateLimitWait"/>.
    /// </summary>
    public TimeSpan MaxRateLimitWait { get; init; } = RateLimitRetry.DefaultMaxWait;

    /// <summary>
    /// The longest a call that cannot report its waits, because the app passed no
    /// <see cref="IProgress{T}"/> or turned reporting off, may wait out rate limits in total (D37). A wait
    /// that would go past it is not taken: the exception is thrown at once with
    /// <see cref="MatrixException.RetryAfter"/>, so the user is never kept waiting unawares. Defaults to
    /// ten seconds.
    /// </summary>
    public TimeSpan MaxUnreportedRateLimitWait { get; init; } = RateLimitRetry.DefaultMaxUnreportedWait;

    /// <summary>
    /// How long a call may take before its waits are reported to the <see cref="IProgress{T}"/> passed to
    /// it, so the app can tell the user what it is waiting for and offer to cancel (D38). Defaults to three
    /// seconds; a wait expected to take the call past it, such as a rate limit, is reported before it
    /// starts. <see cref="Timeout.InfiniteTimeSpan"/> turns reporting off.
    /// </summary>
    public TimeSpan WaitReportThreshold { get; init; } = WaitReporter.DefaultThreshold;

    // Replaced by tests, to expire the cache and time calls without waiting
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    // Replaced by tests, so rate-limit retries do not really wait
    internal Func<TimeSpan, CancellationToken, Task> RateLimitDelay { get; init; } = Task.Delay;

    // Replaced by tests, so the backoff's jitter is predictable
    internal Func<double> BackoffRandom { get; init; } = Backoff.SharedRandom;

    internal RateLimitRetry CreateRateLimitRetry() =>
        new(MaxRateLimitRetries, MaxRateLimitWait, MaxUnreportedRateLimitWait, RateLimitDelay, BackoffRandom);
}
