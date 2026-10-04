using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// An event in a room, as it appears in a timeline. Each event type the library models has its own
/// class, e.g. <see cref="MessageEvent"/>; <see cref="UnknownRoomEvent"/>, <see cref="RedactedRoomEvent"/>
/// and <see cref="InvalidRoomEvent"/> keep everything else without losing data (D32).
/// </summary>
[JsonConverter(typeof(RoomEventConverter))]
public abstract class RoomEvent
{
    private protected RoomEvent()
    {
    }

    /// <summary>The event ID, e.g. <c>$abc</c>; empty for an <see cref="InvalidRoomEvent"/> without one.</summary>
    public string EventId { get; private set; } = "";

    /// <summary>The event type, e.g. <c>m.room.message</c>; empty for an <see cref="InvalidRoomEvent"/> without one.</summary>
    public string Type { get; private set; } = "";

    /// <summary>The user who sent the event, e.g. <c>@alice:example.org</c>.</summary>
    public string Sender { get; private set; } = "";

    /// <summary>When the sender's homeserver received the event.</summary>
    public DateTimeOffset OriginServerTs { get; private set; }

    /// <summary>For state events, which piece of state the event sets; <see langword="null"/> for other events.</summary>
    public string? StateKey { get; private set; }

    /// <summary>Whether this is a state event, such as a membership change or a new room name.</summary>
    public bool IsState => StateKey is not null;

    /// <summary>Data the homeserver adds that is not part of the signed event.</summary>
    public EventUnsigned? Unsigned { get; private set; }

    /// <summary>Top-level fields of the event this library does not model, kept for a round trip.</summary>
    public Dictionary<string, JsonElement>? AdditionalProperties { get; private set; }

    // Events are only created by RoomEventConverter, which sets the common fields once
    internal RoomEvent Initialize(string eventId, string type, string sender, DateTimeOffset originServerTs,
        string? stateKey, EventUnsigned? unsigned, Dictionary<string, JsonElement>? additionalProperties)
    {
        EventId = eventId;
        Type = type;
        Sender = sender;
        OriginServerTs = originServerTs;
        StateKey = stateKey;
        Unsigned = unsigned;
        AdditionalProperties = additionalProperties;
        return this;
    }
}

/// <summary>Data the homeserver adds to an event that is not part of the signed event.</summary>
public sealed class EventUnsigned
{
    /// <summary>Milliseconds since the event was sent, as of when the homeserver returned it.</summary>
    public long? Age { get; set; }

    /// <summary>
    /// The transaction ID the event was sent with, given only to the device that sent it. Matches
    /// <see cref="SendEventResult.TransactionId"/> for your own events (D28).
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>The redaction event that removed this event's content, if it has been redacted.</summary>
    public JsonObject? RedactedBecause { get; set; }

    /// <summary>Fields this library does not model, e.g. <c>prev_content</c> or <c>membership</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>A message (<c>m.room.message</c>).</summary>
public sealed class MessageEvent : RoomEvent
{
    internal MessageEvent(MessageContent content) => Content = content;

    /// <summary>The message.</summary>
    public MessageContent Content { get; }
}

/// <summary>
/// An encrypted event (<c>m.room.encrypted</c>). This library cannot decrypt events yet, so encrypted
/// rooms show only these; show them as encrypted messages that cannot be read.
/// </summary>
public sealed class EncryptedEvent : RoomEvent
{
    internal EncryptedEvent(JsonObject content) => Content = content;

    /// <summary>The encryption algorithm, e.g. <c>m.megolm.v1.aes-sha2</c>, if the content names one.</summary>
    public string? Algorithm => Content["algorithm"] is JsonValue value && value.TryGetValue(out string? algorithm)
        ? algorithm
        : null;

    /// <summary>The encrypted content as received.</summary>
    public JsonObject Content { get; }
}

/// <summary>
/// A redaction (<c>m.room.redaction</c>): the content of <see cref="Redacts"/> was removed. Hide that
/// event, or show it as deleted. Events redacted before you received them arrive as
/// <see cref="RedactedRoomEvent"/> instead.
/// </summary>
public sealed class RedactionEvent : RoomEvent
{
    internal RedactionEvent(string redacts, JsonObject content)
    {
        Redacts = redacts;
        Content = content;
    }

    /// <summary>The ID of the redacted event.</summary>
    public string Redacts { get; }

    /// <summary>Why it was redacted, if a reason was given.</summary>
    public string? Reason => Content["reason"] is JsonValue value && value.TryGetValue(out string? reason) ? reason : null;

    /// <summary>The content as received.</summary>
    public JsonObject Content { get; }
}

/// <summary>
/// An event whose content was removed by a redaction before you received it, of any
/// <see cref="RoomEvent.Type"/>. <see cref="EventUnsigned.RedactedBecause"/> holds the redaction.
/// </summary>
public sealed class RedactedRoomEvent : RoomEvent
{
    internal RedactedRoomEvent(JsonObject content) => Content = content;

    /// <summary>What is left of the content: the keys the protocol keeps for this type, often none.</summary>
    public JsonObject Content { get; }
}

/// <summary>An event of a type this library does not model, including state events such as <c>m.room.name</c>.</summary>
public sealed class UnknownRoomEvent : RoomEvent
{
    internal UnknownRoomEvent(JsonObject content) => Content = content;

    /// <summary>The content as received.</summary>
    public JsonObject Content { get; }
}

/// <summary>
/// An event that breaks the spec, e.g. without an event ID or content, or a redaction without
/// <c>redacts</c>. Kept rather than rejected, so one malformed event cannot break reading the rest.
/// Fields that could not be read are empty.
/// </summary>
public sealed class InvalidRoomEvent : RoomEvent
{
    internal InvalidRoomEvent(JsonNode raw, string error, string type)
    {
        Raw = raw;
        Error = error;
        Initialize("", type, "", DateTimeOffset.UnixEpoch, null, null, null);
    }

    /// <summary>The whole event as received.</summary>
    public JsonNode Raw { get; }

    /// <summary>Why the event is invalid.</summary>
    public string Error { get; }
}
