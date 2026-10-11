using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>Server discovery through <c>/.well-known/matrix/client</c>, as the spec's steps describe (D36).</summary>
internal static class Discovery
{
    public static async Task<ServerDiscovery?> DiscoverAsync(string userIdOrServerName,
        Func<HttpClient>? httpClientSource, CancellationToken cancellationToken)
    {
        var hostname = GetHostname(userIdOrServerName);
        var wellKnownServer = new MatrixTransport(new Uri($"https://{hostname}/"), httpClientSource);

        DiscoveryInformation information;
        try
        {
            information = await wellKnownServer.SendAsync<DiscoveryInformation>(Endpoints.WellKnownClient, cancellationToken);
        }
        catch (MatrixException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // IGNORE: the server name publishes no discovery information, which is not an error
            return null;
        }
        catch (Exception e) when (IsRequestFailure(e, cancellationToken))
        {
            throw new MatrixDiscoveryException(DiscoveryFailureKind.FailPrompt,
                $"Could not read the discovery information of {hostname}: {e.Message}", e);
        }

        if (information.Homeserver?.BaseUrl is not { } baseUrl)
            throw new MatrixDiscoveryException(DiscoveryFailureKind.FailPrompt,
                $"The discovery information of {hostname} has no m.homeserver base_url.");
        if (!TryParseBaseUrl(baseUrl, out var homeserver))
            throw new MatrixDiscoveryException(DiscoveryFailureKind.FailError,
                $"The discovery information of {hostname} gives a homeserver base_url that is not an http or https URL: {baseUrl}");

        VersionsResponse versions;
        try
        {
            var server = httpClientSource is null ? new MatrixServer(homeserver) : new MatrixServer(homeserver, httpClientSource);
            versions = await server.GetVersionsAsync(cancellationToken);
        }
        catch (Exception e) when (IsRequestFailure(e, cancellationToken))
        {
            throw new MatrixDiscoveryException(DiscoveryFailureKind.FailError,
                $"The homeserver {homeserver} that {hostname} points to did not answer GET /versions: {e.Message}", e);
        }

        // Not validated with GET /_matrix/identity/v2, as the spec asks, because the library does not
        // support identity servers yet. Validate it here once it does (D36)
        Uri? identityServer = null;
        DiscoveryFailureKind? identityServerFailure = null;
        if (information.IdentityServer is { } identity)
        {
            if (identity.BaseUrl is null)
                identityServerFailure = DiscoveryFailureKind.FailPrompt;
            else if (TryParseBaseUrl(identity.BaseUrl, out var url))
                identityServer = url;
            else
                identityServerFailure = DiscoveryFailureKind.FailError;
        }

        return new ServerDiscovery(homeserver, versions, information)
        {
            IdentityServer = identityServer,
            IdentityServerFailure = identityServerFailure
        };
    }

    /// <summary>
    /// The hostname to look up for a user ID or server name, without any port: the spec says to
    /// "extract the hostname from the server name as described by the grammar", where
    /// <c>server_name = hostname [ ":" port ]</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The input is not a user ID or server name.</exception>
    internal static string GetHostname(string userIdOrServerName)
    {
        ArgumentException.ThrowIfNullOrEmpty(userIdOrServerName);
        var serverName = userIdOrServerName;
        if (serverName.StartsWith('@'))
        {
            // The server name follows the first colon
            var colon = serverName.IndexOf(':');
            serverName = colon < 0 ? "" : serverName[(colon + 1)..];
        }

        string hostname, port;
        if (serverName.StartsWith('['))
        {
            var end = serverName.IndexOf(']');
            hostname = end < 0 ? "" : serverName[..(end + 1)];
            port = end < 0 ? "" : serverName[(end + 1)..];
        }
        else
        {
            var colon = serverName.IndexOf(':');
            hostname = colon < 0 ? serverName : serverName[..colon];
            port = colon < 0 ? "" : serverName[colon..];
        }

        var validPort = port.Length == 0 ||
                        (port.Length is >= 2 and <= 6 && port[0] == ':' && port.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0);
        var validHostname = hostname.StartsWith('[')
            ? Uri.CheckHostName(hostname.Trim('[', ']')) == UriHostNameType.IPv6
            : Uri.CheckHostName(hostname) is UriHostNameType.Dns or UriHostNameType.IPv4;
        if (!validPort || !validHostname)
            throw new ArgumentException(
                $"\"{userIdOrServerName}\" is not a user ID, e.g. @alice:example.org, or a server name, e.g. example.org.",
                nameof(userIdOrServerName));

        return hostname.ToLower(CultureInfo.InvariantCulture);
    }

    private static bool TryParseBaseUrl(string text, out Uri url) =>
        Uri.TryCreate(text, UriKind.Absolute, out url!) && url.Scheme is "http" or "https" && url.Host.Length > 0;

    // Anything the request itself can fail with; the caller's own cancellation is not a discovery failure
    private static bool IsRequestFailure(Exception e, CancellationToken cancellationToken) => e switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        MatrixException or HttpRequestException or JsonException => true,
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false
    };
}
