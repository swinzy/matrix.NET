using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Room set-up for integration tests, for what the library cannot do yet.</summary>
internal static class TestRooms
{
    public const string LocalOnly = "Creates rooms, which cannot be deleted; local Synapse only";

    /// <summary>Creates a private room through <c>POST /createRoom</c> and returns its ID.</summary>
    public static async Task<string> CreateAsync(MatrixSession session, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(session.Homeserver, "_matrix/client/v3/createRoom"))
        {
            Content = JsonContent.Create(new { name = "matrix.NET test room" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
        return body!["room_id"];
    }
}
