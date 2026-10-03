using System.Net;
using System.Text;
using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Tests;

public class TokenRefreshTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "old-access",
        RefreshToken = "old-refresh"
    };

    private const string WhoAmI = """{"user_id":"@joe:example.org"}""";
    private const string RefreshPath = "/_matrix/client/v3/refresh";
    private const string WhoAmIPath = "/_matrix/client/v3/account/whoami";

    private static readonly (HttpStatusCode, string) UnknownToken =
        (HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN","error":"Token expired","soft_logout":true}""");
    private static readonly (HttpStatusCode, string) Refreshed =
        (HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh","expires_in_ms":60000}""");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A homeserver that rejects every token but <c>new-access</c> and answers refreshes with
    /// <paramref name="refreshResponse"/>.
    /// </summary>
    private static RoutingHandler CreateHomeserver((HttpStatusCode, string)? refreshResponse = null) =>
        new(request => request.RequestUri!.AbsolutePath == RefreshPath
            ? refreshResponse ?? Refreshed
            : request.Headers.Authorization?.Parameter == "new-access" ? (HttpStatusCode.OK, WhoAmI) : UnknownToken);

    private static MatrixClient CreateClient(RoutingHandler handler, ISessionRefreshHandler? refreshHandler = null) =>
        new(Session, new HttpClient(handler),
            new ClientOptions { SessionRefreshHandler = refreshHandler ?? new DiscardingSessionRefreshHandler() });

    [Fact]
    public void Constructor_RequiresHandlerWhenRefreshCanHappen()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MatrixClient(Session));

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("SessionRefreshHandler", exception.Message);
        Assert.Contains("AutoRefreshToken", exception.Message);
    }

    [Fact]
    public void Constructor_NeedsNoHandlerWhenRefreshCannotHappen()
    {
        _ = new MatrixClient(Session with { RefreshToken = null });
        _ = new MatrixClient(Session, new ClientOptions { AutoRefreshToken = false });
    }

    [Fact]
    public async Task RejectedToken_RefreshesAndRetries()
    {
        var handler = CreateHomeserver();
        var client = CreateClient(handler);

        var response = await client.WhoAmIAsync(Ct);

        Assert.Equal("@joe:example.org", response.UserId);
        Assert.Equal([WhoAmIPath, RefreshPath, WhoAmIPath], handler.Requests.Select(r => r.Path));
        var refresh = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, refresh.Method);
        Assert.Null(refresh.AccessToken);
        Assert.Equal("old-refresh", JsonDocument.Parse(refresh.Body!).RootElement.GetProperty("refresh_token").GetString());
        Assert.Equal("new-access", handler.Requests[2].AccessToken);
        Assert.Equal(MatrixClientState.Active, client.State);
    }

    [Fact]
    public async Task Refresh_UpdatesTheSession()
    {
        var client = CreateClient(CreateHomeserver());
        var before = DateTimeOffset.UtcNow;

        await client.WhoAmIAsync(Ct);

        var session = client.Session;
        Assert.Equal("new-access", session.AccessToken);
        Assert.Equal("new-refresh", session.RefreshToken);
        Assert.InRange(session.ExpiresAt!.Value, before.AddSeconds(60), DateTimeOffset.UtcNow.AddSeconds(60));
        Assert.Equal(Session.UserId, session.UserId);
        Assert.Equal(Session.DeviceId, session.DeviceId);
    }

    [Fact]
    public async Task Refresh_WithoutNewRefreshToken_KeepsTheOldOne()
    {
        var client = CreateClient(CreateHomeserver((HttpStatusCode.OK, """{"access_token":"new-access"}""")));

        await client.WhoAmIAsync(Ct);

        Assert.Equal("old-refresh", client.Session.RefreshToken);
        Assert.Null(client.Session.ExpiresAt);
    }

    [Fact]
    public async Task Refresh_SavesBeforeUsingNewTokensAndThenNotifies()
    {
        var handler = CreateHomeserver();
        var events = new List<string>();
        var refreshHandler = new RecordingRefreshHandler(session =>
        {
            events.Add($"saved {session.AccessToken} after {handler.Requests.Count} requests");
            return ValueTask.CompletedTask;
        });
        var client = CreateClient(handler, refreshHandler);
        client.SessionChanged += (_, e) => events.Add($"{e.Kind} with {client.Session.AccessToken}");

        await client.WhoAmIAsync(Ct);

        Assert.Equal(["saved new-access after 2 requests", "TokensRefreshed with new-access"], events);
    }

    [Fact]
    public async Task FailedSave_DiscardsNewTokensAndThrows()
    {
        var handler = CreateHomeserver();
        var client = CreateClient(handler,
            new RecordingRefreshHandler(_ => throw new IOException("Disk full")));
        var changes = new List<SessionChangeKind>();
        client.SessionChanged += (_, e) => changes.Add(e.Kind);

        await Assert.ThrowsAsync<IOException>(() => client.WhoAmIAsync(Ct));

        Assert.Equal("old-access", client.Session.AccessToken);
        Assert.Equal("old-refresh", client.Session.RefreshToken);
        Assert.Equal(MatrixClientState.Active, client.State);
        Assert.Empty(changes);
        // The request was not retried with the unsaved tokens
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RejectedRefresh_InvalidatesSession()
    {
        var client = CreateClient(CreateHomeserver(
            (HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN","error":"Refresh token revoked"}""")));
        var changes = new List<(SessionChangeKind, bool)>();
        client.SessionChanged += (_, e) => changes.Add((e.Kind, e.SoftLogout));

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.StartsWith("The access token expired and could not be refreshed. Log in again.", exception.Message);
        Assert.Equal("Refresh token revoked", exception.ServerMessage);
        Assert.Equal(MatrixClientState.Invalidated, client.State);
        Assert.Equal([(SessionChangeKind.Invalidated, false)], changes);
    }

    // Synapse answers a used or expired refresh token with 403 M_FORBIDDEN instead of M_UNKNOWN_TOKEN
    [Theory]
    [InlineData("refresh token isn't valid anymore")]
    [InlineData("The supplied refresh token has expired")]
    public async Task SynapseRefreshRejection_InvalidatesSession(string serverMessage)
    {
        var client = CreateClient(CreateHomeserver(
            (HttpStatusCode.Forbidden, $$"""{"errcode":"M_FORBIDDEN","error":"{{serverMessage}}"}""")));
        var changes = new List<(SessionChangeKind, bool)>();
        client.SessionChanged += (_, e) => changes.Add((e.Kind, e.SoftLogout));

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.StartsWith("The access token expired and could not be refreshed. Log in again.", exception.Message);
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal(serverMessage, exception.ServerMessage);
        Assert.False(exception.SoftLogout);
        Assert.Equal(MatrixErrorCodes.Forbidden, Assert.IsType<MatrixException>(exception.InnerException).ErrorCode);
        Assert.Equal(MatrixClientState.Invalidated, client.State);
        Assert.Equal([(SessionChangeKind.Invalidated, false)], changes);
    }

    [Fact]
    public async Task RateLimitedRefresh_ThrowsWithoutInvalidating()
    {
        var client = CreateClient(CreateHomeserver(
            ((HttpStatusCode)429, """{"errcode":"M_LIMIT_EXCEEDED","retry_after_ms":2000}""")));

        var exception = await Assert.ThrowsAsync<MatrixException>(() => client.WhoAmIAsync(Ct));

        Assert.Equal(MatrixErrorCodes.LimitExceeded, exception.ErrorCode);
        Assert.Equal(MatrixClientState.Active, client.State);
    }

    [Fact]
    public async Task TransientRefreshFailure_ThrowsWithoutInvalidating()
    {
        var handler = CreateHomeserver((HttpStatusCode.BadGateway, "<html>Bad Gateway</html>"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<MatrixException>(() => client.WhoAmIAsync(Ct));

        Assert.Equal(MatrixClientState.Active, client.State);
        Assert.Equal("old-refresh", client.Session.RefreshToken);
    }

    [Fact]
    public async Task LockedDuringRefresh_LocksWithoutInvalidating()
    {
        var client = CreateClient(CreateHomeserver(
            (HttpStatusCode.Unauthorized, """{"errcode":"M_USER_LOCKED","soft_logout":true}""")));

        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));

        Assert.Equal(MatrixClientState.Locked, client.State);
    }

    [Fact]
    public async Task NewTokenRejectedToo_InvalidatesWithoutRefreshingAgain()
    {
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == RefreshPath ? Refreshed : UnknownToken);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.StartsWith("The access token expired and could not be refreshed.", exception.Message);
        Assert.Equal(MatrixClientState.Invalidated, client.State);
        Assert.Equal([WhoAmIPath, RefreshPath, WhoAmIPath], handler.Requests.Select(r => r.Path));
    }

    // Each request gets its own chance to refresh, even when its token came from an earlier refresh
    [Fact]
    public async Task TokenFromEarlierRefreshRejected_RefreshesAgain()
    {
        var refreshes = 0;
        var newAccessUses = 0;
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == RefreshPath)
                return ++refreshes == 1
                    ? Refreshed
                    : (HttpStatusCode.OK, """{"access_token":"newer-access","refresh_token":"newer-refresh"}""");

            // new-access works once, then the homeserver revokes it
            return request.Headers.Authorization?.Parameter switch
            {
                "new-access" when ++newAccessUses == 1 => (HttpStatusCode.OK, WhoAmI),
                "newer-access" => (HttpStatusCode.OK, WhoAmI),
                _ => UnknownToken
            };
        });
        var client = CreateClient(handler);

        await client.WhoAmIAsync(Ct);
        await client.WhoAmIAsync(Ct);

        Assert.Equal(2, refreshes);
        Assert.Equal("newer-access", client.Session.AccessToken);
        Assert.Equal(MatrixClientState.Active, client.State);
    }

    // A request that only retried with another request's token has not used its own refresh yet
    [Fact]
    public async Task RetryWithOthersTokenRejected_StillRefreshesItself()
    {
        var refreshStarted = new TaskCompletionSource();
        var secondRejected = new TaskCompletionSource();
        var refreshes = 0;
        var handler = new RoutingHandler(async request =>
        {
            var token = request.Headers.Authorization?.Parameter;
            switch (request.RequestUri!.AbsolutePath)
            {
                case RefreshPath when Interlocked.Increment(ref refreshes) == 1:
                    refreshStarted.SetResult();
                    await secondRejected.Task;
                    return Refreshed;
                case RefreshPath:
                    return (HttpStatusCode.OK, """{"access_token":"newer-access","refresh_token":"newer-refresh"}""");
                case WhoAmIPath:
                    return token == "new-access" ? (HttpStatusCode.OK, WhoAmI) : UnknownToken;
                default:
                    // GET /versions accepts only the token from the second refresh
                    if (token == "newer-access")
                        return (HttpStatusCode.OK, """{"versions":["v1.1"]}""");
                    if (token == "old-access")
                        secondRejected.SetResult();
                    return UnknownToken;
            }
        });
        var client = CreateClient(handler);

        // The first request refreshes; the second is rejected while that refresh is in flight
        var first = client.WhoAmIAsync(Ct);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = client.GetVersionsAsync(Ct);

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(2, refreshes);
        Assert.Equal("newer-access", client.Session.AccessToken);
        Assert.Equal(MatrixClientState.Active, client.State);
    }

    [Fact]
    public async Task ConcurrentRejections_ShareOneRefresh()
    {
        // Holds the refresh until both requests have been rejected
        var bothRejected = new TaskCompletionSource();
        var rejections = 0;
        var refreshes = 0;
        var handler = new RoutingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == RefreshPath)
            {
                Interlocked.Increment(ref refreshes);
                await bothRejected.Task;
                return Refreshed;
            }

            if (request.Headers.Authorization?.Parameter == "new-access")
                return (HttpStatusCode.OK, WhoAmI);

            if (Interlocked.Increment(ref rejections) == 2)
                bothRejected.SetResult();
            return UnknownToken;
        });
        var client = CreateClient(handler);
        var tokensRefreshed = 0;
        client.SessionChanged += (_, e) => tokensRefreshed += e.Kind == SessionChangeKind.TokensRefreshed ? 1 : 0;

        await Task.WhenAll(client.WhoAmIAsync(Ct), client.WhoAmIAsync(Ct)).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(1, refreshes);
        Assert.Equal(1, tokensRefreshed);
        Assert.Equal("new-access", client.Session.AccessToken);
    }

    [Fact]
    public async Task WaitingRequest_AfterRefreshFailed_ThrowsTheRejection()
    {
        var bothRejected = new TaskCompletionSource();
        var rejections = 0;
        var handler = new RoutingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == RefreshPath)
            {
                await bothRejected.Task;
                return (HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN"}""");
            }

            if (Interlocked.Increment(ref rejections) == 2)
                bothRejected.SetResult();
            return UnknownToken;
        });
        var client = CreateClient(handler);

        var first = client.WhoAmIAsync(Ct);
        var second = client.WhoAmIAsync(Ct);

        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => first);
        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => second);
        Assert.Equal(1, handler.Requests.Count(r => r.Path == RefreshPath));
    }

    private sealed class RecordingRefreshHandler(Func<MatrixSession, ValueTask> onRefreshed) : ISessionRefreshHandler
    {
        public ValueTask OnSessionRefreshedAsync(MatrixSession session, CancellationToken cancellationToken) =>
            onRefreshed(session);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? AccessToken, string? Body);

    /// <summary>Answers each request through a function of it, and records every request.</summary>
    private sealed class RoutingHandler(Func<HttpRequestMessage, Task<(HttpStatusCode Status, string Body)>> respond)
        : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public RoutingHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond)
            : this(request => Task.FromResult(respond(request)))
        {
        }

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests)
                _requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath,
                    request.Headers.Authorization?.Parameter, body));

            var (status, content) = await respond(request);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
