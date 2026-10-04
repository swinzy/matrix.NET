using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The result of <see cref="MatrixClient.SyncAsync"/>. So far only room timelines are modelled; every
/// other part is kept in <c>AdditionalProperties</c> (D32).
/// </summary>
public sealed class SyncResponse
{
    /// <summary>Pass as <see cref="SyncRequest.Since"/> to continue from here.</summary>
    public required string NextBatch { get; init; }

    /// <summary>Changes to rooms, or <see langword="null"/> if there are none.</summary>
    public SyncRooms? Rooms { get; init; }

    /// <summary>Parts this library does not model yet, e.g. <c>account_data</c>, <c>presence</c> or <c>to_device</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Changes to rooms in a sync, by membership.</summary>
public sealed class SyncRooms
{
    /// <summary>Rooms the user has joined, by room ID.</summary>
    public Dictionary<string, JoinedRoom>? Join { get; init; }

    /// <summary>Rooms the user has left or been removed from, by room ID.</summary>
    public Dictionary<string, LeftRoom>? Leave { get; init; }

    /// <summary>Parts this library does not model yet: <c>invite</c> and <c>knock</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>A joined room in a sync.</summary>
public sealed class JoinedRoom
{
    /// <summary>New events in the room.</summary>
    public Timeline? Timeline { get; init; }

    /// <summary>Parts this library does not model yet, e.g. <c>state</c>, <c>ephemeral</c> or <c>summary</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>A room the user has left, in a sync.</summary>
public sealed class LeftRoom
{
    /// <summary>The events up to the point the user left.</summary>
    public Timeline? Timeline { get; init; }

    /// <summary>Parts this library does not model yet, e.g. <c>state</c> or <c>account_data</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Events of a room in a sync, oldest first.</summary>
public sealed class Timeline
{
    /// <summary>The events.</summary>
    public required List<RoomEvent> Events { get; init; }

    /// <summary>
    /// Whether events were left out between the previous sync and these, e.g. because there were too
    /// many. Fetch them with <see cref="PrevBatch"/> if you need them.
    /// </summary>
    public bool Limited { get; init; }

    /// <summary>A token to fetch events before these, with <c>GET /rooms/{roomId}/messages</c>.</summary>
    public string? PrevBatch { get; init; }

    /// <summary>Fields this library does not model.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
