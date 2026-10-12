using System.Net;
using System.Text;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Retrying requests the homeserver refused for rate limiting (D37).</summary>
public class RateLimitRetryTests
{
    private static readonly Uri Homeserver = new("https://matrix.example.org/");

    private static readonly MatrixSession Session = new()
    {
        Homeserver = Homeserver,
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static readonly Endpoint WhoAmI =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/account/whoami", AuthRequirement.None);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Func<HttpResponseMessage> Ok() =>
        Respond(HttpStatusCode.OK, """{"user_id":"@joe:example.org"}""");

    private static Func<HttpResponseMessage> Limited(string? retryAfterHeader = null, string? retryAfterMs = null) =>
        Respond((HttpStatusCode)429, retryAfterMs is null
            ? """{"errcode":"M_LIMIT_EXCEEDED","error":"Too Many Requests"}"""
            : $$"""{"errcode":"M_LIMIT_EXCEEDED","error":"Too Many Requests","retry_after_ms":{{retryAfterMs}}}""",
            retryAfterHeader);

    private static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body, string? retryAfter = null) => () =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    };

    private sealed class Harness(params Func<HttpResponseMessage>[] responses)
    {
        public SequenceHandler Handler { get; } = new(responses);
        public List<TimeSpan> Waits { get; } = [];

        public MatrixTransport Transport(int maxRetries = 3, TimeSpan? maxWait = null) =>
            new(Homeserver, () => new HttpClient(Handler), rateLimitRetry: new RateLimitRetry(maxRetries,
                maxWait ?? TimeSpan.FromMinutes(1), (wait, _) =>
                {
                    Waits.Add(wait);
                    return Task.CompletedTask;
                }));
    }

    [Fact]
    public async Task SendAsync_WaitsAsAskedAndRetries()
    {
        var harness = new Harness(Limited(retryAfterHeader: "2"), Limited(retryAfterMs: "1500"), Ok());

        var response = await harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct);

        Assert.Equal("@joe:example.org", response.UserId);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1500)], harness.Waits);
        Assert.Equal(3, harness.Handler.Count);
    }

    [Fact]
    public async Task SendAsync_BacksOffWhenNoWaitIsGiven()
    {
        var harness = new Harness(Limited(), Limited(), Limited(), Ok());

        await harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], harness.Waits);
    }

    [Fact]
    public async Task SendAsync_ThrowsTheLastRefusalOnceRetriesAreUsedUp()
    {
        var harness = new Harness(Limited("1"), Limited("1"), Limited("1"), Limited("5"));

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct));

        Assert.Equal(MatrixErrorCodes.LimitExceeded, exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(5), exception.RetryAfter);
        Assert.Equal(3, harness.Waits.Count);
        Assert.Equal(4, harness.Handler.Count);
    }

    // Synapse's login limit asks for over five minutes; a call must not stall that long unasked
    [Fact]
    public async Task SendAsync_LeavesLongWaitsToTheApp()
    {
        var harness = new Harness(Limited(retryAfterHeader: "334"));

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct));

        Assert.Equal(TimeSpan.FromSeconds(334), exception.RetryAfter);
        Assert.Empty(harness.Waits);
        Assert.Equal(1, harness.Handler.Count);
    }

    [Fact]
    public async Task SendAsync_WaitsUpToTheLimitInclusive()
    {
        var harness = new Harness(Limited(retryAfterHeader: "60"), Ok());

        await harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct);

        Assert.Equal([TimeSpan.FromMinutes(1)], harness.Waits);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryWhenTurnedOff()
    {
        var harness = new Harness(Limited("1"), Ok());

        await Assert.ThrowsAsync<MatrixException>(() =>
            harness.Transport(maxRetries: 0).SendAsync<WhoAmIResponse>(WhoAmI, Ct));

        Assert.Empty(harness.Waits);
    }

    // Some homeservers or proxies answer 429 without a Matrix body, or M_LIMIT_EXCEEDED with another status
    [Fact]
    public async Task SendAsync_RecognisesRateLimitsByStatusOrErrorCode()
    {
        var harness = new Harness(
            Respond((HttpStatusCode)429, "<html>Slow down</html>"),
            Respond(HttpStatusCode.BadRequest, """{"errcode":"M_LIMIT_EXCEEDED"}"""),
            Ok());

        await harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct);

        Assert.Equal(2, harness.Waits.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"errcode":"M_UNKNOWN"}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>Down</html>")]
    [InlineData(HttpStatusCode.Forbidden, """{"errcode":"M_FORBIDDEN"}""")]
    public async Task SendAsync_DoesNotRetryOtherErrors(HttpStatusCode status, string body)
    {
        var harness = new Harness(Respond(status, body, retryAfter: "1"), Ok());

        await Assert.ThrowsAsync<MatrixException>(() => harness.Transport().SendAsync<WhoAmIResponse>(WhoAmI, Ct));

        Assert.Empty(harness.Waits);
        Assert.Equal(1, harness.Handler.Count);
    }

    [Fact]
    public async Task SendAsync_CancellationStopsTheWait()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var handler = new SequenceHandler([Limited("30"), Ok()]);
        var transport = new MatrixTransport(Homeserver, () => new HttpClient(handler),
            rateLimitRetry: new RateLimitRetry(3, TimeSpan.FromMinutes(1), Task.Delay));

        var send = transport.SendAsync<WhoAmIResponse>(WhoAmI, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Options_ReachTheClientsRequests()
    {
        var handler = new SequenceHandler([Limited("1"), Ok()]);
        var waits = new List<TimeSpan>();
        var client = new MatrixClient(Session, new HttpClient(handler), new ClientOptions
        {
            MaxRateLimitRetries = 1,
            RateLimitDelay = (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            }
        });

        await client.WhoAmIAsync(cancellationToken: Ct);

        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
    }

    [Fact]
    public void Options_RejectNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatrixServer(Homeserver, new ServerOptions { MaxRateLimitRetries = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatrixClient(Session, new ClientOptions { MaxRateLimitWait = TimeSpan.FromSeconds(-1) }));
    }

    /// <summary>Answers each request with the next scripted response, counting them.</summary>
    private sealed class SequenceHandler(Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _count;

        public int Count => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _count) - 1;
            return index < responses.Length
                ? Task.FromResult(responses[index]())
                : throw new InvalidOperationException("The test sent more requests than it scripted.");
        }
    }
}
