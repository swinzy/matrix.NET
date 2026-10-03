namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Runs against a homeserver without logging in; skipped unless one is configured.
/// </summary>
public class VersionsIntegrationTests
{
    private readonly TestSettings _settings = TestSettings.Load();

    [Fact]
    public async Task GetVersionsAsync_ReturnsModernVersions()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(_settings.Homeserver), TestSettings.NotConfiguredMessage);
        var server = new MatrixServer(new Uri(_settings.Homeserver!));

        var response = await server.GetVersionsAsync(TestContext.Current.CancellationToken);

        // Any homeserver in use today supports at least one v1.x version
        Assert.Contains(response.Versions, version => version.StartsWith("v1."));
    }
}