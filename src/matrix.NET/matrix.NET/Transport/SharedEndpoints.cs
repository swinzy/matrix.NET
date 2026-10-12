namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// The single implementation of <see cref="ISharedEndpoints"/>. Whether a token is sent depends
/// only on how its owner fetches; each owner keeps its own <c>/versions</c> cache (D25).
/// </summary>
internal sealed class SharedEndpoints(
    Func<WaitReporter?, CancellationToken, Task<VersionsResponse>> fetchVersions, TimeSpan versionsLifetime,
    TimeProvider time, Func<IProgress<RequestWait>?, WaitReporter?> startCall)
    : ISharedEndpoints
{
    public VersionsCache Versions { get; } = new(fetchVersions, versionsLifetime, time);

    public Task<VersionsResponse> GetVersionsAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default) =>
        Versions.GetAsync(startCall(progress), cancellationToken);

    public Task<VersionsResponse> RefreshVersionsAsync(IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default) =>
        Versions.RefreshAsync(startCall(progress), cancellationToken);

    public async Task<bool> SupportsAsync(MatrixFeature feature, IProgress<RequestWait>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return (await Versions.GetAsync(startCall(progress), cancellationToken)).Supports(feature);
    }
}
