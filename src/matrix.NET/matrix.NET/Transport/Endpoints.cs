namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Every endpoint the library calls. Checked against the spec by <c>SpecConformanceTests</c>, which
/// finds the endpoints by reflection, so each must be a static field here.
/// </summary>
internal static class Endpoints
{
    public static readonly Endpoint Versions =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/versions", AuthRequirement.Optional);

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
}
