namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Every endpoint the library calls. Checked against the spec by <c>SpecConformanceTests</c>, which
/// finds the endpoints by reflection, so each must be a static field here.
/// </summary>
internal static class Endpoints
{
    public static readonly Endpoint Versions =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/versions", AuthRequirement.Optional);

    // Served at the server name's hostname rather than the homeserver (D36)
    public static readonly Endpoint WellKnownClient =
        Endpoint.Baseline(HttpMethod.Get, ".well-known/matrix/client", AuthRequirement.None);

    public static readonly Endpoint LoginFlows =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/login", AuthRequirement.None);

    public static readonly Endpoint Login =
        Endpoint.Baseline(HttpMethod.Post, "_matrix/client/v3/login", AuthRequirement.None);

    public static readonly Endpoint Refresh =
        Endpoint.Since(MatrixFeature.TokenRefresh, HttpMethod.Post, "_matrix/client/v3/refresh", AuthRequirement.None);

    public static readonly Endpoint WhoAmI =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/account/whoami", AuthRequirement.Required);

    public static readonly Endpoint Logout =
        Endpoint.Baseline(HttpMethod.Post, "_matrix/client/v3/logout", AuthRequirement.Required);

    public static readonly Endpoint Sync =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/sync", AuthRequirement.Required);

    public static readonly Endpoint JoinedRooms =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/joined_rooms", AuthRequirement.Required);

    public static readonly Endpoint SendEvent =
        Endpoint.Baseline(HttpMethod.Put, "_matrix/client/v3/rooms/{roomId}/send/{eventType}/{txnId}",
            AuthRequirement.Required);

    public static readonly Endpoint Redact =
        Endpoint.Baseline(HttpMethod.Put, "_matrix/client/v3/rooms/{roomId}/redact/{eventId}/{txnId}",
            AuthRequirement.Required);

    public static readonly Endpoint CreateRoom =
        Endpoint.Baseline(HttpMethod.Post, "_matrix/client/v3/createRoom", AuthRequirement.Required);

    public static readonly Endpoint JoinRoom =
        Endpoint.Baseline(HttpMethod.Post, "_matrix/client/v3/join/{roomIdOrAlias}", AuthRequirement.Required);

    public static readonly Endpoint LeaveRoom =
        Endpoint.Baseline(HttpMethod.Post, "_matrix/client/v3/rooms/{roomId}/leave", AuthRequirement.Required);
}
