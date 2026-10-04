using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Reads and writes <see cref="MessageContent"/> by its <c>msgtype</c>. Unlike System.Text.Json's
/// built-in polymorphism, it accepts <c>msgtype</c> anywhere in the object, and keeps unknown and
/// invalid content instead of failing or dropping the <c>msgtype</c>.
/// </summary>
internal sealed class MessageContentConverter : JsonConverter<MessageContent>
{
    // The one place to add a modelled msgtype. The subclasses' own metadata does not use this
    // converter, so delegating to it cannot recurse
    private static readonly Dictionary<string, JsonTypeInfo> KnownTypes = new()
    {
        ["m.text"] = MatrixJsonContext.Instance.TextMessageContent
    };

    public override MessageContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject content)
            throw new JsonException("Message content must be a JSON object.");

        var body = content["body"] is JsonValue bodyValue && bodyValue.TryGetValue(out string? text) ? text : null;
        if (content["msgtype"] is not JsonValue msgTypeValue || !msgTypeValue.TryGetValue(out string? msgType))
            return new InvalidMessageContent("", body, content, "The content has no msgtype.");
        if (!KnownTypes.TryGetValue(msgType, out var typeInfo))
            return new UnknownMessageContent(msgType, body, content);

        // msgtype is implied by the subclass; left in, it would end up in AdditionalProperties
        var fields = content.DeepClone().AsObject();
        fields.Remove("msgtype");
        try
        {
            var known = (MessageContent?)fields.Deserialize(typeInfo);
            var error = known is null ? "The content is null." : known.Validate();
            return error is null ? known! : new InvalidMessageContent(msgType, body, content, error);
        }
        catch (JsonException e)
        {
            return new InvalidMessageContent(msgType, body, content, e.Message);
        }
    }

    public override void Write(Utf8JsonWriter writer, MessageContent value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case UnknownMessageContent unknown:
                unknown.Content.WriteTo(writer);
                return;
            case InvalidMessageContent invalid:
                invalid.Content.WriteTo(writer);
                return;
        }

        // The hierarchy is closed, so every other subclass is a known type
        var fields = JsonSerializer.SerializeToNode(value, KnownTypes[value.MsgType])!.AsObject();
        writer.WriteStartObject();
        writer.WriteString("msgtype", value.MsgType);
        foreach (var (name, field) in fields)
        {
            if (name == "msgtype")
                continue;
            writer.WritePropertyName(name);
            if (field is null)
                writer.WriteNullValue();
            else
                field.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
