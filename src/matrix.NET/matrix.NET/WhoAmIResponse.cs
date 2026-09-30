namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The owner of an access token, as reported by the homeserver.
/// </summary>
public class WhoAmIResponse
{
    /// <summary>The user ID that owns the access token.</summary>
    public required string UserId { get; set; }

    /// <summary>
    /// The device associated with the access token, or <see langword="null"/> if there is none,
    /// e.g. for application services.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>Whether the user is a guest.</summary>
    public bool IsGuest { get; set; }
}