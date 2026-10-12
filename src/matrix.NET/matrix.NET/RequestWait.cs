namespace TeamBanana.MatrixDotNet;

/// <summary>
/// A wait in a call that has taken, or is about to take, longer than the threshold set by
/// <see cref="ClientOptions.WaitReportThreshold"/> or <see cref="ServerOptions.WaitReportThreshold"/>
/// (D38). Reported through the call's <see cref="IProgress{T}"/>, so the app can tell the user what
/// the call is waiting for, how long it expects to wait, and what cancelling would mean.
/// </summary>
/// <remarks>
/// A call may report several waits, e.g. a rate limit and then the response to the request it sends
/// again; the latest one describes the call. Nothing is reported when the call ends: the awaited task
/// completing says that.
/// </remarks>
public sealed class RequestWait
{
    internal RequestWait(RequestWaitReason reason, TimeSpan elapsed, TimeSpan? expectedWait, bool mayHaveTakenEffect)
    {
        Reason = reason;
        Elapsed = elapsed;
        ExpectedWait = expectedWait;
        MayHaveTakenEffect = mayHaveTakenEffect;
    }

    /// <summary>What the call is waiting for.</summary>
    public RequestWaitReason Reason { get; }

    /// <summary>How long the call has taken so far.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// For <see cref="RequestWaitReason.RateLimited"/>: how long until the request is sent again.
    /// <see langword="null"/> when the wait has no known end, as for
    /// <see cref="RequestWaitReason.AwaitingResponse"/>.
    /// </summary>
    public TimeSpan? ExpectedWait { get; }

    /// <summary>
    /// Whether the homeserver may already have carried out what the call asked for, so cancelling now
    /// leaves the outcome unknown. <see langword="false"/> while waiting out a rate limit, since the
    /// homeserver refused the request, and for requests that only read; cancelling then is clean.
    /// </summary>
    /// <remarks>
    /// After cancelling with <see langword="true"/>, check what happened before trying again, or send
    /// again with the same transaction ID where the endpoint takes one (D28).
    /// </remarks>
    public bool MayHaveTakenEffect { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Reason} after {Elapsed}" + (ExpectedWait is { } wait ? $", {wait} to go" : "") +
        (MayHaveTakenEffect ? ", may have taken effect" : "");
}

/// <summary>What a call reported by <see cref="RequestWait"/> is waiting for.</summary>
public enum RequestWaitReason
{
    /// <summary>
    /// The homeserver refused the request for rate limiting, and the library waits as asked before
    /// sending it again (D37). <see cref="RequestWait.ExpectedWait"/> says how long.
    /// </summary>
    RateLimited,

    /// <summary>
    /// A request has been sent, and the homeserver has not answered yet. How long it will take is
    /// unknown.
    /// </summary>
    AwaitingResponse
}
