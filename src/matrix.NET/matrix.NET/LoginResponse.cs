namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Wire format of the <c>POST /login</c> response; callers receive a <see cref="LoginResult"/>.
/// </summary>
internal class LoginResponse
{
    public required string UserId { get; set; }
    public required string AccessToken { get; set; }
    public required string DeviceId { get; set; }
    public string? RefreshToken { get; set; }
    public long? ExpiresInMs { get; set; }

    // Read leniently into DiscoveryInformation, so a malformed one cannot fail a successful login
    public System.Text.Json.JsonElement? WellKnown { get; set; }
}