namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Discovers a real, public homeserver, as the local Synapse serves no HTTPS. Explicit, as it depends on a
/// server outside the test setup.
/// </summary>
public class DiscoveryIntegrationTests
{
    [Fact(Explicit = true)]
    public async Task DiscoverAsync_FindsMatrixOrg()
    {
        var discovery = await MatrixServer.DiscoverAsync("@someone:matrix.org", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(discovery);
        Assert.Equal("https", discovery.Homeserver.Scheme);
        Assert.True(discovery.Versions.MeetsMinimumVersion);
    }
}
