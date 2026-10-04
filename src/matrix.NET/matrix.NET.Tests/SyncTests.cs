using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

public class SyncTests
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
    public async Task SyncAsync_WithoutParametersIsAnImmediateInitialSync()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"next_batch":"s1"}""");

        var response = await CreateClient(handler).SyncAsync(cancellationToken: Ct);

        Assert.Equal("s1", response.NextBatch);
        Assert.Null(response.Rooms);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/sync", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task SyncAsync_SendsParametersAsQuery()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"next_batch":"s2"}""");

        await CreateClient(handler).SyncAsync(new SyncRequest
        {
            Since = "s1",
            Timeout = TimeSpan.FromSeconds(30),
            Filter = """{"room":{"timeline":{"limit":10}}}""",
            FullState = true
        }, Ct);

        Assert.Equal(
            "?since=s1&filter=%7B%22room%22%3A%7B%22timeline%22%3A%7B%22limit%22%3A10%7D%7D%7D&full_state=true&timeout=30000",
            handler.Request!.RequestUri!.Query);
    }

    [Fact]
    public async Task SyncAsync_RejectsNegativeTimeout()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"next_batch":"s1"}""");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateClient(handler).SyncAsync(new SyncRequest { Timeout = TimeSpan.FromSeconds(-1) }, Ct));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SyncAsync_ReadsTimelinesAndKeepsWhatIsNotModelled()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """
            {
              "next_batch": "s72595_4483_1934",
              "presence": {"events": []},
              "account_data": {"events": []},
              "rooms": {
                "join": {
                  "!joined:example.org": {
                    "state": {"events": []},
                    "summary": {"m.joined_member_count": 2},
                    "timeline": {
                      "limited": true,
                      "prev_batch": "t34-23535_0_0",
                      "events": [
                        {"content":{"body":"hi","msgtype":"m.text"},"event_id":"$1","origin_server_ts":1,"sender":"@a:example.org","type":"m.room.message"},
                        {"content":{"algorithm":"m.megolm.v1.aes-sha2"},"event_id":"$2","origin_server_ts":2,"sender":"@a:example.org","type":"m.room.encrypted"},
                        {"content":{"name":"Lounge"},"event_id":"$3","origin_server_ts":3,"sender":"@a:example.org","state_key":"","type":"m.room.name"},
                        {"event_id":"$4"}
                      ]
                    }
                  }
                },
                "leave": {
                  "!left:example.org": {
                    "timeline": {"events": [
                      {"content":{"membership":"leave"},"event_id":"$5","origin_server_ts":5,"sender":"@joe:example.org","state_key":"@joe:example.org","type":"m.room.member"}
                    ]}
                  }
                },
                "invite": {"!invited:example.org": {"invite_state": {"events": []}}}
              }
            }
            """);

        var response = await CreateClient(handler).SyncAsync(cancellationToken: Ct);

        Assert.Equal(["presence", "account_data"], response.AdditionalProperties!.Keys);
        Assert.Equal(["invite"], response.Rooms!.AdditionalProperties!.Keys);
        var joined = response.Rooms.Join!["!joined:example.org"];
        Assert.Equal(["state", "summary"], joined.AdditionalProperties!.Keys);
        Assert.True(joined.Timeline!.Limited);
        Assert.Equal("t34-23535_0_0", joined.Timeline.PrevBatch);
        Assert.Collection(joined.Timeline.Events,
            e => Assert.IsType<MessageEvent>(e),
            e => Assert.IsType<EncryptedEvent>(e),
            e => Assert.True(Assert.IsType<UnknownRoomEvent>(e).IsState),
            e => Assert.IsType<InvalidRoomEvent>(e));
        var left = Assert.Single(response.Rooms.Leave!["!left:example.org"].Timeline!.Events);
        Assert.Equal("m.room.member", left.Type);
    }
}
