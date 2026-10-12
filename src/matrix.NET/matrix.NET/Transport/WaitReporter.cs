namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// One public call's waits: reports them to its <see cref="IProgress{T}"/> once the call has taken, or
/// is about to take, longer than the threshold (D38), and adds up the rate-limit waits of a call that
/// cannot report, which are kept short (D37). Shared by every request the call sends, so both apply to
/// the whole call rather than each request.
/// </summary>
internal sealed class WaitReporter
{
    /// <summary>The default for <c>WaitReportThreshold</c> on the options types.</summary>
    internal static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(3);

    private readonly IProgress<RequestWait>? _progress;
    private readonly TimeSpan _threshold;
    private readonly TimeProvider _time;
    private readonly CallState _call;
    private readonly bool _effectsPossible;

    private WaitReporter(IProgress<RequestWait>? progress, TimeSpan threshold, TimeProvider time, CallState call,
        bool effectsPossible)
    {
        _progress = progress;
        _threshold = threshold;
        _time = time;
        _call = call;
        _effectsPossible = effectsPossible;
    }

    /// <summary>Starts a call, which reports its waits to <paramref name="progress"/> if there is one.</summary>
    public static WaitReporter Start(IProgress<RequestWait>? progress, TimeSpan threshold, TimeProvider time) =>
        new(progress, threshold, time, new CallState(time.GetTimestamp()), effectsPossible: true);

    /// <summary>Throws unless <paramref name="threshold"/> is zero or more, or infinite.</summary>
    public static void ValidateThreshold(TimeSpan threshold, string paramName)
    {
        if (threshold < TimeSpan.Zero && threshold != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(paramName, threshold,
                "WaitReportThreshold must be zero or more, or Timeout.InfiniteTimeSpan.");
    }

    /// <summary>
    /// Whether the app hears about this call's waits. If not, the library must not keep the call waiting
    /// long without the app knowing (D37).
    /// </summary>
    public bool IsReporting => _progress is not null && _threshold != Timeout.InfiniteTimeSpan;

    /// <summary>How long the call has waited out rate limits so far.</summary>
    public TimeSpan RateLimitWaited => _call.RateLimitWaited;

    private TimeSpan Elapsed => _time.GetElapsedTime(_call.Start);

    /// <summary>
    /// The same call, for requests that change nothing the call is for, such as a token refresh made
    /// before sending it again: their waits never make the call's outcome unknown.
    /// </summary>
    public WaitReporter WithoutEffects() => new(_progress, _threshold, _time, _call, effectsPossible: false);

    /// <summary>
    /// Records a rate-limit wait about to start, and reports it if it takes the call past the threshold.
    /// </summary>
    public void RateLimited(TimeSpan wait)
    {
        // A call's requests are sent one after another, so this is never updated concurrently
        _call.RateLimitWaited += wait;
        if (!IsReporting)
            return;

        var elapsed = Elapsed;
        if (elapsed + wait > _threshold)
            Report(new RequestWait(RequestWaitReason.RateLimited, elapsed, wait, mayHaveTakenEffect: false));
    }

    /// <summary>
    /// Starts waiting for a request's response, reported once the call passes the threshold, at once if
    /// it already has. Dispose of the result when the response arrives.
    /// </summary>
    /// <param name="mayTakeEffect">Whether the request can change anything, i.e. it does more than read.</param>
    public IDisposable? AwaitResponse(bool mayTakeEffect) =>
        IsReporting ? new Attempt(this, mayTakeEffect && _effectsPossible, _threshold - Elapsed) : null;

    private void Report(RequestWait wait)
    {
        try
        {
            _progress!.Report(wait);
        }
        catch
        {
            // The app's handler must not change the call's result, as with the library's events
        }
    }

    private sealed class CallState(long start)
    {
        public long Start { get; } = start;
        public TimeSpan RateLimitWaited { get; set; }
    }

    private sealed class Attempt : IDisposable
    {
        private readonly ITimer _timer;
        private volatile bool _done;

        public Attempt(WaitReporter owner, bool mayHaveTakenEffect, TimeSpan due)
        {
            _timer = owner._time.CreateTimer(_ =>
            {
                // A response may arrive while the timer fires; reporting it as still awaited is then harmless
                if (!_done)
                    owner.Report(new RequestWait(RequestWaitReason.AwaitingResponse, owner.Elapsed, null,
                        mayHaveTakenEffect));
            }, null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _done = true;
            _timer.Dispose();
        }
    }
}
