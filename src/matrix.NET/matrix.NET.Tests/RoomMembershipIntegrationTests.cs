using System.Net.Http.Headers;
using System.Net.Http.Json;

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
        Assert.SkipUnless(fixture.Settings.IsLocal, "Creates rooms, which cannot be deleted; local Synapse only");
        var client = await fixture.GetClientAsync();
        var roomId = await CreateRoomAsync(client.Session);
        Assert.Contains(roomId, await client.GetJoinedRoomsAsync(Ct));

        await client.LeaveRoomAsync(roomId, "matrix.NET leave test", Ct);

        Assert.DoesNotContain(roomId, await client.GetJoinedRoomsAsync(Ct));
    }

    // The library cannot create rooms yet, so this calls POST /createRoom directly
    private static async Task<string> CreateRoomAsync(MatrixSession session)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(session.Homeserver, "_matrix/client/v3/createRoom"))
        {
            Content = JsonContent.Create(new { name = "matrix.NET test room" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await http.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct);
        return body!["room_id"];
    }
}
