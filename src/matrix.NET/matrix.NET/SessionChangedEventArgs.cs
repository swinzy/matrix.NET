namespace TeamBanana.MatrixDotNet;

/// <summary>
/// What happened to a <see cref="MatrixClient"/>'s session.
/// </summary>
public enum SessionChangeKind
{
    /// <summary>
    /// The app logged out through <see cref="MatrixClient.LogoutAsync"/>. Components that only care
    /// about sessions ending unexpectedly can ignore it.
    /// </summary>
    LoggedOut,

    /// <summary>
    /// The homeserver rejected the access token and it could not be refreshed. See
    /// <see cref="SessionChangedEventArgs.SoftLogout"/> for whether local data may be reused.
    /// </summary>
    Invalidated,

    /// <summary>An administrator locked the account. The session stays valid; hide the normal UI.</summary>
    Locked,

    /// <summary>The account is no longer locked; restore the normal UI.</summary>
    Unlocked,

    /// <summary>
    /// The library refreshed the access token. <see cref="MatrixClient.Session"/> holds the new
    /// session, which <see cref="ClientOptions.SessionRefreshHandler"/> has already saved.
    /// </summary>
    TokensRefreshed
}

/// <summary>
/// Data for <see cref="MatrixClient.SessionChanged"/>.
/// </summary>
public class SessionChangedEventArgs(SessionChangeKind kind, bool softLogout = false) : EventArgs
{
    /// <summary>What happened.</summary>
    public SessionChangeKind Kind { get; } = kind;

    /// <summary>
    /// For <see cref="SessionChangeKind.Invalidated"/>: <see langword="true"/> if local data such as
    /// encryption keys may be reused by logging in again with the same device ID;
    /// <see langword="false"/> if it must be discarded. Always <see langword="false"/> for other kinds.
    /// </summary>
    public bool SoftLogout { get; } = softLogout;
}