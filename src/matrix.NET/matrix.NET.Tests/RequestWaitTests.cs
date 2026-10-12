using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Reporting waits longer than the threshold to the call's progress (D38).</summary>
public class RequestWaitTests
{
    private static readonly Uri Homeserver = new("https://matrix.example.org/");

    private static readonly MatrixSession Session = new()
    {
        Homeserver = Homeserver,
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Ok(string body = """{"event_id":"$e","user_id":"@joe:example.org"}""") =>
        Json(HttpStatusCode.OK, body);

    private static HttpResponseMessage Limited(int retryAfterMs) =>
        Json((HttpStatusCode)429, $$"""{"errcode":"M_LIMIT_EXCEEDED","retry_after_ms":{{retryAfterMs}}}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ThrowingProgress : IProgress<RequestWait>
    {
        public void Report(RequestWait value) => throw new InvalidOperationException("A bug in the app's handler");
    }

    // Synchronous, so the reports are in the list when the call returns
    private sealed class Recorder : IProgress<RequestWait>
    {
        public List<RequestWait> Waits { get; } = [];

        public void Report(RequestWait value)
        {
            lock (Waits)
                Waits.Add(value);
        }
    }

    /// <summary>
    /// A clock the test moves: rate-limit waits and slow responses advance it. A timer fires only when
    /// it is created already due, which is enough to tell whether a wait would be reported.
    /// </summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

        public Task Delay(TimeSpan wait, CancellationToken _)
        {
            Advance(wait);
            return Task.CompletedTask;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == TimeSpan.Zero)
                callback(state);
            return new NeverTimer();
        }

        private sealed class NeverTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Answers from a script; each step may first advance the clock, as a slow response would.</summary>
    private sealed class ScriptHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        private int _next;

        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(steps[_next++](request));
        }
    }

    private static MatrixClient Client(HttpMessageHandler handler, ManualTime time, TimeSpan? threshold = null,
        MatrixSession? session = null) =>
        new(session ?? Session, new HttpClient(handler), new ClientOptions
        {
            SessionRefreshHandler = new DiscardingSessionRefreshHandler(),
            WaitReportThreshold = threshold ?? Threshold,
            TimeProvider = time,
            RateLimitDelay = time.Delay
        });

    [Fact]
    public async Task RateLimitWaitPastTheThreshold_IsReportedBeforeWaiting()
    {
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(_ => Limited(5000), _ => Ok());

        await Client(handler, time).SendMessageAsync("!room:example.org", new TextMessageContent("hi"),
            progress: progress, cancellationToken: Ct);

        var wait = progress.Waits[0];
        Assert.Equal(RequestWaitReason.RateLimited, wait.Reason);
        Assert.Equal(TimeSpan.FromSeconds(5), wait.ExpectedWait);
        Assert.Equal(TimeSpan.Zero, wait.Elapsed);
        Assert.False(wait.MayHaveTakenEffect);
    }

    [Fact]
    public async Task RequestSentAgainAfterTheThreshold_IsReportedAtOnce()
    {
        // Cancelling during the rate limit is clean; once the request is sent again it no longer is
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(_ => Limited(5000), _ => Ok());

        await Client(handler, time).SendMessageAsync("!room:example.org", new TextMessageContent("hi"),
            progress: progress, cancellationToken: Ct);

        Assert.Equal([RequestWaitReason.RateLimited, RequestWaitReason.AwaitingResponse],
            progress.Waits.Select(w => w.Reason));
        Assert.True(progress.Waits[1].MayHaveTakenEffect);
        Assert.Null(progress.Waits[1].ExpectedWait);
        Assert.Equal(TimeSpan.FromSeconds(5), progress.Waits[1].Elapsed);
    }

    [Fact]
    public async Task ShortWaits_AreNotReported()
    {
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(_ => Limited(1000), _ => Limited(1500), _ => Ok());

        await Client(handler, time).SendMessageAsync("!room:example.org", new TextMessageContent("hi"),
            progress: progress, cancellationToken: Ct);

        Assert.Empty(progress.Waits);
    }

    [Fact]
    public async Task ShortWaitsThatAddUpPastTheThreshold_AreReported()
    {
        // The threshold is for the whole call, which is what the user waits for
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(_ => Limited(2000), _ => Limited(2000), _ => Ok());

        await Client(handler, time).SendMessageAsync("!room:example.org", new TextMessageContent("hi"),
            progress: progress, cancellationToken: Ct);

        Assert.Equal(RequestWaitReason.RateLimited, progress.Waits[0].Reason);
        Assert.Equal(TimeSpan.FromSeconds(2), progress.Waits[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(2), progress.Waits[0].ExpectedWait);
    }

    [Fact]
    public async Task SlowResponse_IsReportedOncePastTheThreshold()
    {
        // Real time: the timer must fire while the request is outstanding
        var progress = new Recorder();
        var release = new TaskCompletionSource();
        var handler = new GateHandler(release.Task);
        var client = new MatrixClient(Session, new HttpClient(handler),
            new ClientOptions { WaitReportThreshold = TimeSpan.FromMilliseconds(50) });

        var call = client.SendMessageAsync("!room:example.org", new TextMessageContent("hi"), progress: progress,
            cancellationToken: Ct);
        await WaitUntilAsync(() => progress.Waits.Count > 0);
        release.SetResult();
        await call;

        var wait = Assert.Single(progress.Waits);
        Assert.Equal(RequestWaitReason.AwaitingResponse, wait.Reason);
        Assert.Null(wait.ExpectedWait);
        Assert.True(wait.MayHaveTakenEffect);
        // Timers may fire a millisecond or so early by the stopwatch, so only roughly
        Assert.True(wait.Elapsed >= TimeSpan.FromMilliseconds(40), wait.Elapsed.ToString());
    }

    [Fact]
    public async Task SlowRead_CannotHaveTakenEffect()
    {
        var progress = new Recorder();
        var release = new TaskCompletionSource();
        var client = new MatrixClient(Session, new HttpClient(new GateHandler(release.Task)),
            new ClientOptions { WaitReportThreshold = TimeSpan.FromMilliseconds(50) });

        var call = client.WhoAmIAsync(progress, Ct);
        await WaitUntilAsync(() => progress.Waits.Count > 0);
        release.SetResult();
        await call;

        Assert.False(Assert.Single(progress.Waits).MayHaveTakenEffect);
    }

    [Fact]
    public async Task FastResponse_IsNotReported()
    {
        var progress = new Recorder();
        var client = new MatrixClient(Session, new HttpClient(new ScriptHandler(_ => Ok())),
            new ClientOptions { WaitReportThreshold = TimeSpan.FromMilliseconds(20) });

        await client.WhoAmIAsync(progress, Ct);
        // A timer that was wrongly left running would fire within this
        await Task.Delay(200, Ct);

        Assert.Empty(progress.Waits);
    }

    [Fact]
    public async Task InfiniteThreshold_ReportsNothing()
    {
        // Within the ten seconds an unreported call may wait (D37), so the wait is still taken
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(_ => Limited(5000), _ => Ok());

        await Client(handler, time, Timeout.InfiniteTimeSpan)
            .SendMessageAsync("!room:example.org", new TextMessageContent("hi"), progress: progress,
                cancellationToken: Ct);

        Assert.Empty(progress.Waits);
    }

    [Fact]
    public async Task TokenRefresh_IsReportedAsWithoutEffect()
    {
        // The rejected request was not carried out, so waiting for the refresh leaves nothing unknown
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(
            _ =>
            {
                time.Advance(TimeSpan.FromSeconds(4));
                return Json(HttpStatusCode.Unauthorized, """{"errcode":"M_UNKNOWN_TOKEN","soft_logout":true}""");
            },
            _ => Ok("""{"access_token":"new-access","refresh_token":"new-refresh"}"""),
            _ => Ok());
        var client = Client(handler, time, session: Session with { RefreshToken = "old-refresh" });

        await client.SendMessageAsync("!room:example.org", new TextMessageContent("hi"), progress: progress,
            cancellationToken: Ct);

        Assert.Equal("/_matrix/client/v3/refresh", handler.Paths[1]);
        Assert.Equal([false, true], progress.Waits.Select(w => w.MayHaveTakenEffect));
        Assert.All(progress.Waits, w => Assert.Equal(RequestWaitReason.AwaitingResponse, w.Reason));
    }

    [Fact]
    public async Task ProgressThatThrows_DoesNotChangeTheResult()
    {
        var time = new ManualTime();
        var handler = new ScriptHandler(_ => Limited(5000), _ => Ok());

        var result = await Client(handler, time).SendMessageAsync("!room:example.org", new TextMessageContent("hi"),
            progress: new ThrowingProgress(),
            cancellationToken: Ct);

        Assert.Equal("$e", result.EventId);
    }

    [Fact]
    public async Task ServerCalls_ReportWaitsToo()
    {
        var time = new ManualTime();
        var progress = new Recorder();
        var handler = new ScriptHandler(_ => Limited(5000), _ => Ok("""{"versions":["v1.1"]}"""));
        var server = new MatrixServer(Homeserver, new HttpClient(handler),
            new ServerOptions { TimeProvider = time, RateLimitDelay = time.Delay });

        await server.GetVersionsAsync(progress, Ct);

        Assert.Equal([RequestWaitReason.RateLimited, RequestWaitReason.AwaitingResponse],
            progress.Waits.Select(w => w.Reason));
        Assert.All(progress.Waits, w => Assert.False(w.MayHaveTakenEffect));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1000)]
    public void NegativeThreshold_IsRejected(int milliseconds)
    {
        var options = new ClientOptions { WaitReportThreshold = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new MatrixClient(Session, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatrixServer(Homeserver,
            new ServerOptions { WaitReportThreshold = TimeSpan.FromMilliseconds(milliseconds) }));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>Holds every request until released.</summary>
    private sealed class GateHandler(Task release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await release.WaitAsync(cancellationToken);
            return Ok();
        }
    }
}
