using TeamBanana.MatrixDotNet.Transport;

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

    [Fact]
    public async Task SupportsAsync_FindsFeaturesOfTheAdvertisedVersions()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(_settings.Homeserver), TestSettings.NotConfiguredMessage);
        var server = new MatrixServer(new Uri(_settings.Homeserver!));

        var response = await server.GetVersionsAsync(TestContext.Current.CancellationToken);

        Assert.True(response.MeetsMinimumVersion);
        // Every homeserver in use today is past v1.3, which added token refresh
        Assert.True(await server.SupportsAsync(MatrixFeature.TokenRefresh, TestContext.Current.CancellationToken));
    }

    // Checks that the homeserver answers an unknown path as D26 expects, 404 or 405 M_UNRECOGNIZED, by
    // declaring one as if it belonged to a feature the homeserver advertises
    [Fact]
    public async Task UnknownEndpoint_IsExplainedAsMissing()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(_settings.Homeserver), TestSettings.NotConfiguredMessage);
        var homeserver = new Uri(_settings.Homeserver!);
        var versions = await new MatrixServer(homeserver).GetVersionsAsync(TestContext.Current.CancellationToken);
        var transport = new MatrixTransport(homeserver, cachedVersions: () => versions);
        var unknown = Endpoint.Since(MatrixFeature.TokenRefresh, HttpMethod.Get,
            "_matrix/client/v3/matrixdotnet_does_not_exist", AuthRequirement.None);

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            transport.SendAsync<VersionsResponse>(unknown, TestContext.Current.CancellationToken));

        Assert.Equal(MatrixErrorCodes.Unrecognized, exception.ErrorCode);
        Assert.StartsWith("The homeserver advertises Token refresh", exception.Message);
    }
}
