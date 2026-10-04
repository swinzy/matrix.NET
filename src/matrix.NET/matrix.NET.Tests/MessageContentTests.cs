using System.Text.Json;
using System.Text.Json.Nodes;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

public class MessageContentTests
{
    private static MessageContent Read(string json) =>
        JsonSerializer.Deserialize(json, MatrixJsonContext.Default.MessageContent)!;

    private static string Write(MessageContent content) =>
        JsonSerializer.Serialize(content, MatrixJsonContext.Default.MessageContent);

    [Fact]
    public void Read_TextWithMsgTypeAnywhere()
    {
        var content = Assert.IsType<TextMessageContent>(Read("""{"body":"hi","msgtype":"m.text"}"""));

        Assert.Equal("m.text", content.MsgType);
        Assert.Equal("hi", content.Body);
        Assert.Null(content.Format);
        Assert.Null(content.AdditionalProperties);
    }

    [Fact]
    public void Read_FormattedText()
    {
        var content = Assert.IsType<TextMessageContent>(Read(
            """{"msgtype":"m.text","body":"*hi*","format":"org.matrix.custom.html","formatted_body":"<em>hi</em>"}"""));

        Assert.Equal(TextMessageContent.HtmlFormat, content.Format);
        Assert.Equal("<em>hi</em>", content.FormattedBody);
    }

    [Fact]
    public void Text_KeepsFieldsItDoesNotModelThroughARoundTrip()
    {
        const string json = """{"msgtype":"m.text","body":"hi","m.mentions":{"user_ids":["@a:b"]},"com.example.x":1}""";

        var content = Read(json);

        Assert.Equal(["m.mentions", "com.example.x"], content.AdditionalProperties!.Keys);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(Write(content))));
    }

    [Fact]
    public void Write_PutsMsgTypeFirstAndOmitsNulls()
    {
        Assert.Equal("""{"msgtype":"m.text","body":"hi"}""", Write(new TextMessageContent("hi")));
    }

    [Fact]
    public void Read_UnknownMsgTypeKeepsEverything()
    {
        const string json = """{"body":"Vote!","msgtype":"org.example.poll","answers":[1,2]}""";

        var content = Assert.IsType<UnknownMessageContent>(Read(json));

        Assert.Equal("org.example.poll", content.MsgType);
        Assert.Equal("Vote!", content.Body);
        Assert.Equal(json, Write(content));
    }

    [Theory]
    [InlineData("""{"body":"hi"}""", "", "msgtype")]
    [InlineData("""{"msgtype":5,"body":"hi"}""", "", "msgtype")]
    [InlineData("""{"msgtype":"m.text"}""", "m.text", "body")]
    [InlineData("""{"msgtype":"m.text","body":null}""", "m.text", "body")]
    [InlineData("""{"msgtype":"m.text","body":5}""", "m.text", "")]
    [InlineData("""{"msgtype":"m.text","body":"hi","format":"org.matrix.custom.html"}""", "m.text", "formatted_body")]
    public void Read_InvalidContentIsKeptWithTheReason(string json, string msgType, string errorMentions)
    {
        var content = Assert.IsType<InvalidMessageContent>(Read(json));

        Assert.Equal(msgType, content.MsgType);
        Assert.Contains(errorMentions, content.Error);
        Assert.Equal(json, Write(content));
    }

    [Fact]
    public void Read_InvalidContentStillOffersTheBody()
    {
        var content = Read("""{"body":"hi"}""");

        Assert.Equal("hi", content.Body);
    }

    [Fact]
    public void Read_NonObjectFails()
    {
        Assert.Throws<JsonException>(() => Read("\"hi\""));
    }
}
