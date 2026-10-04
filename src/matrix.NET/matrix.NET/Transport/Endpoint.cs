using System.Text.RegularExpressions;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Everything the library knows about one endpoint of the Client-Server API. Every endpoint is
/// declared once, in <see cref="Endpoints"/>, and requests are sent only through these declarations.
/// </summary>
/// <remarks>
/// There is no public constructor: <see cref="Baseline"/> and <see cref="Since"/> make every
/// declaration state, visibly, whether the endpoint needs a <see cref="MatrixFeature"/> (D26).
/// </remarks>
internal sealed partial class Endpoint
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
    /// The path relative to the homeserver. In <see cref="Endpoints"/> it is a template with
    /// parameters named as in the spec, e.g. <c>_matrix/client/v3/rooms/{roomId}/leave</c>;
    /// <see cref="Bind"/> fills them in.
    /// </summary>
    public string Path { get; }

    /// <summary>Whether <see cref="Path"/> still has parameters to fill in.</summary>
    public bool HasParameters => Parameter().IsMatch(Path);

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

    /// <summary>
    /// Returns this endpoint with its path parameters replaced, in order, by
    /// <paramref name="values"/>, each percent-encoded, so IDs such as <c>!room:example.org</c> or
    /// <c>#alias:example.org</c> cannot change the path.
    /// </summary>
    public Endpoint Bind(params ReadOnlySpan<string> values)
    {
        var parameters = Parameter().Matches(Path);
        if (parameters.Count != values.Length)
            throw new ArgumentException($"{this} takes {parameters.Count} path parameters, not {values.Length}.",
                nameof(values));

        var path = Path;
        // From the end, so earlier match positions stay valid
        for (var i = parameters.Count - 1; i >= 0; i--)
        {
            ArgumentException.ThrowIfNullOrEmpty(values[i], parameters[i].Value.Trim('{', '}'));
            path = path[..parameters[i].Index] + Uri.EscapeDataString(values[i]) +
                   path[(parameters[i].Index + parameters[i].Length)..];
        }

        return new Endpoint(Method, path, Auth, Feature);
    }

    public override string ToString() => $"{Method} /{Path}";

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();
}
