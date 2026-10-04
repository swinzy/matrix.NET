using System.Text.Json;
using System.Text.Json.Serialization;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Everything needed to use or restore a logged-in session. Save it with <see cref="ToJson"/> and
/// restore it with <see cref="FromJson"/>; store it securely, as it contains access credentials.
/// </summary>
/// <remarks>
/// The serialised form is a compatibility contract: property names are fixed regardless of the
/// serialiser's naming policy, fields are only ever added, and <see cref="FormatVersion"/> is
/// raised only for incompatible changes, which this library keeps reading.
/// </remarks>
public sealed record MatrixSession
{
    /// <summary>The format version written by this version of the library.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Version of the serialised format.</summary>
    [JsonPropertyName("format_version")]
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Base URL of the homeserver.</summary>
    [JsonPropertyName("homeserver")]
    public required Uri Homeserver { get; init; }

    /// <summary>Fully qualified user ID, e.g. <c>@alice:example.org</c>.</summary>
    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }

    /// <summary>ID of the device this session belongs to.</summary>
    [JsonPropertyName("device_id")]
    public required string DeviceId { get; init; }

    /// <summary>Access token for authenticated requests.</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>Refresh token, if the homeserver issued one.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>When the access token expires, or <see langword="null"/> if it does not.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Serialises the session in its stable format.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, MatrixJsonContext.Instance.MatrixSession);

    /// <summary>Restores a session serialised by <see cref="ToJson"/>, in any supported format version.</summary>
    /// <exception cref="JsonException">
    /// The JSON is invalid, lacks a required field, or was written by a newer version of this library.
    /// </exception>
    public static MatrixSession FromJson(string json)
    {
        // Check the version before binding, so a newer format fails clearly rather than half-parsed
        using var document = JsonDocument.Parse(json);
        var hasVersion = document.RootElement.TryGetProperty("format_version", out var version);
        if (hasVersion && version.TryGetInt32(out var formatVersion) && formatVersion > CurrentFormatVersion)
        {
            throw new JsonException(
                $"The session was saved in format version {formatVersion}, but this version of the library " +
                $"reads up to version {CurrentFormatVersion}. Upgrade the library to restore it.");
        }

        var session = document.Deserialize(MatrixJsonContext.Instance.MatrixSession)
                      ?? throw new JsonException("The session JSON is null.");
        // Source generation sets a missing init-only property to its default rather than keeping its
        // initialiser, and sessions saved before the field existed are version 1
        return hasVersion ? session : session with { FormatVersion = 1 };
    }

    /// <summary>Describes the session with its tokens masked, so it is safe to log.</summary>
    public override string ToString() =>
        $"MatrixSession {{ Homeserver = {Homeserver}, UserId = {UserId}, DeviceId = {DeviceId}, " +
        $"AccessToken = ***, RefreshToken = {(RefreshToken is null ? "null" : "***")}, ExpiresAt = {ExpiresAt} }}";
}