using System.Net;
using System.Runtime.CompilerServices;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Syncs continuously: each response is handed to the app, and errors that may go away are retried
/// with backoff (D34).
/// </summary>
/// <remarks>
/// The loop runs only while the app enumerates <see cref="RunAsync"/>: there is no background task,
/// and nothing to dispose. Each request is sent once the app asks for the next response, so the app
/// is never handed more than it can process.
/// </remarks>
/// <example>
/// <code>
/// var loop = new SyncLoop(client, new SyncLoopOptions { Since = LoadSavedToken() });
/// loop.StateChanged += (_, e) => ShowConnectionStatus(e.State);
///
/// await foreach (var sync in loop.RunAsync(cancellationToken))
/// {
///     Handle(sync);
///     SaveToken(sync.NextBatch); // after handling, so a crash repeats the batch rather than losing it
/// }
/// </code>
/// </example>
public sealed class SyncLoop
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan DefaultInitialSyncTimeout = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    private int _running;
    private volatile string? _since;
    private volatile SyncLoopState _state;

    /// <param name="client">The client to sync with.</param>
    /// <param name="options">Where to start and how to sync; defaults when omitted.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The timeout is negative, or the initial sync timeout is neither positive nor infinite.
    /// </exception>
    public SyncLoop(MatrixClient client, SyncLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        options ??= new SyncLoopOptions();
        if (options.Timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "The sync timeout cannot be negative.");
        if (options.InitialSyncTimeout <= TimeSpan.Zero && options.InitialSyncTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), options.InitialSyncTimeout,
                "The initial sync timeout must be positive, or Timeout.InfiniteTimeSpan.");

        Client = client;
        Options = options;
        _since = options.Since;
    }

    /// <summary>The client this loop syncs with.</summary>
    public MatrixClient Client { get; }

    /// <summary>The options this loop was created with.</summary>
    public SyncLoopOptions Options { get; }

    /// <summary>What the loop is doing.</summary>
    public SyncLoopState State => _state;

    /// <summary>
    /// Where the next request continues from: <see cref="SyncLoopOptions.Since"/> at first, then the
    /// <see cref="SyncResponse.NextBatch"/> of each response once the app asks for the next one. So
    /// running the loop again repeats the last response if the app stopped before finishing it.
    /// </summary>
    public string? Since => _since;

    /// <summary>
    /// Raised when <see cref="State"/> changes, and again for each further failed attempt while
    /// <see cref="SyncLoopState.Retrying"/>, with the new <see cref="SyncLoopStateChangedEventArgs.Attempt"/>
    /// and <see cref="SyncLoopStateChangedEventArgs.RetryDelay"/>. Meant for showing the connection
    /// status.
    /// </summary>
    /// <remarks>
    /// Handlers run synchronously on the thread running the loop, so UI apps must marshal to their UI
    /// thread. Anything a handler throws is swallowed, as with <see cref="MatrixClient.SessionChanged"/>;
    /// an <c>async</c> handler must wrap its whole body in <c>try</c>/<c>catch</c>.
    /// </remarks>
    public event EventHandler<SyncLoopStateChangedEventArgs>? StateChanged;

    // Replaced by tests, so retries do not really wait
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    // Replaced by tests, so the backoff's jitter is predictable
    internal Func<double> Random { get; init; } = Backoff.SharedRandom;

    /// <summary>
    /// Syncs until cancelled or until an error that retrying cannot fix, handing each response to the
    /// app. Starting from no <see cref="Since"/>, the first request is an initial sync, which answers
    /// once the homeserver has built it and may take minutes for a large account: the loop is
    /// <see cref="SyncLoopState.InitialSyncing"/> meanwhile, and waits up to
    /// <see cref="SyncLoopOptions.InitialSyncTimeout"/>. Every later request long-polls for
    /// <see cref="SyncLoopOptions.Timeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Network failures, local timeouts, 5xx responses, rate limits (429) and a locked account are
    /// retried indefinitely. The loop waits about 1 second after the first failure, doubling up to about
    /// 1 minute, with jitter so clients do not all reconnect at once; or as long as the homeserver asks
    /// (<see cref="MatrixException.RetryAfter"/>), however long that is. Each failure is reported through
    /// <see cref="StateChanged"/>. The loop is the only layer that retries its requests (D34).
    /// </para>
    /// <para>
    /// Anything else ends the enumeration by throwing, e.g. <see cref="MatrixUnknownTokenException"/>
    /// when the session has ended, another <see cref="MatrixException"/> such as an invalid filter, or
    /// a response that cannot be read. Breaking out of <c>await foreach</c> stops the loop as well.
    /// </para>
    /// <para>Only one enumeration may run at a time; the loop can be run again after it ends.</para>
    /// </remarks>
    /// <param name="cancellationToken">Stops the loop, throwing <see cref="OperationCanceledException"/>.</param>
    /// <exception cref="InvalidOperationException">The loop is already running, or the client can no longer be used.</exception>
    public async IAsyncEnumerable<SyncResponse> RunAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            throw new InvalidOperationException("The sync loop is already running.");

        try
        {
            var failures = 0;
            for (;;)
            {
                var since = _since;
                // A retried initial sync may take minutes again, so the app is told each time it starts
                if (since is null)
                    ChangeState(new SyncLoopStateChangedEventArgs(SyncLoopState.InitialSyncing));
                else if (failures == 0)
                    ChangeState(new SyncLoopStateChangedEventArgs(SyncLoopState.Syncing));

                SyncResponse response;
                try
                {
                    // The same parameters on every retry, so a homeserver that caches the response, as
                    // Synapse does, can answer a retried initial sync from the work already done
                    response = await Client.SyncCoreAsync(new SyncRequest
                    {
                        Since = since,
                        Timeout = since is null ? TimeSpan.Zero : Options.Timeout,
                        Filter = Options.Filter
                    }, retryRateLimited: false, since is null ? Options.InitialSyncTimeout : null, cancellationToken);
                }
                catch (Exception e) when (IsTransient(e, cancellationToken))
                {
                    failures++;
                    var delay = GetRetryDelay(e, failures, Random());
                    ChangeState(new SyncLoopStateChangedEventArgs(SyncLoopState.Retrying, e, failures, delay));
                    await Delay(delay, cancellationToken);
                    continue;
                }

                if (failures > 0 || since is null)
                {
                    failures = 0;
                    ChangeState(new SyncLoopStateChangedEventArgs(SyncLoopState.Syncing));
                }

                yield return response;

                // Only once the app asks for more has it finished with this response
                _since = response.NextBatch;
            }
        }
        finally
        {
            ChangeState(new SyncLoopStateChangedEventArgs(SyncLoopState.Stopped));
            Volatile.Write(ref _running, 0);
        }
    }

    private static bool IsTransient(Exception e, CancellationToken cancellationToken) => e switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        // The client stays usable and detects the unlock with the next successful request (D22)
        MatrixUserLockedException => true,
        // The session has ended; nothing a retry can fix
        MatrixUnknownTokenException => false,
        MatrixException { StatusCode: HttpStatusCode.TooManyRequests } => true,
        MatrixException matrix => (int)matrix.StatusCode >= 500,
        HttpRequestException => true,
        // The request's own timeout, as opposed to the app's cancellation
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false
    };

    internal static TimeSpan GetRetryDelay(Exception error, int failures, double random) =>
        error is MatrixException { RetryAfter: { } retryAfter }
            ? retryAfter
            : Backoff.Next(FirstRetryDelay, failures - 1, MaxRetryDelay, random);

    private void ChangeState(SyncLoopStateChangedEventArgs change)
    {
        if (change.State == _state && change.State != SyncLoopState.Retrying)
            return;

        _state = change.State;
        if (StateChanged is not { } handlers)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<SyncLoopStateChangedEventArgs>>())
        {
            try
            {
                handler(this, change);
            }
            catch
            {
                // Handlers must handle their own errors; see StateChanged
            }
        }
    }
}
