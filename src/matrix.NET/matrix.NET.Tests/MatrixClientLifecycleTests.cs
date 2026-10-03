using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixClientLifecycleTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static readonly MatrixSession RefreshableSession = Session with { RefreshToken = "secret-refresh" };

    private const string RejectedToken =
        """{"errcode":"M_UNKNOWN_TOKEN","error":"Token expired","soft_logout":true}""";

    private static MatrixClient CreateClient(StubHttpMessageHandler handler, MatrixSession? session = null,
        bool autoRefreshToken = true) =>
        new(session ?? Session, new HttpClient(handler), new ClientOptions { AutoRefreshToken = autoRefreshToken });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LogoutAsync_PostsWithTokenAndEndsSession()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        await client.LogoutAsync(Ct);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/logout", handler.Request.RequestUri!.ToString());
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal(MatrixClientState.LoggedOut, client.State);
    }

    [Fact]
    public async Task AfterLogout_EveryCallThrowsButIdentityRemains()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);
        await client.LogoutAsync(Ct);
        var logoutRequest = handler.Request;

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.WhoAmIAsync(Ct));
        // Shared endpoints do not fall back to unauthenticated requests (D21)
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVersionsAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LogoutAsync(Ct));
        Assert.Throws<InvalidOperationException>(() => client.Session);

        Assert.Equal("@joe:example.org", client.UserId);
        Assert.Equal("ABC1234", client.DeviceId);
        Assert.Same(logoutRequest, handler.Request);
    }

    [Fact]
    public async Task LogoutAsync_TreatsUnknownTokenAsSuccess()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, RejectedToken));

        await client.LogoutAsync(Ct);

        Assert.Equal(MatrixClientState.LoggedOut, client.State);
    }

    [Fact]
    public async Task LogoutAsync_OtherFailuresLeaveClientUsable()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "text/html"));

        await Assert.ThrowsAsync<MatrixException>(() => client.LogoutAsync(Ct));

        Assert.Equal(MatrixClientState.Active, client.State);
    }

    [Fact]
    public async Task RejectedToken_WithoutRefreshToken_InvalidatesSession()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, RejectedToken));

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.StartsWith("The access token is no longer valid and the session has no refresh token. Log in again.",
            exception.Message);
        Assert.Contains("Token expired", exception.Message);
        Assert.Equal("Token expired", exception.ServerMessage);
        Assert.True(exception.SoftLogout);
        Assert.Equal(MatrixClientState.Invalidated, client.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.WhoAmIAsync(Ct));
    }

    [Fact]
    public async Task RejectedToken_WithAutoRefreshDisabled_InvalidatesSession()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, RejectedToken),
            RefreshableSession, autoRefreshToken: false);

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.StartsWith("The access token is no longer valid and automatic token refresh is disabled.",
            exception.Message);
        Assert.Equal(MatrixClientState.Invalidated, client.State);
    }

    [Fact]
    public async Task RejectedToken_WithAutoRefreshDisabledAndNoRefreshToken_AsksToLogInAgain()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, RejectedToken),
            autoRefreshToken: false);

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.StartsWith("The access token is no longer valid and the session has no refresh token.", exception.Message);
    }

    [Fact]
    public async Task RejectedToken_OnSharedEndpoint_AlsoInvalidates()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, RejectedToken));

        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.GetVersionsAsync(Ct));

        Assert.Equal(MatrixClientState.Invalidated, client.State);
    }

    [Fact]
    public async Task UserLocked_DoesNotInvalidate()
    {
        var client = CreateClient(new StubHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"errcode":"M_USER_LOCKED","soft_logout":true}"""));

        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));

        Assert.NotEqual(MatrixClientState.Invalidated, client.State);
    }
}