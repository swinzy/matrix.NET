namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Waits the library chooses itself, when the homeserver does not say how long to wait: exponential,
/// with jitter so clients that failed together do not all try again at the same moment (D37, D34).
/// </summary>
internal static class Backoff
{
    /// <summary>The default source of randomness for the jitter, in [0, 1).</summary>
    public static readonly Func<double> SharedRandom = Random.Shared.NextDouble;

    /// <summary>
    /// <paramref name="first"/> doubled for each earlier attempt and capped at <paramref name="max"/>,
    /// then spread over its upper half by <paramref name="random"/>: never less than half, so a client
    /// never retries at once, and never more than the capped value.
    /// </summary>
    /// <param name="first">The wait before the first retry, before jitter.</param>
    /// <param name="attemptsSoFar">How many retries have already been made, from 0.</param>
    /// <param name="max">The longest wait, before jitter.</param>
    /// <param name="random">A value in [0, 1].</param>
    public static TimeSpan Next(TimeSpan first, int attemptsSoFar, TimeSpan max, double random)
    {
        // Capping the exponent keeps the arithmetic finite however long the failures go on
        var backoff = first * Math.Pow(2, Math.Min(attemptsSoFar, 16));
        if (backoff > max)
            backoff = max;
        return backoff * (0.5 + Math.Clamp(random, 0, 1) / 2);
    }
}
