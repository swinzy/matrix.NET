using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Runs against a real homeserver; skipped unless <see cref="TestSettings"/> is configured.
/// </summary>
public class LoginIntegrationTests
{
    private readonly TestSettings _settings = TestSettings.Load();

    private MatrixServer CreateServer()
    {
        Assert.SkipUnless(_settings.IsConfigured,
            "Real homeserver not configured; see testsettings.example.json");
        return new MatrixServer(new Uri(_settings.Homeserver!));
    }

    [Fact]
    public async Task LoginAsync_WithValidPassword_ReturnsAccessToken()
    {
        var server = CreateServer();

        var response = await server.LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = _settings.User! },
            Password = _settings.Password,
            DeviceId = _settings.DeviceId,
            InitialDeviceDisplayName = "matrix.NET integration tests"
        }, TestContext.Current.CancellationToken);

        Assert.StartsWith("@", response.UserId);
        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.DeviceId);
        if (_settings.DeviceId is not null)
            Assert.Equal(_settings.DeviceId, response.DeviceId);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsForbidden()
    {
        var server = CreateServer();

        var exception = await Assert.ThrowsAsync<MatrixException>(() => server.LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = _settings.User! },
            Password = _settings.Password + "-wrong"
        }, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("M_FORBIDDEN", exception.ErrorCode);
    }

    [Fact]
    public async Task GetSupportedLoginTypesAsync_IncludesPasswordLogin()
    {
        var server = CreateServer();

        var flows = await server.GetSupportedLoginTypesAsync(TestContext.Current.CancellationToken);

        Assert.Contains(flows, flow => flow.Type == "m.login.password");
    }
}