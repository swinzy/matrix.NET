namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// How the transport retries requests the homeserver refused for rate limiting (D37). A refused
/// request was not executed, so retrying it is safe for any endpoint.
/// </summary>
internal sealed class RateLimitRetry
{
    /// <summary>The default for <c>MaxRateLimitRetries</c> on the options types.</summary>
    internal const int DefaultMaxRetries = 3;

    /// <summary>The default for <c>MaxRateLimitWait</c> on the options types.</summary>
    internal static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(1);

    /// <summary>The default for <c>MaxUnreportedRateLimitWait</c> on the options types.</summary>
    internal static readonly TimeSpan DefaultMaxUnreportedWait = TimeSpan.FromSeconds(10);

    // Used when the homeserver does not say how long to wait: about 1, 2 and 4 seconds, with jitter
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);

    public static readonly RateLimitRetry Default =
        new(DefaultMaxRetries, DefaultMaxWait, DefaultMaxUnreportedWait, Task.Delay, Backoff.SharedRandom);

    public RateLimitRetry(int maxRetries, TimeSpan maxWait, TimeSpan maxUnreportedWait,
        Func<TimeSpan, CancellationToken, Task> delay, Func<double> random)
    {
        MaxRetries = maxRetries;
        MaxWait = maxWait;
        MaxUnreportedWait = maxUnreportedWait;
        Delay = delay;
        Random = random;
    }

    public int MaxRetries { get; }
    public TimeSpan MaxWait { get; }
    public TimeSpan MaxUnreportedWait { get; }

    // Replaced by tests, so retries do not really wait
    public Func<TimeSpan, CancellationToken, Task> Delay { get; }

    // Replaced by tests, so the jitter is predictable
    public Func<double> Random { get; }

    /// <summary>
    /// How long to wait before retrying <paramref name="error"/>, or <see langword="null"/> to give up,
    /// leaving the decision to the app: it is not a rate limit, the retries are used up, the homeserver
    /// asks for longer than <see cref="MaxWait"/>, or the call cannot report its waits (D38) and this one
    /// would take its rate-limit waits past <see cref="MaxUnreportedWait"/>.
    /// </summary>
    public TimeSpan? GetWait(MatrixException error, int retriesSoFar, WaitReporter waits)
    {
        var rateLimited = error.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                          error.ErrorCode == MatrixErrorCodes.LimitExceeded;
        if (!rateLimited || retriesSoFar >= MaxRetries)
            return null;

        // The homeserver's wait is kept as asked: it says how long it needs, not when others retry
        var wait = error.RetryAfter ?? Backoff.Next(FirstWait, retriesSoFar, MaxWait, Random());
        if (wait > MaxWait)
            return null;
        if (!waits.IsReporting && waits.RateLimitWaited + wait > MaxUnreportedWait)
            return null;
        return wait;
    }

    /// <summary>Throws unless the options describe a valid policy.</summary>
    public static void Validate(int maxRetries, TimeSpan maxWait, TimeSpan maxUnreportedWait, string paramName)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(paramName, maxRetries, "MaxRateLimitRetries cannot be negative.");
        if (maxWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, maxWait, "MaxRateLimitWait cannot be negative.");
        if (maxUnreportedWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, maxUnreportedWait,
                "MaxUnreportedRateLimitWait cannot be negative.");
    }
}
