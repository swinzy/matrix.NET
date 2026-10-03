namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Everything the library knows about one endpoint of the Client-Server API. Every endpoint is
/// declared once, in <see cref="Endpoints"/>, and requests are sent only through these declarations.
/// </summary>
/// <remarks>
/// There is no public constructor: <see cref="Baseline"/> and <see cref="Since"/> make every
/// declaration state, visibly, whether the endpoint needs a <see cref="MatrixFeature"/> (D26).
/// </remarks>
internal sealed class Endpoint
{
    private Endpoint(HttpMethod method, string path, AuthRequirement auth, MatrixFeature? feature)
    {
        Method = method;
        Path = path;
        Auth = auth;
        Feature = feature;
    }

    public HttpMethod Method { get; }

    /// <summary>
    /// The path relative to the homeserver, with parameters named as in the spec, e.g.
    /// <c>_matrix/client/v3/rooms/{roomId}/leave</c>.
    /// </summary>
    public string Path { get; }

    public AuthRequirement Auth { get; }

    /// <summary>The feature the endpoint belongs to, or <see langword="null"/> for a baseline endpoint.</summary>
    public MatrixFeature? Feature { get; }

    /// <summary>
    /// Declares an endpoint that every supported homeserver has: it was in the spec by
    /// <see cref="MatrixVersion.Minimum"/> and has not been removed since.
    /// </summary>
    public static Endpoint Baseline(HttpMethod method, string path, AuthRequirement auth) =>
        new(method, path, auth, null);

    /// <summary>Declares an endpoint that only homeservers supporting <paramref name="feature"/> have.</summary>
    public static Endpoint Since(MatrixFeature feature, HttpMethod method, string path, AuthRequirement auth) =>
        new(method, path, auth, feature ?? throw new ArgumentNullException(nameof(feature)));

    public override string ToString() => $"{Method} /{Path}";
}
