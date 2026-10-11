using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Reads events back through sync. Local only, as it creates a room.</summary>
[Collection(nameof(SyncIntegrationTests))]
public class SyncIntegrationTests(LoggedInClientFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sync_ReadsMessagesRedactionsAndEncryptedEvents()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var roomId = await TestRooms.CreateAsync(client, Ct);
        // Room creation alone adds several events, so a default-sized initial timeline could miss the message
        var onlyThisRoom = """{"room":{"rooms":[""" + $"\"{roomId}\"" + """],"timeline":{"limit":50}}}""";
        var since = (await client.SyncAsync(new SyncRequest { Filter = onlyThisRoom }, Ct)).NextBatch;

        // A message comes back with the transaction ID it was sent with (D28)
        var sent = await client.SendMessageAsync(roomId, new TextMessageContent("matrix.NET sync test"), cancellationToken: Ct);
        var sync = await client.SyncAsync(new SyncRequest { Since = since, Filter = onlyThisRoom }, Ct);
        var message = Assert.Single(Events(sync, roomId).OfType<MessageEvent>(), e => e.EventId == sent.EventId);
        Assert.Equal("matrix.NET sync test", message.Content.Body);
        Assert.Equal(sent.TransactionId, message.Unsigned?.TransactionId);
        Assert.Equal(client.UserId, message.Sender);

        // A later redaction arrives as its own event
        await TestRooms.RedactAsync(client.Session, roomId, sent.EventId, Ct);
        sync = await client.SyncAsync(new SyncRequest { Since = sync.NextBatch, Filter = onlyThisRoom }, Ct);
        var redaction = Assert.Single(Events(sync, roomId).OfType<RedactionEvent>());
        Assert.Equal(sent.EventId, redaction.Redacts);

        await client.SendEventAsync(roomId, "m.room.encrypted",
            new JsonObject { ["algorithm"] = "m.megolm.v1.aes-sha2", ["ciphertext"] = "AwgAEnAC", ["session_id"] = "s" },
            cancellationToken: Ct);
        sync = await client.SyncAsync(new SyncRequest { Since = sync.NextBatch, Filter = onlyThisRoom }, Ct);
        Assert.Equal("m.megolm.v1.aes-sha2", Assert.Single(Events(sync, roomId).OfType<EncryptedEvent>()).Algorithm);

        // Seen afresh, the message is already redacted. FullState makes the request differ from the first
        // one, whose response Synapse may still have cached
        var initial = await client.SyncAsync(new SyncRequest { Filter = onlyThisRoom, FullState = true }, Ct);
        var redacted = Assert.Single(Events(initial, roomId).OfType<RedactedRoomEvent>(), e => e.EventId == sent.EventId);
        Assert.Equal("m.room.message", redacted.Type);
        Assert.DoesNotContain(Events(initial, roomId), e => e is InvalidRoomEvent);

        await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
    }

    [Fact]
    public async Task Sync_LongPollsUntilTheTimeout()
    {
        var client = await fixture.GetClientAsync();
        // Other tests' logins and logouts change the account's device list, which no filter can exclude
        // and which ends a long poll, so this class runs alone (see the collection below)
        // Filters everything out, so that nothing, not even the account's own presence, ends the wait early.
        // The unused not_rooms entry makes the filter unique, so Synapse cannot answer the first request
        // with a response cached by an earlier run, whose since token would end the wait at once
        var nothing = """{"presence":{"types":[]},"account_data":{"types":[]},"room":{"rooms":[],"not_rooms":["!""" +
                      Guid.NewGuid().ToString("N") + """:localhost"]}}""";
        var since = (await client.SyncAsync(new SyncRequest { Filter = nothing }, Ct)).NextBatch;

        var stopwatch = Stopwatch.StartNew();
        var response = await client.SyncAsync(new SyncRequest { Since = since, Timeout = TimeSpan.FromSeconds(2), Filter = nothing }, Ct);

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(30));
        Assert.NotEmpty(response.NextBatch);
    }

    [Fact]
    public async Task SyncLoop_HandsOverAMessageSentWhileItWaits()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var roomId = await TestRooms.CreateAsync(client, Ct);
        var onlyThisRoom = """{"room":{"rooms":[""" + $"\"{roomId}\"" + """]}}""";
        var since = (await client.SyncAsync(new SyncRequest { Filter = onlyThisRoom }, Ct)).NextBatch;
        var loop = new SyncLoop(client, new SyncLoopOptions { Since = since, Filter = onlyThisRoom });
        var states = new List<SyncLoopState>();
        loop.StateChanged += (_, e) => states.Add(e.State);

        // Sent once the loop's first long poll is waiting, which the message must end early
        var stopwatch = Stopwatch.StartNew();
        var send = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
            return await client.SendMessageAsync(roomId, new TextMessageContent("matrix.NET sync loop test"),
                cancellationToken: Ct);
        }, Ct);
        MessageEvent? received = null;
        await foreach (var sync in loop.RunAsync(Ct))
        {
            received = Events(sync, roomId).OfType<MessageEvent>().FirstOrDefault();
            if (received is not null)
                break;
        }

        Assert.Equal((await send).EventId, received!.EventId);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(20));
        Assert.Equal([SyncLoopState.Syncing, SyncLoopState.Stopped], states);

        await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
    }

    private static IEnumerable<RoomEvent> Events(SyncResponse sync, string roomId) =>
        sync.Rooms?.Join?.GetValueOrDefault(roomId)?.Timeline?.Events ?? [];
}

/// <summary>Runs <see cref="SyncIntegrationTests"/> without other tests in parallel.</summary>
[CollectionDefinition(nameof(SyncIntegrationTests), DisableParallelization = true)]
public class SyncIntegrationTestsCollection;
