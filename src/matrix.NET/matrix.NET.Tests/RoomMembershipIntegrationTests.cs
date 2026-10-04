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

    [Fact]
    public async Task LeaveRoomAsync_RemovesTheRoomFromJoinedRooms()
    {
        Assert.SkipUnless(fixture.Settings.IsLocal, TestRooms.LocalOnly);
        var client = await fixture.GetClientAsync();
        var roomId = await TestRooms.CreateAsync(client.Session, Ct);
        Assert.Contains(roomId, await client.GetJoinedRoomsAsync(Ct));

        await client.LeaveRoomAsync(roomId, "matrix.NET leave test", Ct);

        Assert.DoesNotContain(roomId, await client.GetJoinedRoomsAsync(Ct));
    }
}
