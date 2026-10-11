namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// The single implementation of <see cref="ISharedEndpoints"/>. Whether a token is sent depends
/// only on how its owner fetches; each owner keeps its own <c>/versions</c> cache (D25).
/// </summary>
internal sealed class SharedEndpoints(
    Func<CancellationToken, Task<VersionsResponse>> fetchVersions, TimeSpan versionsLifetime, TimeProvider time)
    : ISharedEndpoints
{
    public VersionsCache Versions { get; } = new(fetchVersions, versionsLifetime, time);

    public Task<VersionsResponse> GetVersionsAsync(CancellationToken cancellationToken = default) =>
        Versions.GetAsync(cancellationToken);

    public Task<VersionsResponse> RefreshVersionsAsync(CancellationToken cancellationToken = default) =>
        Versions.RefreshAsync(cancellationToken);

    public async Task<bool> SupportsAsync(MatrixFeature feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return (await Versions.GetAsync(cancellationToken)).Supports(feature);
    }
}
