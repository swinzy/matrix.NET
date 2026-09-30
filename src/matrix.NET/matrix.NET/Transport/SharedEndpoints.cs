namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// The single implementation of <see cref="ISharedEndpoints"/>. Whether a token is sent depends
/// only on the transport it is given.
/// </summary>
internal sealed class SharedEndpoints(MatrixTransport transport) : ISharedEndpoints
{
    public Task<VersionsResponse> GetVersionsAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync<VersionsResponse>(HttpMethod.Get, "_matrix/client/versions",
            AuthRequirement.Optional, cancellationToken);
}