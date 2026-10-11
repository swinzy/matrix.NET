using TeamBanana.MatrixDotNet.Transport;

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

    /// <summary>
    /// How long the homeserver's <c>GET /versions</c> is cached before the next call that needs it fetches
    /// it again (D25). Defaults to one day. <see cref="TimeSpan.Zero"/> fetches it every time;
    /// <see cref="Timeout.InfiniteTimeSpan"/> keeps it until <see cref="MatrixServer.RefreshVersionsAsync"/>
    /// is called, so the app decides when to pay for the request.
    /// </summary>
    public TimeSpan VersionsCacheLifetime { get; init; } = VersionsCache.DefaultLifetime;

    // Replaced by tests, to expire the cache without waiting
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
