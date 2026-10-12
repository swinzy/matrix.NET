namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Endpoints usable with or without a session (D19). <see cref="MatrixServer"/> and
/// <see cref="MatrixClient"/> both implement it, so neither can fall behind the other.
/// </summary>
internal interface ISharedEndpoints
{
    Task<VersionsResponse> GetVersionsAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default);

    Task<VersionsResponse> RefreshVersionsAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> SupportsAsync(MatrixFeature feature, IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default);
}