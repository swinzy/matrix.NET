using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Room set-up for integration tests, for what the library cannot do yet.</summary>
internal static class TestRooms
{
    public const string LocalOnly = "Creates rooms, which cannot be deleted; local Synapse only";

    /// <summary>Creates a private room through <c>POST /createRoom</c> and returns its ID.</summary>
    public static Task<string> CreateAsync(MatrixSession session, CancellationToken cancellationToken) =>
        CreateAsync(session, new { name = "matrix.NET test room" }, cancellationToken);

    /// <summary>Creates a public room with a new alias, which anyone can join, and returns both.</summary>
    public static async Task<(string RoomId, string Alias)> CreatePublicAsync(MatrixSession session,
        CancellationToken cancellationToken)
    {
        var aliasName = $"matrixdotnet-{Guid.NewGuid():N}";
        var roomId = await CreateAsync(session,
            new { name = "matrix.NET public test room", preset = "public_chat", room_alias_name = aliasName },
            cancellationToken);
        return (roomId, $"#{aliasName}:{ServerName(session)}");
    }

    /// <summary>The server name part of the session's user ID, e.g. <c>localhost</c>.</summary>
    public static string ServerName(MatrixSession session) => session.UserId[(session.UserId.IndexOf(':') + 1)..];

    private static async Task<string> CreateAsync(MatrixSession session, object request, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(session.Homeserver, "_matrix/client/v3/createRoom"))
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
        return body!["room_id"];
    }
}
