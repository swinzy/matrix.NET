using System.Text.Json;
using System.Text.Json.Serialization;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Discovery information, as served at <c>/.well-known/matrix/client</c> and returned by login in
/// <c>well_known</c> (D36). Read as received: nothing in it has been validated.
/// </summary>
public sealed class DiscoveryInformation
{
    /// <summary>The homeserver (<c>m.homeserver</c>); the spec requires it, but it may be missing.</summary>
    [JsonPropertyName("m.homeserver")]
    public ServerInformation? Homeserver { get; init; }

    /// <summary>The identity server (<c>m.identity_server</c>), if any.</summary>
    [JsonPropertyName("m.identity_server")]
    public ServerInformation? IdentityServer { get; init; }

    /// <summary>Other keys, which the spec reserves for applications, e.g. <c>com.example.app.setting</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    // An optional part of a successful response, so a malformed one is dropped rather than failing the whole
    internal static DiscoveryInformation? TryRead(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } json)
            return null;

        try
        {
            return json.Deserialize(MatrixTransport.TypeInfo<DiscoveryInformation>());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>One server in <see cref="DiscoveryInformation"/>.</summary>
public sealed class ServerInformation
{
    /// <summary>The server's base URL as given, e.g. <c>https://matrix.example.org</c>; not validated.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Other keys in the server's object.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
