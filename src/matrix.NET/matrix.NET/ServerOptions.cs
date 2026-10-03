namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Options for a <see cref="MatrixServer"/>.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Whether compressed responses (GZip, Deflate, Brotli) are requested and decompressed
    /// automatically. Turn it off only if you need the raw encoded bytes, e.g. when forwarding
    /// responses unchanged. Only applies when the server uses the library's shared
    /// <see cref="HttpClient"/>; a caller-supplied client keeps its own settings. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AutomaticDecompression { get; init; } = true;
}
