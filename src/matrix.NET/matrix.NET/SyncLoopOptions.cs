namespace TeamBanana.MatrixDotNet;

/// <summary>Options for a <see cref="SyncLoop"/>; all are optional (D34).</summary>
public sealed class SyncLoopOptions
{
    /// <summary>
    /// Where to continue from: the <see cref="SyncResponse.NextBatch"/> the app saved last time. Omit
    /// it to start with an initial sync.
    /// </summary>
    public string? Since { get; init; }

    /// <summary>
    /// How long the homeserver may hold each request while waiting for new events. Defaults to 30
    /// seconds. An initial sync always answers at once.
    /// </summary>
    public TimeSpan Timeout { get; init; } = SyncLoop.DefaultTimeout;

    /// <summary>
    /// How long the loop waits for the initial sync before giving up on that request and trying again.
    /// Defaults to ten minutes: a homeserver can take minutes to build the initial sync of a large
    /// account, and answers at once when it is done. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
    /// waits as long as it takes; only use it if the user can cancel, because a connection that drops
    /// without notice would then leave the request waiting for ever.
    /// </summary>
    /// <remarks>
    /// While it runs, the loop is <see cref="SyncLoopState.InitialSyncing"/>, so the app can tell the user.
    /// </remarks>
    public TimeSpan InitialSyncTimeout { get; init; } = SyncLoop.DefaultInitialSyncTimeout;

    /// <summary>A filter ID from <c>POST /user/{userId}/filter</c>, or a filter as JSON.</summary>
    public string? Filter { get; init; }
}
