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

    /// <summary>A filter ID from <c>POST /user/{userId}/filter</c>, or a filter as JSON.</summary>
    public string? Filter { get; init; }
}
