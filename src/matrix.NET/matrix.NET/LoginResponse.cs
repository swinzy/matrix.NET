namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Wire format of the <c>POST /login</c> response; callers receive a <see cref="MatrixSession"/>.
/// </summary>
internal class LoginResponse
{
    public required string UserId { get; set; }
    public required string AccessToken { get; set; }
    public required string DeviceId { get; set; }
    public string? RefreshToken { get; set; }
    public long? ExpiresInMs { get; set; }
}