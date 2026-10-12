using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

public class RoomMembershipTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MatrixClient CreateClient(StubHttpMessageHandler handler) => new(Session, new HttpClient(handler));

    [Fact]
    public async Task GetJoinedRoomsAsync_GetsWithToken()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"joined_rooms":[]}""");

        await CreateClient(handler).GetJoinedRoomsAsync(cancellationToken: Ct);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/joined_rooms", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetJoinedRoomsAsync_ParsesSpecExample()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"joined_rooms":["!foo:example.com"]}""");

        var rooms = await CreateClient(handler).GetJoinedRoomsAsync(cancellationToken: Ct);

        Assert.Equal(["!foo:example.com"], rooms);
    }

    [Fact]
    public async Task JoinRoomAsync_PostsToEncodedAliasAndReturnsRoomId()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"room_id":"!abc:example.org"}""");

        var roomId = await CreateClient(handler).JoinRoomAsync("#room:example.org", cancellationToken: Ct);

        Assert.Equal("!abc:example.org", roomId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/join/%23room%3Aexample.org",
            handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("{}", handler.RequestBody);
    }

    // The spec allows both with the same values, so no version detection is needed (D26)
    [Fact]
    public async Task JoinRoomAsync_SendsServersAsViaAndServerName()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"room_id":"!abc:example.org"}""");

        await CreateClient(handler).JoinRoomAsync("!abc:example.org", ["example.org", "other.example"], "Hello", cancellationToken: Ct);

        Assert.Equal("?via=example.org&via=other.example&server_name=example.org&server_name=other.example",
            handler.Request!.RequestUri!.Query);
        Assert.Equal("""{"reason":"Hello"}""", handler.RequestBody);
    }

    [Fact]
    public async Task JoinRoomAsync_RejectsMissingArguments()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"room_id":"!abc:example.org"}""");
        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.JoinRoomAsync("", cancellationToken: Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.JoinRoomAsync("!abc:example.org", [""], cancellationToken: Ct));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task LeaveRoomAsync_PostsToEncodedRoomPath()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        await CreateClient(handler).LeaveRoomAsync("!abc:example.org", cancellationToken: Ct);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/rooms/%21abc%3Aexample.org/leave",
            handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("{}", handler.RequestBody);
    }

    [Fact]
    public async Task LeaveRoomAsync_SendsReason()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        await CreateClient(handler).LeaveRoomAsync("!abc:example.org", "Moving on", cancellationToken: Ct);

        Assert.Equal("""{"reason":"Moving on"}""", handler.RequestBody);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task LeaveRoomAsync_RejectsMissingRoomId(string? roomId)
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateClient(handler).LeaveRoomAsync(roomId!, cancellationToken: Ct));

        Assert.Null(handler.Request);
    }
}
