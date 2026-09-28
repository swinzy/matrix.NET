namespace TeamBanana.MatrixDotNet;

public class LoginResponse
{
    public required string UserId { get; set; }
    public required string AccessToken { get; set; }
    public required string DeviceId { get; set; }
    public string? RefreshToken { get; set; }
    public long? ExpiresInMs { get; set; }
}