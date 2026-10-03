namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Saves sessions that a <see cref="MatrixClient"/> refreshed itself. Set it through
/// <see cref="ClientOptions.SessionRefreshHandler"/>.
/// </summary>
/// <remarks>
/// The library calls it only after an automatic token refresh, never after login: the app saves
/// the session it got from <see cref="MatrixServer.LoginAsync"/> itself. One save function in the
/// app, called after login and from this handler, covers both.
/// </remarks>
public interface ISessionRefreshHandler
{
    /// <summary>
    /// Called after the library refreshed the session, before it uses the new tokens. Persist
    /// <paramref name="session"/> durably before the returned task completes.
    /// </summary>
    /// <remarks>
    /// The homeserver revokes the old refresh token once the new tokens are used, so a session
    /// that was not saved by then can no longer be restored. If this throws, the new tokens are
    /// discarded, the exception reaches the caller of the request that triggered the refresh,
    /// and the old refresh token stays usable for the next attempt.
    /// </remarks>
    /// <param name="session">The refreshed session, replacing the previous one.</param>
    /// <param name="cancellationToken">Cancelled with the request that triggered the refresh.</param>
    ValueTask OnSessionRefreshedAsync(MatrixSession session, CancellationToken cancellationToken);
}

/// <summary>
/// Stores nothing, so refreshed tokens are lost when the app exits. Use it only where that is
/// acceptable, e.g. in tests and one-off scripts.
/// </summary>
public sealed class DiscardingSessionRefreshHandler : ISessionRefreshHandler
{
    /// <inheritdoc/>
    public ValueTask OnSessionRefreshedAsync(MatrixSession session, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
