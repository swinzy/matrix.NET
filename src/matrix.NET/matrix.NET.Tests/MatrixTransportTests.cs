using System.Net;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixTransportTests
{
    private const string Path = "_matrix/client/v3/account/whoami";

    private static MatrixTransport CreateTransport(HttpMessageHandler handler, string? accessToken = null,
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

    [Fact]
    public async Task SendAsync_TimesOutLikeHttpClient()
    {
        var handler = new DelayingHandler(TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None,
                TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(50)));

        Assert.IsType<TimeoutException>(exception.InnerException);
    }

    [Fact]
    public async Task SendAsync_CallerCancellationIsNotReportedAsTimeout()
    {
        var handler = new DelayingHandler(TimeSpan.FromSeconds(30));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, cancellation.Token));

        Assert.IsNotType<TimeoutException>(exception.InnerException);
    }

    [Fact]
    public async Task SendAsync_InfiniteTimeoutDoesNotCancel()
    {
        var handler = new DelayingHandler(TimeSpan.FromMilliseconds(200));

        var response = await CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None,
            TestContext.Current.CancellationToken, Timeout.InfiniteTimeSpan);

        Assert.Equal("@a:example.org", response.UserId);
    }

    [Fact]
    public void SharedClient_LeavesTimeoutsToEachRequest()
    {
        Assert.Equal(Timeout.InfiniteTimeSpan, MatrixTransport.SharedClient.Timeout);
    }

    [Fact]
    public void SharedHandler_IsolatesCookiesAndDecompresses()
    {
        using var handler = MatrixTransport.CreateSharedHandler(automaticDecompression: true);

        Assert.False(handler.UseCookies);
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);
    }

    [Fact]
    public void SharedHandler_CanOptOutOfDecompressionOnly()
    {
        using var handler = MatrixTransport.CreateSharedHandler(automaticDecompression: false);

        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.False(handler.UseCookies);
        Assert.Equal(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);
    }

    [Fact]
    public void GetSharedClient_ReusesOneClientPerDecompressionSetting()
    {
        var withDecompression = MatrixTransport.GetSharedClient(automaticDecompression: true);
        var withoutDecompression = MatrixTransport.GetSharedClient(automaticDecompression: false);

        Assert.Same(MatrixTransport.SharedClient, withDecompression);
        Assert.NotSame(withDecompression, withoutDecompression);
        Assert.Same(withoutDecompression, MatrixTransport.GetSharedClient(automaticDecompression: false));
        Assert.Equal(Timeout.InfiniteTimeSpan, withoutDecompression.Timeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_MapsUnknownTokenWithSoftLogout(bool softLogout)
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized,
            $$"""{"errcode":"M_UNKNOWN_TOKEN","error":"Token expired","soft_logout":{{(softLogout ? "true" : "false")}}}""");

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));

        Assert.Equal(softLogout, exception.SoftLogout);
        Assert.Equal("Token expired", exception.ServerMessage);
        Assert.Equal(MatrixErrorCodes.UnknownToken, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_TreatsMissingSoftLogoutAsFalse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN"}""");

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));

        Assert.False(exception.SoftLogout);
    }

    [Fact]
    public async Task SendAsync_RecordsTheRejectedTokenFromTheRequestSent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN"}""");

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() =>
            CreateTransport(handler, "secret").SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.Required, TestContext.Current.CancellationToken));

        Assert.Equal("secret", exception.RejectedAccessToken);
    }

    [Fact]
    public async Task SendAsync_MapsUserLocked()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"errcode":"M_USER_LOCKED","error":"This account has been locked","soft_logout":true}""");

        var exception = await Assert.ThrowsAsync<MatrixUserLockedException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));

        Assert.True(exception.SoftLogout);
        Assert.Equal(MatrixErrorCodes.UserLocked, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_KeepsSuspensionAsPlainMatrixException()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden, """{"errcode":"M_USER_SUSPENDED"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateTransport(handler).SendAsync<WhoAmI>(HttpMethod.Get, Path, AuthRequirement.None, TestContext.Current.CancellationToken));

        Assert.Equal(MatrixErrorCodes.UserSuspended, exception.ErrorCode);
    }

    /// <summary>Waits before answering, and honours cancellation while waiting.</summary>
    private sealed class DelayingHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"user_id":"@a:example.org"}""", System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
