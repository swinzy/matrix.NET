using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Reads and writes <see cref="RoomEvent"/> by its <c>type</c> (D32). Like
/// <see cref="MessageContentConverter"/>, it never fails on an object: unknown, redacted and invalid
/// events are kept, so one event cannot break a whole sync response.
/// </summary>
internal sealed class RoomEventConverter : JsonConverter<RoomEvent>
{
    // Fields every room event has; anything else at the top level goes to AdditionalProperties
    private static readonly HashSet<string> CommonFields =
        ["event_id", "type", "sender", "origin_server_ts", "content", "state_key", "unsigned"];

    public override RoomEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return new InvalidRoomEvent(JsonNode.Parse(root.GetRawText())!, "The event is not a JSON object.", "");

        var type = GetString(root, "type");
        var eventId = GetString(root, "event_id");
        var sender = GetString(root, "sender");
        var hasTimestamp = root.TryGetProperty("origin_server_ts", out var timestamp) &&
                           timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out _);
        var hasContent = root.TryGetProperty("content", out var contentElement) &&
                         contentElement.ValueKind == JsonValueKind.Object;
        var stateKey = root.TryGetProperty("state_key", out var key) ? key : default;

        var error = type is null ? "The event has no type."
            : eventId is null ? "The event has no event_id."
            : sender is null ? "The event has no sender."
            : !hasTimestamp ? "The event has no origin_server_ts."
            : !hasContent ? "The event has no content object."
            : stateKey.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.String) ? "The state_key is not a string."
            : null;
        if (error is not null)
            return new InvalidRoomEvent(JsonObject.Create(root.Clone())!, error, type ?? "");

        EventUnsigned? unsigned = null;
        if (root.TryGetProperty("unsigned", out var unsignedElement))
        {
            try
            {
                unsigned = unsignedElement.Deserialize(MatrixJsonContext.Instance.EventUnsigned);
            }
            catch (JsonException e)
            {
                return new InvalidRoomEvent(JsonObject.Create(root.Clone())!, $"The unsigned data is invalid: {e.Message}", type!);
            }
        }

        var content = JsonObject.Create(contentElement.Clone())!;
        RoomEvent? roomEvent = unsigned?.RedactedBecause is not null
            ? new RedactedRoomEvent(content)
            : type switch
            {
                "m.room.message" => new MessageEvent(contentElement.Deserialize(MatrixJsonContext.Instance.MessageContent)!),
                "m.room.encrypted" => new EncryptedEvent(content),
                // Room versions 11 and later put redacts in the content, earlier ones at the top level
                "m.room.redaction" => (GetString(contentElement, "redacts") ?? GetString(root, "redacts")) is { } redacts
                    ? new RedactionEvent(redacts, content)
                    : null,
                _ => new UnknownRoomEvent(content)
            };
        if (roomEvent is null)
            return new InvalidRoomEvent(JsonObject.Create(root.Clone())!, "The redaction has no redacts.", type!);

        var extra = root.EnumerateObject()
            .Where(field => !CommonFields.Contains(field.Name))
            .ToDictionary(field => field.Name, field => field.Value.Clone());

        return roomEvent.Initialize(eventId!, type!, sender!, DateTimeOffset.FromUnixTimeMilliseconds(timestamp.GetInt64()),
            stateKey.ValueKind == JsonValueKind.String ? stateKey.GetString() : null, unsigned,
            extra.Count == 0 ? null : extra);
    }

    public override void Write(Utf8JsonWriter writer, RoomEvent value, JsonSerializerOptions options)
    {
        if (value is InvalidRoomEvent invalid)
        {
            invalid.Raw.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("event_id", value.EventId);
        writer.WriteString("type", value.Type);
        writer.WriteString("sender", value.Sender);
        writer.WriteNumber("origin_server_ts", value.OriginServerTs.ToUnixTimeMilliseconds());
        if (value.StateKey is not null)
            writer.WriteString("state_key", value.StateKey);

        writer.WritePropertyName("content");
        switch (value)
        {
            case MessageEvent message:
                JsonSerializer.Serialize(writer, message.Content, MatrixJsonContext.Instance.MessageContent);
                break;
            case EncryptedEvent encrypted:
                encrypted.Content.WriteTo(writer);
                break;
            case RedactionEvent redaction:
                redaction.Content.WriteTo(writer);
                break;
            case RedactedRoomEvent redacted:
                redacted.Content.WriteTo(writer);
                break;
            case UnknownRoomEvent unknown:
                unknown.Content.WriteTo(writer);
                break;
            default:
                throw new JsonException($"Cannot write {value.GetType().Name}.");
        }

        if (value.Unsigned is not null)
        {
            writer.WritePropertyName("unsigned");
            JsonSerializer.Serialize(writer, value.Unsigned, MatrixJsonContext.Instance.EventUnsigned);
        }

        foreach (var (name, field) in value.AdditionalProperties ?? [])
        {
            writer.WritePropertyName(name);
            field.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
