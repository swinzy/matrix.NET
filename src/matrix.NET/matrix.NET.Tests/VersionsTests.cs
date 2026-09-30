using System.Net;
using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Tests;

public class VersionsTests
{
    private static MatrixServer CreateServer(StubHttpMessageHandler handler) =>
        new(new Uri("https://matrix.example.org/"), new HttpClient(handler));

    [Fact]
    public async Task GetVersionsAsync_GetsUnversionedEndpointWithoutToken()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"versions":["v1.1"]}""");

        await CreateServer(handler).GetVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        // Unlike most endpoints, /versions has no v3 in its path
        Assert.Equal("https://matrix.example.org/_matrix/client/versions", handler.Request.RequestUri!.ToString());
        Assert.Null(handler.Request.Headers.Authorization);
    }

    [Fact]
    public async Task GetVersionsAsync_ParsesSpecExample()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"versions":["r0.0.1","v1.1"],"unstable_features":{"org.example.my_feature":true}}""");

        var response = await CreateServer(handler).GetVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["r0.0.1", "v1.1"], response.Versions);
        Assert.Equal(new Dictionary<string, bool> { ["org.example.my_feature"] = true }, response.UnstableFeatures);
    }

    [Fact]
    public async Task GetVersionsAsync_LeavesUnstableFeaturesNullWhenAbsent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"versions":["v1.1"]}""");

        var response = await CreateServer(handler).GetVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Null(response.UnstableFeatures);
    }

    [Fact]
    public async Task GetVersionsAsync_ThrowsWhenVersionsMissing()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"unstable_features":{}}""");

        await Assert.ThrowsAsync<JsonException>(() =>
            CreateServer(handler).GetVersionsAsync(TestContext.Current.CancellationToken));
    }
}