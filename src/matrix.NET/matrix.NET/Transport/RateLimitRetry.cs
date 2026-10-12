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

    // Used when the homeserver does not say how long to wait: 1, 2 and 4 seconds
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);

    public static readonly RateLimitRetry Default = new(DefaultMaxRetries, DefaultMaxWait, Task.Delay);

    public RateLimitRetry(int maxRetries, TimeSpan maxWait, Func<TimeSpan, CancellationToken, Task> delay)
    {
        MaxRetries = maxRetries;
        MaxWait = maxWait;
        Delay = delay;
    }

    public int MaxRetries { get; }
    public TimeSpan MaxWait { get; }

    // Replaced by tests, so retries do not really wait
    public Func<TimeSpan, CancellationToken, Task> Delay { get; }

    /// <summary>
    /// How long to wait before retrying <paramref name="error"/>, or <see langword="null"/> to give up:
    /// it is not a rate limit, the retries are used up, or the homeserver asks for a longer wait than
    /// allowed, which is then the app's decision.
    /// </summary>
    public TimeSpan? GetWait(MatrixException error, int retriesSoFar)
    {
        var rateLimited = error.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                          error.ErrorCode == MatrixErrorCodes.LimitExceeded;
        if (!rateLimited || retriesSoFar >= MaxRetries)
            return null;

        var wait = error.RetryAfter ?? FirstWait * Math.Pow(2, retriesSoFar);
        return wait <= MaxWait ? wait : null;
    }

    /// <summary>Throws unless the options describe a valid policy.</summary>
    public static void Validate(int maxRetries, TimeSpan maxWait, string paramName)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(paramName, maxRetries, "MaxRateLimitRetries cannot be negative.");
        if (maxWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, maxWait, "MaxRateLimitWait cannot be negative.");
    }
}
