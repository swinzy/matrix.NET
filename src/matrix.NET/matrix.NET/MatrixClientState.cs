namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The lifecycle state of a <see cref="MatrixClient"/>.
/// </summary>
public enum MatrixClientState
{
    /// <summary>The session is in use.</summary>
    Active,

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