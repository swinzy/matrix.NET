namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>Room set-up for integration tests.</summary>
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

    /// <summary>The server name part of the session's user ID, e.g. <c>localhost</c>.</summary>
    public static string ServerName(MatrixSession session) => session.UserId[(session.UserId.IndexOf(':') + 1)..];
}
