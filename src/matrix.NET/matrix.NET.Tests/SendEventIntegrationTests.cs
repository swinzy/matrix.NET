using System.Text.Json.Nodes;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Sends events through the shared session. Local only, as it creates a room.</summary>
public class SendEventIntegrationTests(LoggedInClientFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendMessageAsync_IsIdempotentPerTransactionId()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var roomId = await TestRooms.CreateAsync(client.Session, Ct);

        var first = await client.SendMessageAsync(roomId, new TextMessageContent("matrix.NET test message"), cancellationToken: Ct);
        var repeated = await client.SendMessageAsync(roomId, new TextMessageContent("matrix.NET test message"),
            first.TransactionId, Ct);
        var another = await client.SendMessageAsync(roomId, new TextMessageContent("matrix.NET test message"), cancellationToken: Ct);

        Assert.StartsWith("$", first.EventId);
        Assert.Equal(first.EventId, repeated.EventId);
        Assert.NotEqual(first.EventId, another.EventId);
        await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
    }

    [Fact]
    public async Task SendEventAsync_SendsCustomEvents()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var roomId = await TestRooms.CreateAsync(client.Session, Ct);

        var result = await client.SendEventAsync(roomId, "com.example.matrixdotnet.test",
            new JsonObject { ["move"] = "e2e4" }, cancellationToken: Ct);

        Assert.StartsWith("$", result.EventId);
        await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
    }
}
