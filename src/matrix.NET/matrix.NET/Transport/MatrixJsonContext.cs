using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Source-generated JSON metadata for every type the library itself serialises, so no library type
/// needs reflection and the library works with trimming and Native AOT (D29). The transport resolves
/// types only through this context: a type missing here fails at runtime, which the endpoint's
/// unit tests catch. Use <see cref="Instance"/>, not <c>Default</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Requests
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(UserIdentifier))]
[JsonSerializable(typeof(MatrixClient.RefreshRequest))]
[JsonSerializable(typeof(MatrixClient.LeaveRequest))]
// Event content: raw, and the modelled message types (through MessageContentConverter)
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(MessageContent))]
[JsonSerializable(typeof(TextMessageContent))]
// Responses
[JsonSerializable(typeof(MatrixTransport.ErrorResponse))]
[JsonSerializable(typeof(MatrixServer.LoginFlowsResponse))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(VersionsResponse))]
[JsonSerializable(typeof(WhoAmIResponse))]
[JsonSerializable(typeof(MatrixClient.RefreshResponse))]
[JsonSerializable(typeof(MatrixClient.JoinedRoomsResponse))]
[JsonSerializable(typeof(MatrixClient.EmptyResponse))]
[JsonSerializable(typeof(MatrixClient.SendEventResponse))]
// Stored by apps; its property names are fixed by attributes, not by the naming policy
[JsonSerializable(typeof(MatrixSession))]
internal sealed partial class MatrixJsonContext : JsonSerializerContext
{
    /// <summary>
    /// The context the library uses. It differs from <c>Default</c> only in escaping: the attribute
    /// above cannot set an encoder. Non-ASCII text and HTML characters are written as they are rather
    /// than as <c>\uXXXX</c>, which keeps messages readable and non-Latin text about half the size
    /// (D31). The other settings must match the attribute.
    /// </summary>
    public static MatrixJsonContext Instance { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}
