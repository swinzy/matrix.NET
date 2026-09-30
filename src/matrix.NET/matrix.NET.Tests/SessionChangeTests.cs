using System.Net;
using System.Text;

namespace TeamBanana.MatrixDotNet.Tests;

public class SessionChangeTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static readonly (HttpStatusCode, string) Ok = (HttpStatusCode.OK, """{"user_id":"@joe:example.org","versions":["v1.1"]}""");
    private static readonly (HttpStatusCode, string) LoggedOutOk = (HttpStatusCode.OK, "{}");
    private static readonly (HttpStatusCode, string) Locked =
        (HttpStatusCode.Unauthorized, """{"errcode":"M_USER_LOCKED","error":"This account has been locked","soft_logout":true}""");
    private static readonly (HttpStatusCode, string) UnknownToken =
        (HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN","soft_logout":true}""");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Creates a client whose homeserver answers with <paramref name="responses"/> in order.</summary>
    private static (MatrixClient Client, List<(SessionChangeKind Kind, bool SoftLogout, MatrixClientState State)> Changes)
        CreateClient(params (HttpStatusCode, string)[] responses)
    {
        var client = new MatrixClient(Session, new HttpClient(new SequenceHandler(responses)),
            new ClientOptions { AutoRefreshToken = false });
        var changes = new List<(SessionChangeKind, bool, MatrixClientState)>();
        client.SessionChanged += (_, e) => changes.Add((e.Kind, e.SoftLogout, client.State));
        return (client, changes);
    }

    [Fact]
    public async Task Logout_RaisesLoggedOut()
    {
        var (client, changes) = CreateClient(LoggedOutOk);

        await client.LogoutAsync(Ct);

        Assert.Equal([(SessionChangeKind.LoggedOut, false, MatrixClientState.LoggedOut)], changes);
    }

    [Fact]
    public async Task RejectedToken_RaisesInvalidatedWithSoftLogoutAfterStateChange()
    {
        var (client, changes) = CreateClient(UnknownToken);

        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        // The handler already saw the new state
        Assert.Equal([(SessionChangeKind.Invalidated, true, MatrixClientState.Invalidated)], changes);
    }

    [Fact]
    public async Task SuccessfulCall_RaisesNothing()
    {
        var (client, changes) = CreateClient(Ok);

        await client.WhoAmIAsync(Ct);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task Locked_KeepsClientUsableAndRaisesOnce()
    {
        var (client, changes) = CreateClient(Locked, Locked);

        var exception = await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));
        // Still sent while locked, so the unlock can be detected
        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));

        Assert.True(exception.SoftLogout);
        Assert.Equal(MatrixClientState.Locked, client.State);
        Assert.Equal("secret-access", client.Session.AccessToken);
        Assert.Equal([(SessionChangeKind.Locked, false, MatrixClientState.Locked)], changes);
    }

    [Fact]
    public async Task Locked_UnlocksOnSuccessfulAuthenticatedCall()
    {
        var (client, changes) = CreateClient(Locked, Ok);

        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));
        await client.WhoAmIAsync(Ct);

        Assert.Equal(MatrixClientState.Active, client.State);
        Assert.Equal(
            [(SessionChangeKind.Locked, false, MatrixClientState.Locked), (SessionChangeKind.Unlocked, false, MatrixClientState.Active)],
            changes);
    }

    // An endpoint with optional authentication may be answered without checking the token
    [Fact]
    public async Task Locked_StaysLockedAfterSuccessfulOptionalAuthCall()
    {
        var (client, changes) = CreateClient(Locked, Ok);

        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));
        await client.GetVersionsAsync(Ct);

        Assert.Equal(MatrixClientState.Locked, client.State);
        Assert.Single(changes);
    }

    [Fact]
    public async Task Locked_CanLogOut()
    {
        var (client, changes) = CreateClient(Locked, LoggedOutOk);

        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));
        await client.LogoutAsync(Ct);

        Assert.Equal(MatrixClientState.LoggedOut, client.State);
        Assert.Equal(SessionChangeKind.LoggedOut, changes[^1].Kind);
    }

    [Fact]
    public async Task Locked_ThenRejectedToken_Invalidates()
    {
        var (client, changes) = CreateClient(Locked, UnknownToken);

        await Assert.ThrowsAsync<MatrixUserLockedException>(() => client.WhoAmIAsync(Ct));
        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.Equal(MatrixClientState.Invalidated, client.State);
        Assert.Equal(SessionChangeKind.Invalidated, changes[^1].Kind);
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotHideTheLibraryErrorOrStopOtherHandlers()
    {
        var (client, changes) = CreateClient(UnknownToken);
        client.SessionChanged += (_, _) => throw new InvalidOperationException("Bug in the app's handler");
        var laterHandlerRan = false;
        client.SessionChanged += (_, _) => laterHandlerRan = true;

        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(Ct));

        Assert.Single(changes);
        Assert.True(laterHandlerRan);
    }

    /// <summary>Answers each request with the next of the given responses.</summary>
    private sealed class SequenceHandler((HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var (status, body) = responses[_next++];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}