using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet;

/// <summary>Parameters of <see cref="MatrixClient.CreateRoomAsync"/>; all are optional (D35).</summary>
public sealed class CreateRoomRequest
{
    /// <summary>
    /// Sets the join rules, history visibility and guest access, e.g. <see cref="RoomPresets.PrivateChat"/>.
    /// When omitted, the homeserver chooses from <see cref="Visibility"/>: <see cref="RoomPresets.PublicChat"/>
    /// for a public room, <see cref="RoomPresets.PrivateChat"/> otherwise.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>
    /// Whether the room is listed in the homeserver's published room directory, e.g.
    /// <see cref="RoomVisibilities.Public"/>; <see cref="RoomVisibilities.Private"/> when omitted.
    /// </summary>
    public string? Visibility { get; init; }

    /// <summary>The room's name (<c>m.room.name</c>).</summary>
    public string? Name { get; init; }

    /// <summary>The room's topic (<c>m.room.topic</c>), as plain text.</summary>
    public string? Topic { get; init; }

    /// <summary>
    /// The local part of an alias to create for the room on this homeserver, e.g. <c>foo</c> for
    /// <c>#foo:example.org</c>. It becomes the room's canonical alias.
    /// </summary>
    public string? RoomAliasName { get; init; }

    /// <summary>User IDs to invite, e.g. <c>@alice:example.org</c>.</summary>
    public IReadOnlyList<string>? Invite { get; init; }

    /// <summary>Marks the invitations as for a direct chat (<c>is_direct</c> on their membership events).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDirect { get; init; }

    /// <summary>
    /// The room version, e.g. <c>12</c>; the homeserver's default when omitted. A version it does not
    /// support fails with <see cref="MatrixErrorCodes.UnsupportedRoomVersion"/>.
    /// </summary>
    public string? RoomVersion { get; init; }

    /// <summary>
    /// Extra keys for the content of the <c>m.room.create</c> event, e.g. <c>m.federate</c>. The homeserver
    /// sets <c>creator</c> and <c>room_version</c> itself.
    /// </summary>
    public JsonObject? CreationContent { get; init; }

    /// <summary>
    /// State events to set when the room is created, after those of <see cref="Preset"/> and before
    /// <see cref="Name"/> and <see cref="Topic"/>, which take precedence.
    /// </summary>
    public IReadOnlyList<InitialStateEvent>? InitialState { get; init; }

    /// <summary>Applied on top of the default <c>m.room.power_levels</c> content.</summary>
    public JsonObject? PowerLevelContentOverride { get; init; }

    /// <summary>
    /// Parameters this library does not model, e.g. <c>invite_3pid</c>, sent as they are. A key that
    /// also has a property here must not be repeated.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>A state event to set when a room is created (<see cref="CreateRoomRequest.InitialState"/>).</summary>
public sealed class InitialStateEvent
{
    /// <summary>The event type, e.g. <c>m.room.join_rules</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The state key; empty for most room-wide state.</summary>
    public string StateKey { get; init; } = "";

    /// <summary>The event content.</summary>
    public required JsonObject Content { get; init; }
}

/// <summary>Values for <see cref="CreateRoomRequest.Preset"/>.</summary>
public static class RoomPresets
{
    /// <summary>Invite only, history shared with members, guests may join.</summary>
    public const string PrivateChat = "private_chat";

    /// <summary>As <see cref="PrivateChat"/>, and every invitee gets the creator's power level.</summary>
    public const string TrustedPrivateChat = "trusted_private_chat";

    /// <summary>Anyone may join, history shared with members, no guests.</summary>
    public const string PublicChat = "public_chat";
}

/// <summary>Values for <see cref="CreateRoomRequest.Visibility"/>.</summary>
public static class RoomVisibilities
{
    /// <summary>Listed in the published room directory.</summary>
    public const string Public = "public";

    /// <summary>Not listed.</summary>
    public const string Private = "private";
}
