namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The lifecycle state of a <see cref="MatrixClient"/>.
/// </summary>
public enum MatrixClientState
{
    /// <summary>The session is in use.</summary>
    Active,

    /// <summary>
    /// An administrator locked the account. The session stays valid and the client stays usable,
    /// so requests can detect the unlock; the first successful authenticated request returns the
    /// client to <see cref="Active"/>.
    /// </summary>
    Locked,

    /// <summary>
    /// <see cref="MatrixClient.LogoutAsync"/> succeeded. The client is unusable; only
    /// <see cref="MatrixClient.UserId"/> and <see cref="MatrixClient.DeviceId"/> remain readable.
    /// </summary>
    LoggedOut,

    /// <summary>
    /// The homeserver rejected the access token and it could not be refreshed. The client is
    /// unusable; log in again and create a new client.
    /// </summary>
    Invalidated
}