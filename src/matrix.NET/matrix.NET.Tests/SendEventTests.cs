using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet.Tests;

public partial class SendEventTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private const string RoomPath = "https://matrix.example.org/_matrix/client/v3/rooms/%21abc%3Aexample.org/send/";
    private const string Sent = """{"event_id":"$sent"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MatrixClient CreateClient(StubHttpMessageHandler handler) => new(Session, new HttpClient(handler));

    [Fact]
    public async Task SendMessageAsync_PutsTextWithTokenAndCreatedTransactionId()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        var result = await CreateClient(handler).SendMessageAsync("!abc:example.org", new TextMessageContent("hi"),
            cancellationToken: Ct);

        Assert.Equal("$sent", result.EventId);
        Assert.Matches("^[0-9a-f]{32}$", result.TransactionId);
        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal(RoomPath + "m.room.message/" + result.TransactionId, handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("""{"msgtype":"m.text","body":"hi"}""", handler.RequestBody);
    }

    [Fact]
    public async Task SendMessageAsync_UsesTheGivenTransactionId()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        var result = await CreateClient(handler).SendMessageAsync("!abc:example.org", new TextMessageContent("hi"),
            "my-txn", Ct);

        Assert.Equal("my-txn", result.TransactionId);
        Assert.EndsWith("/m.room.message/my-txn", handler.Request!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SendMessageAsync_SendsFormattedText()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        await CreateClient(handler).SendMessageAsync("!abc:example.org", new TextMessageContent("*hi*")
        {
            Format = TextMessageContent.HtmlFormat,
            FormattedBody = "<em>hi</em>"
        }, cancellationToken: Ct);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"msgtype":"m.text","body":"*hi*","format":"org.matrix.custom.html","formatted_body":"<em>hi</em>"}"""),
            JsonNode.Parse(handler.RequestBody!)));
    }

    [Fact]
    public async Task SendMessageAsync_RefusesContentThatBreaksTheSpec()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);
        var client = CreateClient(handler);
        var invalid = System.Text.Json.JsonSerializer.Deserialize("""{"body":"hi"}""",
            Transport.MatrixJsonContext.Default.MessageContent)!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendMessageAsync("!abc:example.org", new TextMessageContent("hi") { Format = "x" }, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendMessageAsync("!abc:example.org", invalid, cancellationToken: Ct));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SendMessageAsync_SendsUnknownContentUnchanged()
    {
        const string json = """{"body":"Vote!","msgtype":"org.example.poll","answers":[1,2]}""";
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);
        var unknown = System.Text.Json.JsonSerializer.Deserialize(json, Transport.MatrixJsonContext.Default.MessageContent)!;

        await CreateClient(handler).SendMessageAsync("!abc:example.org", unknown, cancellationToken: Ct);

        Assert.Equal(json, handler.RequestBody);
    }

    [Fact]
    public async Task SendEventAsync_SendsJsonObjectUnchangedAndEncodesTheEventType()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);
        var content = new JsonObject { ["Move"] = "e2e4", ["m.relates_to"] = new JsonObject() };

        await CreateClient(handler).SendEventAsync("!abc:example.org", "com.example/game", content, "t1", Ct);

        Assert.Equal(RoomPath + "com.example%2Fgame/t1", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("""{"Move":"e2e4","m.relates_to":{}}""", handler.RequestBody);
    }

    private record GameMove(string FromSquare, string ToSquare, string? Promotion = null);

    [Fact]
    public async Task SendEventAsync_ByReflectionUsesTheLibraryNamingPolicy()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        await CreateClient(handler).SendEventAsync("!abc:example.org", "com.example.game.move",
            new GameMove("e2", "e4"), cancellationToken: Ct);

        Assert.Equal("""{"from_square":"e2","to_square":"e4"}""", handler.RequestBody);
    }

    [JsonSerializable(typeof(GameMove))]
    private partial class AppContext : JsonSerializerContext;

    [Fact]
    public async Task SendEventAsync_WithTypeInfoUsesTheAppsOwnNaming()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        await CreateClient(handler).SendEventAsync("!abc:example.org", "com.example.game.move",
            new GameMove("e2", "e4"), AppContext.Default.GameMove, cancellationToken: Ct);

        Assert.Equal("""{"FromSquare":"e2","ToSquare":"e4","Promotion":null}""", handler.RequestBody);
    }

    [Fact]
    public async Task SendEventAsync_RefusesContentThatIsNotAnObject()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateClient(handler).SendEventAsync("!abc:example.org", "com.example.x", "just text", cancellationToken: Ct));

        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData("", "com.example.x", null)]
    [InlineData("!abc:example.org", "", null)]
    [InlineData("!abc:example.org", "com.example.x", "")]
    public async Task SendEventAsync_RefusesMissingArguments(string roomId, string eventType, string? transactionId)
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, Sent);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            CreateClient(handler).SendEventAsync(roomId, eventType, new JsonObject(), transactionId, Ct));

        Assert.Null(handler.Request);
    }

    // The transaction ID is bound before the call, so the retry after a refresh sends the same path (D28)
    [Fact]
    public async Task RetryAfterRefresh_ReusesTheTransactionId()
    {
        var paths = new List<string>();
        var handler = new CallbackHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (path.EndsWith("/refresh"))
                return (HttpStatusCode.OK, """{"access_token":"new-access"}""");
            return request.Headers.Authorization!.Parameter == "new-access"
                ? (HttpStatusCode.OK, Sent)
                : (HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN","soft_logout":true}""");
        });
        var client = new MatrixClient(Session with { RefreshToken = "refresh" }, new HttpClient(handler),
            new ClientOptions { SessionRefreshHandler = new DiscardingSessionRefreshHandler() });

        var result = await client.SendMessageAsync("!abc:example.org", new TextMessageContent("hi"), cancellationToken: Ct);

        Assert.Equal(3, paths.Count);
        Assert.EndsWith("/refresh", paths[1]);
        Assert.Equal(paths[0], paths[2]);
        Assert.EndsWith("/" + result.TransactionId, paths[2]);
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = respond(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
