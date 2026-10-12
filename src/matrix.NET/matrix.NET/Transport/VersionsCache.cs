namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// One instance's cached <c>GET /versions</c> (D25): fetched on first need, kept for a lifetime the app
/// chooses, and never refreshed in the background.
/// </summary>
internal sealed class VersionsCache
{
    /// <summary>The default for <see cref="ServerOptions.VersionsCacheLifetime"/> and <see cref="ClientOptions.VersionsCacheLifetime"/>.</summary>
    internal static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(1);

    private readonly Func<WaitReporter?, CancellationToken, Task<VersionsResponse>> _fetch;
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _time;

    // Concurrent callers wait for one request instead of each sending their own, as token refresh does
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile Entry? _entry;

    public VersionsCache(Func<WaitReporter?, CancellationToken, Task<VersionsResponse>> fetch, TimeSpan lifetime, TimeProvider time)
    {
        _fetch = fetch;
        _lifetime = lifetime;
        _time = time;
    }

    /// <summary>
    /// The last response fetched, even if expired, for explaining errors without another request;
    /// <see langword="null"/> if none has been fetched yet.
    /// </summary>
    public VersionsResponse? Cached => _entry?.Response;

    /// <summary>Returns the cached response, fetching it first if there is none or it has expired.</summary>
    public async Task<VersionsResponse> GetAsync(WaitReporter? waits, CancellationToken cancellationToken)
    {
        if (Fresh() is { } cached)
            return cached;

        await WaitForLockAsync(waits, cancellationToken);
        try
        {
            // Another caller may have fetched it while this one waited
            return Fresh() ?? await FetchAsync(waits, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Fetches the response now, whatever the cache holds, and caches it.</summary>
    public async Task<VersionsResponse> RefreshAsync(WaitReporter? waits, CancellationToken cancellationToken)
    {
        await WaitForLockAsync(waits, cancellationToken);
        try
        {
            return await FetchAsync(waits, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Throws unless <paramref name="lifetime"/> is zero or more, or infinite.</summary>
    public static void ValidateLifetime(TimeSpan lifetime, string paramName)
    {
        if (lifetime < TimeSpan.Zero && lifetime != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(paramName, lifetime,
                "The /versions cache lifetime must be zero or more, or Timeout.InfiniteTimeSpan.");
    }

    // A failure leaves the cache as it was, so the next call tries again
    private async Task<VersionsResponse> FetchAsync(WaitReporter? waits, CancellationToken cancellationToken)
    {
        var response = await _fetch(waits, cancellationToken);
        _entry = new Entry(response, _time.GetUtcNow());
        return response;
    }

    // Waiting for another caller's request is waiting for a response too, though not this call's own
    private async Task WaitForLockAsync(WaitReporter? waits, CancellationToken cancellationToken)
    {
        if (_lock.Wait(0, CancellationToken.None))
            return;

        using (waits?.AwaitResponse(mayTakeEffect: false))
            await _lock.WaitAsync(cancellationToken);
    }

    private VersionsResponse? Fresh()
    {
        var entry = _entry;
        if (entry is null)
            return null;
        if (_lifetime == Timeout.InfiniteTimeSpan || _time.GetUtcNow() - entry.FetchedAt < _lifetime)
            return entry.Response;

        return null;
    }

    private sealed record Entry(VersionsResponse Response, DateTimeOffset FetchedAt);
}
