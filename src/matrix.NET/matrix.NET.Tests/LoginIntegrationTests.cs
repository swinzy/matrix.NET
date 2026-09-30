using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Runs against a real homeserver; skipped unless <see cref="TestSettings"/> is configured.
/// </summary>
public class LoginIntegrationTests(LoggedInClientFixture fixture)
{
    private TestSettings Settings => fixture.Settings;

    private MatrixServer CreateServer()
    {
        Assert.SkipUnless(Settings.IsConfigured,
            "Real homeserver not configured; see testsettings.example.json");
        return new MatrixServer(new Uri(Settings.Homeserver!));
    }

    // Checks the run's shared login rather than logging in again, to stay within rate limits
    [Fact]
    public async Task LoginAsync_WithValidPassword_ReturnsSession()
    {
        var session = (await fixture.GetClientAsync()).Session;

        Assert.StartsWith("@", session.UserId);
        Assert.NotEmpty(session.AccessToken);
        Assert.NotEmpty(session.DeviceId);
        Assert.Equal(new Uri(Settings.Homeserver!), session.Homeserver);
        if (Settings.DeviceId is not null)
            Assert.Equal(Settings.DeviceId, session.DeviceId);
    }

    // Explicit: a failed login counts towards the account's rate limit, and this behaviour is
    // unlikely to change with our code. Run with: dotnet test --explicit only
    [Fact(Explicit = true)]
    public async Task LoginAsync_WithWrongPassword_ThrowsForbidden()
    {
        var server = CreateServer();

        var exception = await Assert.ThrowsAsync<MatrixException>(() => server.LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = Settings.User! },
            Password = Settings.Password + "-wrong"
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