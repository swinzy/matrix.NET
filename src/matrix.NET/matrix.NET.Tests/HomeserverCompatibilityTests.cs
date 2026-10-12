using System.Net;
using System.Reflection;
using System.Text;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Version parsing, the /versions cache (D25), feature detection and missing-endpoint errors (D26).</summary>
public class HomeserverCompatibilityTests
{
    private static readonly Uri Homeserver = new("https://matrix.example.org/");

    private static readonly MatrixSession Session = new()
    {
        Homeserver = Homeserver,
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VersionsResponse Versions(params string[] versions) => new() { Versions = versions };

    // The library's table may not yet contain a removed feature or one with a flag, so tests make their own
    private static MatrixFeature Feature(int addedMinor, int? removedMinor = null, string? flag = null) =>
        (MatrixFeature)typeof(MatrixFeature).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0]
            .Invoke(["Test feature (GET /test)", new MatrixVersion(1, addedMinor),
                removedMinor is { } removed ? new MatrixVersion(1, removed) : null, flag]);

    [Theory]
    [InlineData("v1.1", 1, 1)]
    [InlineData("v1.15", 1, 15)]
    [InlineData("v2.0", 2, 0)]
    [InlineData("r0.6.1", 1, 0)]
    [InlineData("r0.0.1", 1, 0)]
    public void TryParse_ReadsVersionsAsListed(string text, int major, int minor)
    {
        Assert.True(MatrixVersion.TryParse(text, out var version));
        Assert.Equal(new MatrixVersion(major, minor), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("v1")]
    [InlineData("1.15")]
    [InlineData("v1.2.3")]
    [InlineData("v-1.2")]
    [InlineData("v+1.2")]
    [InlineData("v 1.2")]
    [InlineData("vX.Y")]
    [InlineData("r")]
    [InlineData("rX.Y")]
    [InlineData("v99999999999.1")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(MatrixVersion.TryParse(text, out _));
    }

    [Fact]
    public void VersionsResponse_SkipsVersionsItCannotRead()
    {
        var versions = Versions("r0.6.1", "v1.1", "v1.12", "future-1", "v1.3");

        Assert.Equal([new(1, 0), new(1, 1), new(1, 12), new(1, 3)], versions.SpecVersions);
        Assert.Equal(new MatrixVersion(1, 12), versions.LatestVersion);
        Assert.Null(Versions("future-1").LatestVersion);
    }

    [Theory]
    [InlineData(true, "v1.1")]
    [InlineData(true, "r0.6.1", "v1.12")]
    [InlineData(false, "r0.5.0", "r0.6.1")]
    [InlineData(false)]
    public void MeetsMinimumVersion_NeedsV1Point1OrLater(bool expected, params string[] versions)
    {
        Assert.Equal(expected, Versions(versions).MeetsMinimumVersion);
    }

    [Theory]
    [InlineData(false, "v1.1", "v1.2")]
    [InlineData(true, "v1.3")]
    [InlineData(true, "v1.1", "v1.5")]
    public void Supports_ChecksTheVersionRange(bool expected, params string[] versions)
    {
        Assert.Equal(expected, Versions(versions).Supports(MatrixFeature.TokenRefresh));
    }

    [Theory]
    [InlineData(false, "v1.4")]
    [InlineData(true, "v1.5")]
    [InlineData(true, "v1.9")]
    [InlineData(false, "v1.10")]
    // A newer version without the feature does not hide an older advertised one that has it
    [InlineData(true, "v1.2", "v1.5", "v1.12")]
    public void Supports_RespectsRemovals(bool expected, params string[] versions)
    {
        Assert.Equal(expected, Versions(versions).Supports(Feature(addedMinor: 5, removedMinor: 10)));
    }

    [Fact]
    public void Supports_AcceptsTheStableFlagBeforeTheVersion()
    {
        var feature = Feature(addedMinor: 15, flag: "org.matrix.msc0000.stable");

        Assert.True(new VersionsResponse
        {
            Versions = ["v1.12"],
            UnstableFeatures = new Dictionary<string, bool> { ["org.matrix.msc0000.stable"] = true }
        }.Supports(feature));
        Assert.False(new VersionsResponse
        {
            Versions = ["v1.12"],
            UnstableFeatures = new Dictionary<string, bool> { ["org.matrix.msc0000.stable"] = false }
        }.Supports(feature));
    }

    [Fact]
    public async Task GetVersionsAsync_FetchesOnceWithinTheLifetime()
    {
        var time = new ManualTime();
        var handler = new CountingHandler();
        var server = new MatrixServer(Homeserver, new HttpClient(handler), new ServerOptions { TimeProvider = time });

        var first = await server.GetVersionsAsync(cancellationToken: Ct);
        time.Now += TimeSpan.FromHours(23);
        var second = await server.GetVersionsAsync(cancellationToken: Ct);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Count);

        time.Now += TimeSpan.FromHours(1);
        Assert.NotSame(first, await server.GetVersionsAsync(cancellationToken: Ct));
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task GetVersionsAsync_ZeroLifetimeFetchesEveryTime()
    {
        var handler = new CountingHandler();
        var server = new MatrixServer(Homeserver, new HttpClient(handler),
            new ServerOptions { VersionsCacheLifetime = TimeSpan.Zero });

        await server.GetVersionsAsync(cancellationToken: Ct);
        await server.GetVersionsAsync(cancellationToken: Ct);

        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task GetVersionsAsync_InfiniteLifetimeWaitsForTheAppToRefresh()
    {
        var time = new ManualTime();
        var handler = new CountingHandler();
        var server = new MatrixServer(Homeserver, new HttpClient(handler),
            new ServerOptions { VersionsCacheLifetime = Timeout.InfiniteTimeSpan, TimeProvider = time });

        await server.GetVersionsAsync(cancellationToken: Ct);
        time.Now += TimeSpan.FromDays(3650);
        await server.GetVersionsAsync(cancellationToken: Ct);
        Assert.Equal(1, handler.Count);

        await server.RefreshVersionsAsync(cancellationToken: Ct);
        await server.GetVersionsAsync(cancellationToken: Ct);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task RefreshVersionsAsync_AlwaysFetchesAndCaches()
    {
        var handler = new CountingHandler();
        var server = new MatrixServer(Homeserver, new HttpClient(handler));

        await server.GetVersionsAsync(cancellationToken: Ct);
        var refreshed = await server.RefreshVersionsAsync(cancellationToken: Ct);

        Assert.Equal(2, handler.Count);
        Assert.Same(refreshed, await server.GetVersionsAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task GetVersionsAsync_DoesNotCacheFailures()
    {
        var handler = new CountingHandler { FailFirst = true };
        var server = new MatrixServer(Homeserver, new HttpClient(handler));

        await Assert.ThrowsAsync<MatrixException>(() => server.GetVersionsAsync(cancellationToken: Ct));
        await server.GetVersionsAsync(cancellationToken: Ct);

        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task GetVersionsAsync_ConcurrentCallersShareOneRequest()
    {
        var handler = new CountingHandler { Gate = new TaskCompletionSource() };
        var server = new MatrixServer(Homeserver, new HttpClient(handler));

        var calls = Enumerable.Range(0, 5).Select(_ => server.GetVersionsAsync(cancellationToken: Ct)).ToList();
        handler.Gate.SetResult();
        var responses = await Task.WhenAll(calls);

        Assert.Equal(1, handler.Count);
        Assert.All(responses, response => Assert.Same(responses[0], response));
    }

    [Fact]
    public async Task SupportsAsync_UsesTheCachedVersions()
    {
        var handler = new CountingHandler { Body = """{"versions":["v1.1","v1.2"]}""" };
        var server = new MatrixServer(Homeserver, new HttpClient(handler));

        Assert.False(await server.SupportsAsync(MatrixFeature.TokenRefresh, cancellationToken: Ct));
        Assert.False(await server.SupportsAsync(MatrixFeature.TokenRefresh, cancellationToken: Ct));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Client_SendsTokenAndCachesSeparately()
    {
        var handler = new CountingHandler { Body = """{"versions":["v1.3"]}""" };
        var client = new MatrixClient(Session, new HttpClient(handler));

        Assert.True(await client.SupportsAsync(MatrixFeature.TokenRefresh, cancellationToken: Ct));
        await client.GetVersionsAsync(cancellationToken: Ct);

        Assert.Equal(1, handler.Count);
        Assert.Equal("secret-access", handler.LastAuthorization);
    }

    [Fact]
    public async Task Client_RefusesCachedVersionsAfterLogout()
    {
        var handler = new CountingHandler();
        var client = new MatrixClient(Session, new HttpClient(handler));
        await client.GetVersionsAsync(cancellationToken: Ct);
        await client.LogoutAsync(cancellationToken: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVersionsAsync(cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshVersionsAsync(cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SupportsAsync(MatrixFeature.TokenRefresh, cancellationToken: Ct));
    }

    [Fact]
    public void Options_RejectNegativeLifetimes()
    {
        var lifetime = TimeSpan.FromSeconds(-1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatrixServer(Homeserver, new ServerOptions { VersionsCacheLifetime = lifetime }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatrixClient(Session, new ClientOptions { VersionsCacheLifetime = lifetime }));
    }

    // Missing endpoints (D26): only through the transport, as the one feature endpoint so far, /refresh,
    // ends the session when it fails

    private static readonly Endpoint FeatureEndpoint =
        Endpoint.Since(MatrixFeature.TokenRefresh, HttpMethod.Post, "_matrix/client/v3/refresh", AuthRequirement.None);

    private static Task<MatrixException> SendAsync(Endpoint endpoint, HttpStatusCode status, string body,
        VersionsResponse? cached)
    {
        var transport = new MatrixTransport(Homeserver, () => new HttpClient(new StubHttpMessageHandler(status, body)),
            cachedVersions: () => cached);
        return Assert.ThrowsAnyAsync<MatrixException>(() => transport.SendAsync<WhoAmIResponse>(endpoint, Ct));
    }

    private const string Unrecognized = """{"errcode":"M_UNRECOGNIZED","error":"Unrecognized request"}""";

    [Fact]
    public async Task MissingEndpoint_NamesRequiredAndAdvertisedVersions()
    {
        var exception = await SendAsync(FeatureEndpoint, HttpStatusCode.NotFound, Unrecognized, Versions("v1.1", "v1.2"));

        Assert.Equal(
            "Token refresh (POST /refresh) requires Matrix v1.3 or later, but the homeserver advertises up to v1.2, " +
            "so it likely does not support it. Homeserver: Unrecognized request", exception.Message);
        Assert.Equal(MatrixErrorCodes.Unrecognized, exception.ErrorCode);
        Assert.Equal("Unrecognized request", exception.ServerMessage);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task MissingEndpoint_PointsElsewhereWhenTheFeatureIsAdvertised()
    {
        var exception = await SendAsync(FeatureEndpoint, HttpStatusCode.MethodNotAllowed, Unrecognized, Versions("v1.3"));

        Assert.StartsWith("The homeserver advertises Token refresh (POST /refresh), yet answered as if it did not " +
                          "exist; a likely cause is a reverse proxy", exception.Message);
    }

    [Fact]
    public async Task MissingEndpoint_WithoutCacheNamesTheRequiredVersionOnly()
    {
        var exception = await SendAsync(FeatureEndpoint, HttpStatusCode.NotFound, """{"errcode":"M_UNRECOGNIZED"}""", null);

        Assert.Equal("Token refresh (POST /refresh) requires Matrix v1.3 or later, so the homeserver likely does " +
                     "not support it.", exception.Message);
    }

    [Fact]
    public async Task MissingEndpoint_ExplainsRemovals()
    {
        var removed = Endpoint.Since(Feature(addedMinor: 2, removedMinor: 14), HttpMethod.Get, "_matrix/client/v3/test",
            AuthRequirement.None);

        var exception = await SendAsync(removed, HttpStatusCode.NotFound, """{"errcode":"M_UNRECOGNIZED"}""",
            Versions("v1.14", "v1.15"));

        Assert.Equal("Test feature (GET /test) was removed in Matrix v1.14, and the homeserver advertises up to " +
                     "v1.15 without any version that still has it, so it likely does not support it.", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"errcode":"M_NOT_FOUND","error":"Room not found"}""")]
    [InlineData(HttpStatusCode.NotFound, "<html>Not found</html>")]
    [InlineData(HttpStatusCode.BadRequest, Unrecognized)]
    public async Task MissingEndpoint_LeavesOtherErrorsAlone(HttpStatusCode status, string body)
    {
        var exception = await SendAsync(FeatureEndpoint, status, body, Versions("v1.1"));

        Assert.DoesNotContain("likely", exception.Message);
    }

    [Fact]
    public async Task MissingEndpoint_LeavesBaselineEndpointsAlone()
    {
        var baseline = Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/account/whoami", AuthRequirement.None);

        var exception = await SendAsync(baseline, HttpStatusCode.NotFound, Unrecognized, Versions("v1.1"));

        Assert.Equal("Unrecognized request", exception.Message);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Answers /versions (and logout), counting the /versions requests; can hold them until released.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _count;

        public int Count => _count;
        public string Body { get; init; } = """{"versions":["v1.1"]}""";
        public bool FailFirst { get; init; }
        public TaskCompletionSource? Gate { get; init; }
        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout"))
                return Json(HttpStatusCode.OK, "{}");

            var count = Interlocked.Increment(ref _count);
            LastAuthorization = request.Headers.Authorization?.Parameter;
            if (Gate is not null)
                await Gate.Task.WaitAsync(cancellationToken);

            return FailFirst && count == 1
                ? Json(HttpStatusCode.BadGateway, """{"errcode":"M_UNKNOWN"}""")
                : Json(HttpStatusCode.OK, Body);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
