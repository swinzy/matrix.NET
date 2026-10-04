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

        await CreateClient(handler).GetJoinedRoomsAsync(Ct);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/joined_rooms", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetJoinedRoomsAsync_ParsesSpecExample()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"joined_rooms":["!foo:example.com"]}""");

        var rooms = await CreateClient(handler).GetJoinedRoomsAsync(Ct);

        Assert.Equal(["!foo:example.com"], rooms);
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

        await CreateClient(handler).LeaveRoomAsync("!abc:example.org", "Moving on", Ct);

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
