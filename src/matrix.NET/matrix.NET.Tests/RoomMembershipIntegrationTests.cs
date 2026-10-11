namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Joins and leaves rooms through the shared session. Local only: the rooms it creates cannot be
/// deleted, so they would pile up on your own homeserver.
/// </summary>
public class RoomMembershipIntegrationTests(LoggedInClientFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetJoinedRoomsAsync_Succeeds()
    {
        var client = await fixture.GetClientAsync();

        var rooms = await client.GetJoinedRoomsAsync(Ct);

        Assert.All(rooms, room => Assert.StartsWith("!", room));
    }

    // The other account owns the room, so it stays alive while the test user is not in it
    [Fact]
    public async Task JoinRoomAsync_JoinsByAliasThroughAServer()
    {
        var (client, other) = await ClientAndOtherAsync();
        try
        {
            var (roomId, alias) = await TestRooms.CreatePublicAsync(other, Ct);

            var joined = await client.JoinRoomAsync(alias, [TestRooms.ServerName(client.Session)], "matrix.NET join test", Ct);

            Assert.Equal(roomId, joined);
            Assert.Contains(roomId, await client.GetJoinedRoomsAsync(Ct));
            await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
        }
        finally
        {
            await other.LogoutAsync(Ct);
        }
    }

    [Fact]
    public async Task JoinRoomAsync_JoinsByRoomId()
    {
        var (client, other) = await ClientAndOtherAsync();
        try
        {
            var (roomId, _) = await TestRooms.CreatePublicAsync(other, Ct);

            Assert.Equal(roomId, await client.JoinRoomAsync(roomId, cancellationToken: Ct));
            await client.LeaveRoomAsync(roomId, cancellationToken: Ct);
        }
        finally
        {
            await other.LogoutAsync(Ct);
        }
    }

    // Logs the other account in on a device of its own; the caller logs it out
    private async Task<(MatrixClient Client, MatrixClient Other)> ClientAndOtherAsync()
    {
        var settings = fixture.Settings;
        Assert.SkipUnless(settings.IsLocal, TestRooms.LocalOnly);
        Assert.SkipWhen(settings.OtherUser is null, "No second account configured; restart tools/synapse/synapse.sh");
        var client = await fixture.GetClientAsync();
        var session = await new MatrixServer(new Uri(settings.Homeserver!)).LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = settings.OtherUser! },
            Password = settings.Password,
            RefreshToken = false
        }, Ct);
        return (client, new MatrixClient(session));
    }

    [Fact]
    public async Task LeaveRoomAsync_RemovesTheRoomFromJoinedRooms()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var roomId = await TestRooms.CreateAsync(client, Ct);
        Assert.Contains(roomId, await client.GetJoinedRoomsAsync(Ct));

        await client.LeaveRoomAsync(roomId, "matrix.NET leave test", Ct);

        Assert.DoesNotContain(roomId, await client.GetJoinedRoomsAsync(Ct));
    }
}
