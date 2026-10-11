using System.Net;
using System.Text;
using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Tests;

public class SyncLoopTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Func<HttpResponseMessage> Ok(string nextBatch) =>
        Respond(HttpStatusCode.OK, $$"""{"next_batch":"{{nextBatch}}"}""");

    private static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body,
        string? retryAfter = null) => () =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    };

    private static Func<HttpResponseMessage> Throw(Exception exception) => () => throw exception;

    /// <summary>A loop over a scripted homeserver, recording each wait and state change instead of waiting.</summary>
    private sealed class Harness
    {
        public Harness(SyncLoopOptions? options, params Func<HttpResponseMessage>[] responses)
        {
            Handler = new ScriptedHandler(responses);
            Client = new MatrixClient(Session, new HttpClient(Handler));
            Loop = new SyncLoop(Client, options)
            {
                Delay = (delay, _) =>
                {
                    Delays.Add(delay);
                    return Task.CompletedTask;
                }
            };
            Loop.StateChanged += (_, e) => Changes.Add(e);
        }

        public ScriptedHandler Handler { get; }
        public MatrixClient Client { get; }
        public SyncLoop Loop { get; }
        public List<TimeSpan> Delays { get; } = [];
        public List<SyncLoopStateChangedEventArgs> Changes { get; } = [];

        /// <summary>Runs the loop until it has handed over <paramref name="count"/> responses.</summary>
        public async Task<List<string>> TakeAsync(int count)
        {
            var batches = new List<string>();
            await foreach (var sync in Loop.RunAsync(Ct))
            {
                batches.Add(sync.NextBatch);
                if (batches.Count == count)
                    break;
            }

            return batches;
        }
    }

    [Fact]
    public async Task RunAsync_StartsWithImmediateInitialSyncThenLongPolls()
    {
        var harness = new Harness(null, Ok("s1"), Ok("s2"), Ok("s3"));

        var batches = await harness.TakeAsync(3);

        Assert.Equal(["s1", "s2", "s3"], batches);
        Assert.Equal(["?timeout=0", "?since=s1&timeout=30000", "?since=s2&timeout=30000"], harness.Handler.Queries);
    }

    [Fact]
    public async Task RunAsync_ContinuesFromGivenSinceWithOptions()
    {
        var harness = new Harness(new SyncLoopOptions
        {
            Since = "saved",
            Timeout = TimeSpan.FromSeconds(10),
            Filter = "f1"
        }, Ok("s1"));

        await harness.TakeAsync(1);

        Assert.Equal(["?since=saved&filter=f1&timeout=10000"], harness.Handler.Queries);
    }

    [Fact]
    public async Task RunAsync_AdvancesSinceOnlyWhenTheAppAsksForMore()
    {
        var harness = new Harness(null, Ok("s1"), Ok("s2"), Ok("s2-again"));

        await using (var enumerator = harness.Loop.RunAsync(Ct).GetAsyncEnumerator(Ct))
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Null(harness.Loop.Since);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("s1", harness.Loop.Since);
        }

        // Stopped while handling s2, so running again repeats it rather than skipping it
        await harness.TakeAsync(1);
        Assert.Equal("?since=s1&timeout=30000", harness.Handler.Queries[^1]);
    }

    [Fact]
    public async Task RunAsync_RetriesTransientErrorsWithDoublingDelay()
    {
        var harness = new Harness(null,
            Throw(new HttpRequestException("Connection refused")),
            Respond(HttpStatusCode.BadGateway, "<html>Bad gateway</html>"),
            Throw(new TaskCanceledException("Timed out", new TimeoutException())),
            Respond(HttpStatusCode.InternalServerError, """{"errcode":"M_UNKNOWN"}"""),
            Ok("s1"));

        var batches = await harness.TakeAsync(1);

        Assert.Equal(["s1"], batches);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)],
            harness.Delays);
        Assert.Equal(5, harness.Handler.Queries.Count);
    }

    [Fact]
    public async Task RunAsync_WaitsAsLongAsTheHomeserverAsks()
    {
        var harness = new Harness(null,
            Respond((HttpStatusCode)429, """{"errcode":"M_LIMIT_EXCEEDED","retry_after_ms":4500}"""),
            Respond((HttpStatusCode)429, """{"errcode":"M_LIMIT_EXCEEDED"}""", retryAfter: "120"),
            Respond((HttpStatusCode)429, """{"errcode":"M_LIMIT_EXCEEDED"}"""),
            Ok("s1"));

        await harness.TakeAsync(1);

        // Without a requested wait, the backoff applies as for any other failure
        Assert.Equal([TimeSpan.FromMilliseconds(4500), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(4)], harness.Delays);
    }

    [Fact]
    public async Task RunAsync_ReportsEachRetryAndRecovery()
    {
        var failure = new HttpRequestException("Connection refused");
        var harness = new Harness(null, Ok("s1"), Throw(failure), Throw(failure), Ok("s2"),
            Throw(failure), Ok("s3"));

        await harness.TakeAsync(3);

        Assert.Equal(
        [
            (SyncLoopState.Syncing, 0), (SyncLoopState.Retrying, 1), (SyncLoopState.Retrying, 2),
            (SyncLoopState.Syncing, 0), (SyncLoopState.Retrying, 1), (SyncLoopState.Syncing, 0),
            (SyncLoopState.Stopped, 0)
        ], harness.Changes.Select(c => (c.State, c.Attempt)));
        Assert.All(harness.Changes.Where(c => c.State == SyncLoopState.Retrying), c => Assert.Same(failure, c.Error));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)], harness.Delays);
        Assert.Equal(SyncLoopState.Stopped, harness.Loop.State);
    }

    [Fact]
    public async Task RunAsync_KeepsPollingWhileTheAccountIsLocked()
    {
        var locked = Respond(HttpStatusCode.Unauthorized, """{"errcode":"M_USER_LOCKED","soft_logout":true}""");
        var harness = new Harness(null, locked, locked, Ok("s1"));
        var clientStates = new List<SessionChangeKind>();
        harness.Client.SessionChanged += (_, e) => clientStates.Add(e.Kind);

        await harness.TakeAsync(1);

        Assert.IsType<MatrixUserLockedException>(harness.Changes[1].Error);
        Assert.Equal([SessionChangeKind.Locked, SessionChangeKind.Unlocked], clientStates);
        Assert.Equal(MatrixClientState.Active, harness.Client.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"errcode":"M_INVALID_PARAM","error":"Bad filter"}""", MatrixErrorCodes.InvalidParam)]
    [InlineData(HttpStatusCode.Forbidden, """{"errcode":"M_FORBIDDEN"}""", MatrixErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, "<html>Not found</html>", MatrixErrorCodes.Unknown)]
    public async Task RunAsync_ThrowsErrorsRetryingCannotFix(HttpStatusCode status, string body, string errorCode)
    {
        var harness = new Harness(null, Ok("s1"), Respond(status, body));

        var exception = await Assert.ThrowsAsync<MatrixException>(() => harness.TakeAsync(2));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Empty(harness.Delays);
        Assert.Equal(SyncLoopState.Stopped, harness.Loop.State);
        Assert.Equal("s1", harness.Loop.Since);
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenTheSessionEnds()
    {
        var harness = new Harness(null, Respond(HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN"}"""));

        await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => harness.TakeAsync(1));

        Assert.Equal(MatrixClientState.Invalidated, harness.Client.State);
        Assert.Empty(harness.Delays);
    }

    [Fact]
    public async Task RunAsync_ThrowsOnUnreadableResponse()
    {
        var harness = new Harness(null, Respond(HttpStatusCode.OK, "<html>Captive portal</html>"));

        await Assert.ThrowsAsync<JsonException>(() => harness.TakeAsync(1));
    }

    [Fact]
    public async Task RunAsync_CancellationStopsTheWaitBeforeARetry()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var client = new MatrixClient(Session, new HttpClient(new ScriptedHandler(
            [Throw(new HttpRequestException("Connection refused"))])));
        var loop = new SyncLoop(client);
        loop.StateChanged += (_, e) =>
        {
            if (e.State == SyncLoopState.Retrying)
                cancellation.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in loop.RunAsync(cancellation.Token))
            {
            }
        });

        Assert.Equal(SyncLoopState.Stopped, loop.State);
    }

    [Fact]
    public async Task RunAsync_AllowsOnlyOneRunAtATime()
    {
        var harness = new Harness(null, Ok("s1"), Ok("s2"));

        await using (var first = harness.Loop.RunAsync(Ct).GetAsyncEnumerator(Ct))
        {
            Assert.True(await first.MoveNextAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => harness.TakeAsync(1));
            Assert.Equal(SyncLoopState.Syncing, harness.Loop.State);
        }

        Assert.Equal(["s2"], await harness.TakeAsync(1));
    }

    [Fact]
    public async Task RunAsync_IgnoresExceptionsFromStateHandlers()
    {
        var harness = new Harness(null, Throw(new HttpRequestException("Connection refused")), Ok("s1"));
        harness.Loop.StateChanged += (_, _) => throw new InvalidOperationException("Handler bug");

        Assert.Equal(["s1"], await harness.TakeAsync(1));
    }

    [Fact]
    public void Constructor_RejectsNegativeTimeout()
    {
        var client = new MatrixClient(Session, new HttpClient(new ScriptedHandler([])));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyncLoop(client, new SyncLoopOptions { Timeout = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public void GetRetryDelay_DoublesUpToOneMinute()
    {
        var failure = new HttpRequestException();

        var delays = Enumerable.Range(1, 9).Select(n => SyncLoop.GetRetryDelay(failure, n).TotalSeconds);

        Assert.Equal([1, 2, 4, 8, 16, 32, 60, 60, 60], delays);
        Assert.Equal(TimeSpan.FromMinutes(1), SyncLoop.GetRetryDelay(failure, int.MaxValue));
    }

    /// <summary>Answers each request with the next scripted response, and records the query sent.</summary>
    private sealed class ScriptedHandler(Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Queries.Add(request.RequestUri!.Query);
            if (_next == responses.Length)
                throw new InvalidOperationException("The test sent more requests than it scripted.");

            return Task.FromResult(responses[_next++]());
        }
    }
}
