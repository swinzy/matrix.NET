using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The content of an <c>m.room.message</c> event. Each <c>msgtype</c> the library models has its own
/// class, e.g. <see cref="TextMessageContent"/>; <see cref="UnknownMessageContent"/> and
/// <see cref="InvalidMessageContent"/> keep everything else without losing data.
/// </summary>
/// <remarks>
/// The set of subclasses is fixed by the library. To send a <c>msgtype</c> it does not model, use
/// <see cref="MatrixClient.SendEventAsync(string, string, JsonObject, string?, CancellationToken)"/>.
/// </remarks>
[JsonConverter(typeof(MessageContentConverter))]
public abstract class MessageContent
{
    private protected MessageContent()
    {
    }

    /// <summary>The <c>msgtype</c>, e.g. <c>m.text</c>.</summary>
    [JsonIgnore]
    public abstract string MsgType { get; }

    /// <summary>
    /// The plain-text body. Clients that cannot display a message's <see cref="MsgType"/> show this
    /// instead.
    /// </summary>
    // Written straight after msgtype, before the subclass's own fields
    [JsonPropertyOrder(-1)]
    public required string Body { get; init; }

    /// <summary>
    /// Fields of the content this library does not model, e.g. <c>m.relates_to</c> or another
    /// client's extensions, kept so that nothing is lost when content is read and sent again.
    /// Always <see langword="null"/> for <see cref="UnknownMessageContent"/> and
    /// <see cref="InvalidMessageContent"/>, which keep the whole content instead.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>Returns why the content breaks the spec, or <see langword="null"/> if it does not.</summary>
    internal virtual string? Validate() =>
        Body is null ? "The content has no body." : null;
}

/// <summary>A text message (<c>m.text</c>).</summary>
public sealed class TextMessageContent : MessageContent
{
    /// <summary>The format of <see cref="FormattedBody"/>.</summary>
    public const string HtmlFormat = "org.matrix.custom.html";

    /// <summary>Creates an empty text message; set <see cref="MessageContent.Body"/>.</summary>
    public TextMessageContent()
    {
    }

    /// <summary>Creates a plain text message.</summary>
    /// <param name="body">The text.</param>
    [SetsRequiredMembers]
    public TextMessageContent(string body)
    {
        Body = body;
    }

    /// <inheritdoc/>
    // [JsonIgnore] is not inherited by overrides; the converter writes msgtype itself
    [JsonIgnore]
    public override string MsgType => "m.text";

    /// <summary>
    /// The format of <see cref="FormattedBody"/>; only <see cref="HtmlFormat"/> is defined. Set
    /// both or neither.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>The formatted version of <see cref="MessageContent.Body"/>, e.g. HTML. Set both or neither.</summary>
    public string? FormattedBody { get; init; }

    internal override string? Validate() =>
        base.Validate() ?? ((Format is null) != (FormattedBody is null)
            ? "format and formatted_body must be given together."
            : null);
}

/// <summary>
/// A message whose <c>msgtype</c> this library does not model. Show <see cref="MessageContent.Body"/>,
/// as the spec asks of clients that cannot display a message type.
/// </summary>
public sealed class UnknownMessageContent : MessageContent
{
    [SetsRequiredMembers]
    internal UnknownMessageContent(string msgType, string? body, JsonObject content)
    {
        MsgType = msgType;
        Body = body ?? "";
        Content = content;
    }

    /// <inheritdoc/>
    public override string MsgType { get; }

    /// <summary>The whole content as received, written back unchanged when sent.</summary>
    public JsonObject Content { get; }

    internal override string? Validate() => null;
}

/// <summary>
/// A message that breaks the spec, e.g. an <c>m.text</c> without a body, or one without a
/// <c>msgtype</c>. Kept rather than rejected, so one malformed event cannot break reading the rest.
/// </summary>
public sealed class InvalidMessageContent : MessageContent
{
    [SetsRequiredMembers]
    internal InvalidMessageContent(string msgType, string? body, JsonObject content, string error)
    {
        MsgType = msgType;
        Body = body ?? "";
        Content = content;
        Error = error;
    }

    /// <summary>The <c>msgtype</c> the content claims, or an empty string if it has none.</summary>
    public override string MsgType { get; }

    /// <summary>The whole content as received.</summary>
    public JsonObject Content { get; }

    /// <summary>Why the content is invalid.</summary>
    public string Error { get; }

    internal override string? Validate() => Error;
}
