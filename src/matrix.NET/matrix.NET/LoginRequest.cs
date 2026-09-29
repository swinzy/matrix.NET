namespace TeamBanana.MatrixDotNet;

public class LoginRequest
{
    public required string Type { get; set; }
    public IIdentifier? Identifier { get; set; }
    public string? Password { get; set; }
    public string? Token { get; set; }
    public string? DeviceId { get; set; }
    public string? InitialDeviceDisplayName { get; set; }
    public bool? RefreshToken { get; set; } = true;
}