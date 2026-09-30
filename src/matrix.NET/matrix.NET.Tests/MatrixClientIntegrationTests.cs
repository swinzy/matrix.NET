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
}