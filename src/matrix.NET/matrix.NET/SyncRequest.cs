namespace TeamBanana.MatrixDotNet;

/// <summary>Parameters of <see cref="MatrixClient.SyncAsync"/>; all are optional (D32).</summary>
public sealed class SyncRequest
{
    /// <summary>
    /// The <see cref="SyncResponse.NextBatch"/> of the previous sync, to get only what happened since.
    /// Omit it for an initial sync.
    /// </summary>
    public string? Since { get; init; }

    /// <summary>
    /// How long the homeserver may wait for new events before answering with none. Omit it, or use
    /// <see cref="TimeSpan.Zero"/>, to answer at once. The request itself is allowed this long plus a
    /// margin before it times out locally.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>A filter ID from <c>POST /user/{userId}/filter</c>, or a filter as JSON.</summary>
    public string? Filter { get; init; }

    /// <summary>Whether to return the full state of every room, even with <see cref="Since"/>.</summary>
    public bool FullState { get; init; }
}
