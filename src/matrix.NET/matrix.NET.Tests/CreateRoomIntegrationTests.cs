using System.Text.Json.Nodes;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Creates rooms on the homeserver. Local only, as rooms cannot be deleted.</summary>
public class CreateRoomIntegrationTests(LoggedInClientFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateRoomAsync_AppliesEveryParameterToTheNewRoom()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var aliasName = TestRooms.NewAliasName();

        var roomId = await client.CreateRoomAsync(new CreateRoomRequest
        {
            Preset = RoomPresets.PrivateChat,
            Name = "matrix.NET create test",
            Topic = "Created by CreateRoomAsync",
            RoomAliasName = aliasName,
            InitialState =
            [
                new InitialStateEvent
                {
                    Type = "m.room.history_visibility", Content = new JsonObject { ["history_visibility"] = "joined" }
                }
            ],
            PowerLevelContentOverride = new JsonObject { ["events_default"] = 50 }
        }, Ct);

        // A room's creation is short, so the whole of it fits in the initial timeline
        var filter = """{"room":{"rooms":[""" + $"\"{roomId}\"" + """],"timeline":{"limit":50}}}""";
        var sync = await client.SyncAsync(new SyncRequest { Filter = filter }, Ct);
        var state = (sync.Rooms?.Join?[roomId].Timeline?.Events ?? []).OfType<UnknownRoomEvent>()
            .Where(e => e.IsState).ToDictionary(e => e.Type);

        Assert.Equal("matrix.NET create test", (string?)state["m.room.name"].Content["name"]);
        Assert.Equal("Created by CreateRoomAsync", (string?)state["m.room.topic"].Content["topic"]);
        Assert.Equal($"#{aliasName}:{TestRooms.ServerName(client.Session)}",
            (string?)state["m.room.canonical_alias"].Content["alias"]);
        // initial_state comes after the preset's history visibility, so it wins
        Assert.Equal("joined", (string?)state["m.room.history_visibility"].Content["history_visibility"]);
        Assert.Equal("invite", (string?)state["m.room.join_rules"].Content["join_rule"]);
        Assert.Equal(50, (int?)state["m.room.power_levels"].Content["events_default"]);

        var taken = await Assert.ThrowsAsync<MatrixException>(() =>
            client.CreateRoomAsync(new CreateRoomRequest { RoomAliasName = aliasName }, Ct));
        Assert.Equal(MatrixErrorCodes.RoomInUse, taken.ErrorCode);

        await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
    }
}
