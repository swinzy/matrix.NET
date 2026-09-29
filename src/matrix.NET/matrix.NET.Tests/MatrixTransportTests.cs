using System.Net;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixTransportTests
{
    private const string Path = "_matrix/client/v3/account/whoami";

    private static MatrixTransport CreateTransport(StubHttpMessageHandler handler, string? accessToken = null,
        string homeserver = "https://matrix.example.org/") =>
        new(new Uri(homeserver), () => new HttpClient(handler), accessToken is null ? null : () => accessToken);

    private static StubHttpMessageHandler OkHandler() => new(HttpStatusCode.OK, """{"user_id":"@a:example.org"}""");

    private record WhoAmI(string UserId);

    [Theory]
    [InlineData("https://matrix.example.org/", "https://matrix.example.org/_matrix/client/v3/account/whoami")]
    [InlineData("https://matrix.example.org", "https://matrix.example.org/_matrix/client/v3/account/whoami")]
    [InlineData("https://example.org/matrix/", "https://example.org/matrix/_matrix/client/v3/account/whoami")]
    [InlineData("https://example.org/matrix", "https://example.org/matrix/_matrix/client/v3/account/whoami")]
    public async Task SendAsync_BuildsAbsoluteUriUnderHomeserver(string homeserver, string expected)
    {
        var handler = OkHandler();

        await CreateTransport(handler, homeserver: homeserver)
            .SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken);

        Assert.Equal(expected, handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendAsync_SendsNoBodyWhenNoneGiven()
    {
        var handler = OkHandler();

        await CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken);

        Assert.Null(handler.RequestBody);
    }

    [Fact]
    public async Task SendAsync_DeserialisesResponse()
    {
        var response = await CreateTransport(OkHandler())
            .SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken);

        Assert.Equal("@a:example.org", response.UserId);
    }

    [Fact]
    public async Task SendAsync_RequiredAuth_SendsBearerToken()
    {
        var handler = OkHandler();

        await CreateTransport(handler, "secret").SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Required, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("secret", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_RequiredAuth_ThrowsWithoutTokenAndSendsNothing()
    {
        var handler = OkHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Required, TestContext.Current.CancellationToken));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SendAsync_OptionalAuth_SendsTokenWhenAvailable()
    {
        var handler = OkHandler();

        await CreateTransport(handler, "secret").SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Optional, TestContext.Current.CancellationToken);

        Assert.Equal("secret", handler.Request!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task SendAsync_OptionalAuth_SendsNoTokenWhenUnavailable()
    {
        var handler = OkHandler();

        await CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Optional, TestContext.Current.CancellationToken);

        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_NoAuth_NeverSendsToken()
    {
        var handler = OkHandler();

        await CreateTransport(handler, "secret").SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken);

        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_ReadsTokenAndClientPerRequest()
    {
        var handler = OkHandler();
        var token = "first";
        var clientsCreated = 0;
        var transport = new MatrixTransport(new Uri("https://matrix.example.org/"),
            () =>
            {
                clientsCreated++;
                return new HttpClient(handler);
            },
            () => token);

        await transport.SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Required, TestContext.Current.CancellationToken);
        token = "second";
        await transport.SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Required, TestContext.Current.CancellationToken);

        Assert.Equal("second", handler.Request!.Headers.Authorization!.Parameter);
        Assert.Equal(2, clientsCreated);
    }

    [Fact]
    public async Task SendAsync_ThrowsOnNullResponseBody()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "null");

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));
    }

    // Error codes outside MatrixErrorCodes, e.g. added by a newer spec or custom to a homeserver,
    // must reach the caller unchanged rather than failing or collapsing to M_UNKNOWN
    [Theory]
    [InlineData("M_SOMETHING_NEWER_THAN_THIS_SDK")]
    [InlineData("COM.EXAMPLE.HOMESERVER_FORBIDDEN")]
    public async Task SendAsync_PreservesErrorCodesNotInConstants(string errorCode)
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            $$"""{"errcode":"{{errorCode}}","error":"Something went wrong"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal("Something went wrong", exception.Message);
    }

    [Fact]
    public async Task SendAsync_IgnoresUnknownFieldsInErrorResponse()
    {
        var handler = new StubHttpMessageHandler((HttpStatusCode)403,
            """{"errcode":"M_USER_LIMIT_EXCEEDED","error":"Quota exceeded","info_uri":"https://example.org/limits","can_upgrade":true,"some_future_field":{"nested":[1,2]}}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));

        Assert.Equal(MatrixErrorCodes.UserLimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_IgnoresUnknownFieldsInSuccessResponse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@a:example.org","some_future_field":true}""");

        var response = await CreateTransport(handler)
            .SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken);

        Assert.Equal("@a:example.org", response.UserId);
    }
}