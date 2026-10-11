namespace TeamBanana.MatrixDotNet;

/// <summary>
/// What a <see cref="SyncLoop"/> is doing.
/// </summary>
public enum SyncLoopState
{
    /// <summary>The loop is not running: it has not been started, or it has ended.</summary>
    Stopped,

    /// <summary>The loop is running, and its last request succeeded or it has just started.</summary>
    Syncing,

    /// <summary>
    /// The last request failed with an error that may go away, such as a network failure, a server
    /// error, a rate limit or a locked account. The loop waits, then tries again.
    /// </summary>
    Retrying
}

/// <summary>
/// Data for <see cref="SyncLoop.StateChanged"/>.
/// </summary>
public class SyncLoopStateChangedEventArgs(SyncLoopState state, Exception? error = null, int attempt = 0,
    TimeSpan? retryDelay = null) : EventArgs
{
    /// <summary>The new state.</summary>
    public SyncLoopState State { get; } = state;

    /// <summary>
    /// For <see cref="SyncLoopState.Retrying"/>: why the last request failed, e.g. a
    /// <see cref="MatrixUserLockedException"/> while the account is locked. Otherwise
    /// <see langword="null"/>.
    /// </summary>
    public Exception? Error { get; } = error;

    /// <summary>
    /// For <see cref="SyncLoopState.Retrying"/>: how many requests in a row have failed, from 1.
    /// Otherwise 0.
    /// </summary>
    public int Attempt { get; } = attempt;

    /// <summary>
    /// For <see cref="SyncLoopState.Retrying"/>: how long the loop waits before trying again.
    /// Otherwise <see langword="null"/>.
    /// </summary>
    public TimeSpan? RetryDelay { get; } = retryDelay;
}
