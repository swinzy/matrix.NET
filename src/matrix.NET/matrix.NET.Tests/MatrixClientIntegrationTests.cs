namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Uses the shared logged-in session through <see cref="MatrixClient"/>; skipped unless
/// <see cref="TestSettings"/> is configured.
/// </summary>
public class MatrixClientIntegrationTests(LoggedInClientFixture fixture)
{
    [Fact]
    public async Task WhoAmIAsync_ReturnsTheSessionOwner()
    {
        var client = await fixture.GetClientAsync();

        var response = await client.WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal(client.UserId, response.UserId);
        Assert.Equal(client.DeviceId, response.DeviceId);
        Assert.False(response.IsGuest);
    }

    [Fact]
    public async Task GetVersionsAsync_WorksWithSession()
    {
        var client = await fixture.GetClientAsync();

        var response = await client.GetVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(response.Versions, version => version.StartsWith("v1."));
    }

    // Needs its own login, which counts towards the account's rate limit on a homeserver that has
    // one. It uses a fresh device rather than the configured DeviceId, so logging out cannot end the
    // shared session, and the device is deleted by the logout.
    [Fact]
    public async Task LogoutAsync_EndsTheSessionOnTheHomeserver()
    {
        var settings = fixture.Settings;
        Assert.SkipUnless(settings.IsConfigured, TestSettings.NotConfiguredMessage);
        var session = await new MatrixServer(new Uri(settings.Homeserver!)).LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = settings.User! },
            Password = settings.Password,
            InitialDeviceDisplayName = "matrix.NET logout test",
            RefreshToken = false
        }, TestContext.Current.CancellationToken);
        var client = new MatrixClient(session, new ClientOptions { AutoRefreshToken = false });

        await client.LogoutAsync(TestContext.Current.CancellationToken);

        Assert.Equal(MatrixClientState.LoggedOut, client.State);
        // The homeserver no longer accepts the token
        var afterLogout = new MatrixClient(session, new ClientOptions { AutoRefreshToken = false });
        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() =>
            afterLogout.WhoAmIAsync(TestContext.Current.CancellationToken));
    }
}
