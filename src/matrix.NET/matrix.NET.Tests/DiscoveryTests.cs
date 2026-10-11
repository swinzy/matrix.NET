using System.Net;
using System.Text;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Server discovery and the login's discovery information (D36).</summary>
public class DiscoveryTests
{
    private const string WellKnownUrl = "https://example.org/.well-known/matrix/client";
    private const string VersionsUrl = "https://matrix.example.org/_matrix/client/versions";
    private const string Versions = """{"versions":["v1.1","v1.12"]}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string WellKnown(string homeserver = "https://matrix.example.org", string extra = "") =>
        $$"""{"m.homeserver":{"base_url":"{{homeserver}}"}{{extra}}}""";

    [Theory]
    [InlineData("@alice:example.org", "example.org")]
    [InlineData("example.org", "example.org")]
    [InlineData("example.org:8448", "example.org")]
    [InlineData("@alice:example.org:8448", "example.org")]
    [InlineData("@alice:Example.ORG", "example.org")]
    [InlineData("1.2.3.4:8448", "1.2.3.4")]
    [InlineData("[1234:5678::abcd]:8448", "[1234:5678::abcd]")]
    public void GetHostname_DropsThePortAsTheSpecSays(string input, string expected)
    {
        Assert.Equal(expected, Discovery.GetHostname(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("@alice")]
    [InlineData("@alice:")]
    [InlineData("https://example.org")]
    [InlineData("example.org:")]
    [InlineData("example.org:https")]
    [InlineData("example.org:1234567")]
    [InlineData("[1234:5678::abcd")]
    [InlineData("exa mple.org")]
    [InlineData("example.org/path")]
    [InlineData("@a:b:c@example.org")]
    public void GetHostname_RejectsWhatIsNotAServerName(string input)
    {
        Assert.Throws<ArgumentException>(() => Discovery.GetHostname(input));
    }

    [Fact]
    public async Task DiscoverAsync_FindsAndValidatesTheHomeserver()
    {
        var handler = new RoutingHandler
        {
            [WellKnownUrl] = Ok(WellKnown(extra: ""","com.example.app":{"theme":"dark"}""")),
            [VersionsUrl] = Ok(Versions)
        };

        var discovery = await MatrixServer.DiscoverAsync("@alice:example.org:8448", new HttpClient(handler), Ct);

        Assert.NotNull(discovery);
        Assert.Equal(new Uri("https://matrix.example.org"), discovery.Homeserver);
        Assert.True(discovery.Versions.MeetsMinimumVersion);
        Assert.Null(discovery.IdentityServer);
        Assert.Null(discovery.IdentityServerFailure);
        Assert.Equal("https://matrix.example.org", discovery.Information.Homeserver?.BaseUrl);
        Assert.Equal("dark", discovery.Information.AdditionalProperties!["com.example.app"].GetProperty("theme").GetString());
        Assert.Equal([WellKnownUrl, VersionsUrl], handler.Requested);
    }

    [Theory]
    [InlineData("https://example.org/matrix/", "https://example.org/matrix/_matrix/client/versions")]
    [InlineData("https://example.org/matrix", "https://example.org/matrix/_matrix/client/versions")]
    [InlineData("http://matrix.example.org:8008", "http://matrix.example.org:8008/_matrix/client/versions")]
    public async Task DiscoverAsync_AcceptsBaseUrlsWithPathsPortsAndSlashes(string baseUrl, string versionsUrl)
    {
        var handler = new RoutingHandler { [WellKnownUrl] = Ok(WellKnown(baseUrl)), [versionsUrl] = Ok(Versions) };

        var discovery = await MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct);

        Assert.Equal(new Uri(baseUrl), discovery!.Homeserver);
    }

    // Some servers send the file with another content type; matrix-nio works around one such server
    [Fact]
    public async Task DiscoverAsync_ReadsJsonWhateverTheContentType()
    {
        var handler = new RoutingHandler
        {
            [WellKnownUrl] = Ok(WellKnown(), "text/plain"),
            [VersionsUrl] = Ok(Versions)
        };

        Assert.NotNull(await MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct));
    }

    [Theory]
    [InlineData("<html>Not found</html>")]
    [InlineData("""{"errcode":"M_UNRECOGNIZED","error":"Unrecognized request"}""")]
    public async Task DiscoverAsync_ReturnsNullWithoutDiscoveryInformation(string body)
    {
        var handler = new RoutingHandler { [WellKnownUrl] = Respond(HttpStatusCode.NotFound, body) };

        Assert.Null(await MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct));
    }

    public static TheoryData<string, Func<HttpResponseMessage>> UnusableInformation => new()
    {
        { "error status", Respond(HttpStatusCode.InternalServerError, "") },
        { "empty body", Ok("") },
        { "not JSON", Ok("<html>Welcome</html>") },
        { "not an object", Ok("[]") },
        { "no m.homeserver", Ok("{}") },
        { "no base_url", Ok("""{"m.homeserver":{}}""") },
        { "base_url not a string", Ok("""{"m.homeserver":{"base_url":5}}""") },
        { "network failure", () => throw new HttpRequestException("Connection refused") }
    };

    [Theory]
    [MemberData(nameof(UnusableInformation))]
    public async Task DiscoverAsync_AsksForTheHomeserverWhenTheInformationIsUnusable(string reason,
        Func<HttpResponseMessage> wellKnown)
    {
        var handler = new RoutingHandler { [WellKnownUrl] = wellKnown };

        var exception = await Assert.ThrowsAsync<MatrixDiscoveryException>(() =>
            MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct));

        Assert.True(exception.Kind == DiscoveryFailureKind.FailPrompt, reason);
        Assert.Equal([WellKnownUrl], handler.Requested);
    }

    [Theory]
    [InlineData("matrix.example.org")]
    [InlineData("ftp://matrix.example.org")]
    [InlineData("/relative")]
    public async Task DiscoverAsync_StopsWhenTheBaseUrlIsInvalid(string baseUrl)
    {
        var handler = new RoutingHandler { [WellKnownUrl] = Ok(WellKnown(baseUrl)) };

        var exception = await Assert.ThrowsAsync<MatrixDiscoveryException>(() =>
            MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct));

        Assert.Equal(DiscoveryFailureKind.FailError, exception.Kind);
        Assert.Contains(baseUrl, exception.Message);
    }

    public static TheoryData<string, Func<HttpResponseMessage>> NoHomeserver => new()
    {
        { "404", Respond(HttpStatusCode.NotFound, "<html>Not found</html>") },
        { "not versions", Ok("""{"something":"else"}""") },
        { "network failure", () => throw new HttpRequestException("Connection refused") }
    };

    [Theory]
    [MemberData(nameof(NoHomeserver))]
    public async Task DiscoverAsync_StopsWhenNoHomeserverAnswers(string reason, Func<HttpResponseMessage> versions)
    {
        var handler = new RoutingHandler { [WellKnownUrl] = Ok(WellKnown()), [VersionsUrl] = versions };

        var exception = await Assert.ThrowsAsync<MatrixDiscoveryException>(() =>
            MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct));

        Assert.True(exception.Kind == DiscoveryFailureKind.FailError, reason);
        Assert.NotNull(exception.InnerException);
    }

    [Theory]
    [InlineData("""{"base_url":"https://identity.example.org"}""", "https://identity.example.org/", null)]
    [InlineData("{}", null, DiscoveryFailureKind.FailPrompt)]
    [InlineData("""{"base_url":"identity.example.org"}""", null, DiscoveryFailureKind.FailError)]
    public async Task DiscoverAsync_ReadsTheIdentityServerWithoutContactingIt(string identityServer, string? expected,
        DiscoveryFailureKind? failure)
    {
        var handler = new RoutingHandler
        {
            [WellKnownUrl] = Ok(WellKnown(extra: $$""","m.identity_server":{{identityServer}}""")),
            [VersionsUrl] = Ok(Versions)
        };

        var discovery = await MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), Ct);

        // A problem with the identity server does not affect the homeserver
        Assert.Equal(new Uri("https://matrix.example.org"), discovery!.Homeserver);
        Assert.Equal(expected, discovery.IdentityServer?.AbsoluteUri);
        Assert.Equal(failure, discovery.IdentityServerFailure);
        Assert.Equal([WellKnownUrl, VersionsUrl], handler.Requested);
    }

    [Fact]
    public async Task DiscoverAsync_CallerCancellationIsNotADiscoveryFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RoutingHandler
        {
            [WellKnownUrl] = () =>
            {
                cancellation.Cancel();
                throw new TaskCanceledException();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MatrixServer.DiscoverAsync("example.org", new HttpClient(handler), cancellation.Token));
    }

    [Fact]
    public async Task LoginAsync_ReturnsDiscoveryInformationWithoutApplyingIt()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@a:example.org","access_token":"t","device_id":"D","well_known":""" +
            """{"m.homeserver":{"base_url":"https://new.example.org"},"m.identity_server":{"base_url":"https://id.example.org"}}}""");

        var login = await new MatrixServer(new Uri("https://matrix.example.org"), new HttpClient(handler))
            .LoginAsync(Login(), Ct);

        Assert.Equal("https://new.example.org", login.WellKnown?.Homeserver?.BaseUrl);
        Assert.Equal("https://id.example.org", login.WellKnown?.IdentityServer?.BaseUrl);
        Assert.Equal(new Uri("https://matrix.example.org/"), login.Session.Homeserver);
    }

    [Theory]
    [InlineData("")]
    [InlineData(""","well_known":null""")]
    [InlineData(",\"well_known\":\"https://new.example.org\"")]
    [InlineData(""","well_known":{"m.homeserver":5}""")]
    public async Task LoginAsync_IgnoresMissingOrMalformedDiscoveryInformation(string wellKnown)
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            $$"""{"user_id":"@a:example.org","access_token":"t","device_id":"D"{{wellKnown}}}""");

        var login = await new MatrixServer(new Uri("https://matrix.example.org"), new HttpClient(handler))
            .LoginAsync(Login(), Ct);

        Assert.Null(login.WellKnown);
        Assert.Equal("t", login.Session.AccessToken);
    }

    private static LoginRequest Login() => new()
    {
        Type = "m.login.password", Identifier = new UserIdentifier { User = "a" }, Password = "p"
    };

    private static Func<HttpResponseMessage> Ok(string body, string mediaType = "application/json") =>
        Respond(HttpStatusCode.OK, body, mediaType);

    private static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body,
        string mediaType = "application/json") =>
        () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    /// <summary>Answers each URL with its own response, recording what was requested; unknown URLs fail the test.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new();

        public List<string> Requested { get; } = [];

        public Func<HttpResponseMessage> this[string url]
        {
            set => _routes[url] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            return _routes.TryGetValue(url, out var respond)
                ? Task.FromResult(respond())
                : throw new InvalidOperationException($"Unexpected request to {url}");
        }
    }
}
