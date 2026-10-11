namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The result of <see cref="MatrixServer.DiscoverAsync(string, CancellationToken)"/>: a homeserver that
/// answered <c>GET /versions</c> (D36).
/// </summary>
public sealed class ServerDiscovery
{
    internal ServerDiscovery(Uri homeserver, VersionsResponse versions, DiscoveryInformation information)
    {
        Homeserver = homeserver;
        Versions = versions;
        Information = information;
    }

    /// <summary>The homeserver's base URL, to create a <see cref="MatrixServer"/> with.</summary>
    public Uri Homeserver { get; }

    /// <summary>
    /// The homeserver's answer to <c>GET /versions</c>, which validated it. Check
    /// <see cref="VersionsResponse.MeetsMinimumVersion"/> before going on: discovery does not refuse
    /// an older homeserver (D24).
    /// </summary>
    public VersionsResponse Versions { get; }

    /// <summary>
    /// The identity server's base URL, if one was advertised with a valid URL. <b>Not validated</b>: the
    /// library does not support identity servers yet, so it does not check that one answers there, as
    /// the spec asks (D36).
    /// </summary>
    public Uri? IdentityServer { get; init; }

    /// <summary>
    /// Why the advertised identity server could not be used, if one was advertised but its
    /// <c>base_url</c> is missing (<see cref="DiscoveryFailureKind.FailPrompt"/>) or not a URL
    /// (<see cref="DiscoveryFailureKind.FailError"/>). It does not affect the homeserver.
    /// </summary>
    public DiscoveryFailureKind? IdentityServerFailure { get; init; }

    /// <summary>The discovery information as received, including keys for applications.</summary>
    public DiscoveryInformation Information { get; }
}
