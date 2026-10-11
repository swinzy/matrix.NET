using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Room set-up for integration tests; redaction goes around the library, which cannot redact yet.</summary>
internal static class TestRooms
{
    public const string LocalOnly = "Creates rooms, which cannot be deleted; local Synapse only";

    /// <summary>Creates a private room and returns its ID.</summary>
    public static Task<string> CreateAsync(MatrixClient client, CancellationToken cancellationToken) =>
        client.CreateRoomAsync(new CreateRoomRequest { Name = "matrix.NET test room" }, cancellationToken);

    /// <summary>Creates a public room with a new alias, which anyone can join, and returns both.</summary>
    public static async Task<(string RoomId, string Alias)> CreatePublicAsync(MatrixClient client,
        CancellationToken cancellationToken)
    {
        var aliasName = NewAliasName();
        var roomId = await client.CreateRoomAsync(new CreateRoomRequest
        {
            Name = "matrix.NET public test room", Preset = RoomPresets.PublicChat, RoomAliasName = aliasName
        }, cancellationToken);
        return (roomId, $"#{aliasName}:{ServerName(client.Session)}");
    }

    /// <summary>An alias local part no earlier run has used.</summary>
    public static string NewAliasName() => $"matrixdotnet-{Guid.NewGuid():N}";

    /// <summary>Redacts an event through <c>PUT /rooms/{roomId}/redact/{eventId}/{txnId}</c>.</summary>
    public static async Task RedactAsync(MatrixSession session, string roomId, string eventId, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        var path = $"_matrix/client/v3/rooms/{Uri.EscapeDataString(roomId)}/redact/{Uri.EscapeDataString(eventId)}/{TransactionId.New()}";
        using var message = new HttpRequestMessage(HttpMethod.Put, new Uri(session.Homeserver, path))
        {
            Content = JsonContent.Create(new { reason = "matrix.NET redaction test" })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The server name part of the session's user ID, e.g. <c>localhost</c>.</summary>
    public static string ServerName(MatrixSession session) => session.UserId[(session.UserId.IndexOf(':') + 1)..];
}
