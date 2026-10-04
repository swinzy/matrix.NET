using System.Text.Json;
using System.Text.Json.Nodes;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

public class RoomEventTests
{
    private static RoomEvent Read(string json) => JsonSerializer.Deserialize(json, MatrixJsonContext.Instance.RoomEvent)!;

    private static string Write(RoomEvent roomEvent) =>
        JsonSerializer.Serialize(roomEvent, MatrixJsonContext.Instance.RoomEvent);

    private static void AssertRoundTrip(string json, RoomEvent roomEvent) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(Write(roomEvent))), Write(roomEvent));

    private const string Message = """
        {"content":{"body":"hi","msgtype":"m.text"},"event_id":"$m","origin_server_ts":1432735824653,
         "sender":"@a:example.org","type":"m.room.message","unsigned":{"age":1234,"transaction_id":"t1","membership":"join"}}
        """;

    [Fact]
    public void Message_IsReadWithTypedContentAndCommonFields()
    {
        var message = Assert.IsType<MessageEvent>(Read(Message));

        Assert.Equal("$m", message.EventId);
        Assert.Equal("m.room.message", message.Type);
        Assert.Equal("@a:example.org", message.Sender);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1432735824653), message.OriginServerTs);
        Assert.False(message.IsState);
        Assert.Equal("hi", Assert.IsType<TextMessageContent>(message.Content).Body);
        Assert.Equal("t1", message.Unsigned!.TransactionId);
        Assert.Equal(1234, message.Unsigned.Age);
        Assert.Equal(["membership"], message.Unsigned.AdditionalProperties!.Keys);
        AssertRoundTrip(Message, message);
    }

    [Fact]
    public void Message_WithUnknownMsgTypeIsStillAMessage()
    {
        var message = Assert.IsType<MessageEvent>(Read(
            """{"content":{"body":"Vote","msgtype":"org.example.poll"},"event_id":"$m","origin_server_ts":1,"sender":"@a:b","type":"m.room.message"}"""));

        Assert.IsType<UnknownMessageContent>(message.Content);
    }

    [Fact]
    public void Encrypted_IsRecognisedButNotReadable()
    {
        const string json = """
            {"content":{"algorithm":"m.megolm.v1.aes-sha2","ciphertext":"AwgAEnAC","session_id":"s"},
             "event_id":"$e","origin_server_ts":1,"sender":"@a:b","type":"m.room.encrypted"}
            """;

        var encrypted = Assert.IsType<EncryptedEvent>(Read(json));

        Assert.Equal("m.megolm.v1.aes-sha2", encrypted.Algorithm);
        AssertRoundTrip(json, encrypted);
    }

    // Room versions 11 and later put redacts in the content, earlier ones at the top level
    [Theory]
    [InlineData("""{"content":{"redacts":"$old","reason":"Spam"},"event_id":"$r","origin_server_ts":1,"sender":"@a:b","type":"m.room.redaction"}""")]
    [InlineData("""{"content":{"reason":"Spam"},"redacts":"$old","event_id":"$r","origin_server_ts":1,"sender":"@a:b","type":"m.room.redaction"}""")]
    public void Redaction_ReadsRedactsFromEitherPlace(string json)
    {
        var redaction = Assert.IsType<RedactionEvent>(Read(json));

        Assert.Equal("$old", redaction.Redacts);
        Assert.Equal("Spam", redaction.Reason);
        AssertRoundTrip(json, redaction);
    }

    [Fact]
    public void Redaction_WithoutRedactsIsInvalid()
    {
        var invalid = Assert.IsType<InvalidRoomEvent>(Read(
            """{"content":{},"event_id":"$r","origin_server_ts":1,"sender":"@a:b","type":"m.room.redaction"}"""));

        Assert.Equal("m.room.redaction", invalid.Type);
        Assert.Contains("redacts", invalid.Error);
    }

    [Fact]
    public void RedactedEvent_KeepsItsTypeWhateverItWas()
    {
        const string json = """
            {"content":{},"event_id":"$m","origin_server_ts":1,"sender":"@a:b","type":"m.room.message",
             "unsigned":{"redacted_because":{"type":"m.room.redaction","event_id":"$r","content":{"redacts":"$m"}}}}
            """;

        var redacted = Assert.IsType<RedactedRoomEvent>(Read(json));

        Assert.Equal("m.room.message", redacted.Type);
        Assert.Equal("$r", redacted.Unsigned!.RedactedBecause!["event_id"]!.GetValue<string>());
        Assert.Empty(redacted.Content);
        AssertRoundTrip(json, redacted);
    }

    [Fact]
    public void StateEventOfUnmodelledType_IsUnknownWithItsStateKey()
    {
        const string json = """
            {"content":{"name":"Lounge"},"event_id":"$s","origin_server_ts":1,"sender":"@a:b","state_key":"","type":"m.room.name"}
            """;

        var unknown = Assert.IsType<UnknownRoomEvent>(Read(json));

        Assert.True(unknown.IsState);
        Assert.Equal("", unknown.StateKey);
        Assert.Equal("Lounge", unknown.Content["name"]!.GetValue<string>());
        AssertRoundTrip(json, unknown);
    }

    [Fact]
    public void UnmodelledTopLevelFields_AreKept()
    {
        const string json = """
            {"content":{},"event_id":"$s","origin_server_ts":1,"sender":"@a:b","type":"com.example.x","room_id":"!r:b"}
            """;

        var unknown = Read(json);

        Assert.Equal(["room_id"], unknown.AdditionalProperties!.Keys);
        AssertRoundTrip(json, unknown);
    }

    [Theory]
    [InlineData("""{"event_id":"$x","origin_server_ts":1,"sender":"@a:b","content":{}}""", "type")]
    [InlineData("""{"type":"m.room.message","origin_server_ts":1,"sender":"@a:b","content":{}}""", "event_id")]
    [InlineData("""{"type":"m.room.message","event_id":"$x","origin_server_ts":1,"content":{}}""", "sender")]
    [InlineData("""{"type":"m.room.message","event_id":"$x","sender":"@a:b","content":{}}""", "origin_server_ts")]
    [InlineData("""{"type":"m.room.message","event_id":"$x","origin_server_ts":1,"sender":"@a:b","content":"hi"}""", "content")]
    [InlineData("""{"type":"m.room.name","event_id":"$x","origin_server_ts":1,"sender":"@a:b","content":{},"state_key":5}""", "state_key")]
    [InlineData("""{"type":"m.room.message","event_id":"$x","origin_server_ts":1,"sender":"@a:b","content":{},"unsigned":{"age":"old"}}""", "unsigned")]
    public void Invalid_IsKeptWithTheReason(string json, string errorMentions)
    {
        var invalid = Assert.IsType<InvalidRoomEvent>(Read(json));

        Assert.Contains(errorMentions, invalid.Error);
        Assert.Equal(json, Write(invalid));
    }

    [Fact]
    public void NonObject_IsInvalidRatherThanAFailure()
    {
        var invalid = Assert.IsType<InvalidRoomEvent>(Read("\"oops\""));

        Assert.Equal("\"oops\"", Write(invalid));
    }
}
