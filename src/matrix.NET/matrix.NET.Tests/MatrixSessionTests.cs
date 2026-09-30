using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixSessionTests
{
    private static MatrixSession FullSession() => new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@cheeky_monkey:matrix.org",
        DeviceId = "GHTYAJCE",
        AccessToken = "secret-access",
        RefreshToken = "secret-refresh",
        ExpiresAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void ToString_MasksTokens()
    {
        var text = FullSession().ToString();

        Assert.DoesNotContain("secret-access", text);
        Assert.DoesNotContain("secret-refresh", text);
        Assert.Contains("@cheeky_monkey:matrix.org", text);
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        var session = FullSession();

        Assert.Equal(session, MatrixSession.FromJson(session.ToJson()));
    }

    [Fact]
    public void ToJson_RoundTripsWithoutOptionalFields()
    {
        var session = FullSession() with { RefreshToken = null, ExpiresAt = null };

        Assert.Equal(session, MatrixSession.FromJson(session.ToJson()));
    }

    [Fact]
    public void ToJson_WritesTheStableFormat()
    {
        var json = FullSession().ToJson();

        Assert.Equal(
            """{"format_version":1,"homeserver":"https://matrix.example.org/","user_id":"@cheeky_monkey:matrix.org","device_id":"GHTYAJCE","access_token":"secret-access","refresh_token":"secret-refresh","expires_at":"2026-09-30T12:00:00+00:00"}""",
            json);
    }

    // Apps may serialise the record with their own options; the field names must not change
    [Fact]
    public void Serialise_KeepsFieldNamesUnderAnyNamingPolicy()
    {
        var camelCase = JsonSerializer.Serialize(FullSession(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"user_id\"", camelCase);
        Assert.Contains("\"format_version\"", camelCase);
        Assert.Equal(FullSession(), MatrixSession.FromJson(camelCase));
    }

    [Fact]
    public void FromJson_ReadsSessionWithoutFormatVersionAsVersionOne()
    {
        var session = MatrixSession.FromJson(
            """{"homeserver":"https://matrix.example.org/","user_id":"@a:example.org","device_id":"D","access_token":"t"}""");

        Assert.Equal(1, session.FormatVersion);
    }

    [Fact]
    public void FromJson_RejectsNewerFormatVersion()
    {
        var json = FullSession().ToJson().Replace("\"format_version\":1", "\"format_version\":2");

        var exception = Assert.Throws<JsonException>(() => MatrixSession.FromJson(json));

        Assert.Contains("format version 2", exception.Message);
    }

    [Fact]
    public void FromJson_RejectsMissingRequiredField()
    {
        Assert.Throws<JsonException>(() => MatrixSession.FromJson(
            """{"format_version":1,"homeserver":"https://matrix.example.org/","user_id":"@a:example.org","device_id":"D"}"""));
    }

    [Fact]
    public void FromJson_IgnoresFieldsAddedByNewerVersions()
    {
        var json = FullSession().ToJson().Replace("}", ",\"some_future_field\":true}");

        Assert.Equal(FullSession(), MatrixSession.FromJson(json));
    }
}