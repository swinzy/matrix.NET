using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

public class RedactEventTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private const string RedactPath =
        "https://matrix.example.org/_matrix/client/v3/rooms/%21abc%3Aexample.org/redact/%24event%2Fwith%2Fslashes/";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StubHttpMessageHandler Redacted() => new(HttpStatusCode.OK, """{"event_id":"$redaction"}""");

    private static MatrixClient CreateClient(StubHttpMessageHandler handler) => new(Session, new HttpClient(handler));

    [Fact]
    public async Task RedactEventAsync_PutsTheReasonWithACreatedTransactionId()
    {
        var handler = Redacted();

        var result = await CreateClient(handler).RedactEventAsync("!abc:example.org", "$event/with/slashes",
            "Spam", cancellationToken: Ct);

        Assert.Equal("$redaction", result.EventId);
        Assert.Matches("^[0-9a-f]{32}$", result.TransactionId);
        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal(RedactPath + result.TransactionId, handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("""{"reason":"Spam"}""", handler.RequestBody);
    }

    [Fact]
    public async Task RedactEventAsync_SendsAnEmptyObjectWithoutReasonAndUsesTheGivenId()
    {
        var handler = Redacted();

        var result = await CreateClient(handler).RedactEventAsync("!abc:example.org", "$event/with/slashes",
            transactionId: "my-txn", cancellationToken: Ct);

        Assert.Equal("my-txn", result.TransactionId);
        Assert.Equal(RedactPath + "my-txn", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("{}", handler.RequestBody);
    }

    [Theory]
    [InlineData("", "$event", null)]
    [InlineData("!abc:example.org", "", null)]
    [InlineData("!abc:example.org", "$event", "")]
    public async Task RedactEventAsync_RejectsEmptyArgumentsAndSendsNothing(string roomId, string eventId,
        string? transactionId)
    {
        var handler = Redacted();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateClient(handler).RedactEventAsync(roomId, eventId, transactionId: transactionId, cancellationToken: Ct));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task RedactEventAsync_ThrowsTheHomeserversRefusal()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden,
            """{"errcode":"M_FORBIDDEN","error":"You don't have permission to redact events"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateClient(handler).RedactEventAsync("!abc:example.org", "$event", cancellationToken: Ct));

        Assert.Equal(MatrixErrorCodes.Forbidden, exception.ErrorCode);
    }
}
