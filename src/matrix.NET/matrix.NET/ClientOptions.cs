namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Options for a <see cref="MatrixClient"/>.
/// </summary>
public sealed class ClientOptions
{
    /// <summary>
    /// Whether the library refreshes the access token itself when the homeserver rejects it and
    /// the session has a refresh token. Turn it off to manage refresh in the app. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AutoRefreshToken { get; init; } = true;

    /// <summary>
    /// Whether compressed responses (GZip, Deflate, Brotli) are requested and decompressed
    /// automatically. Only applies when the client uses the library's shared
    /// <see cref="HttpClient"/>; a caller-supplied client keeps its own settings. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AutomaticDecompression { get; init; } = true;
}