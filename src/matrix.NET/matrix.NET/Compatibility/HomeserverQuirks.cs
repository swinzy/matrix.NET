using System.Net;

namespace TeamBanana.MatrixDotNet.Compatibility;

/// <summary>
/// Homeserver behaviour that deviates from the spec and that the library tolerates (D27). The rest
/// of the library implements the spec; every workaround lives here and names the implementation,
/// the spec text it deviates from, and the source it was confirmed in.
/// </summary>
internal static class HomeserverQuirks
{
    /// <summary>
    /// Whether a failed <c>POST /refresh</c> that the spec does not cover still means the refresh
    /// token is dead for good. The spec's own case, <c>M_UNKNOWN_TOKEN</c>, is handled by the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spec answers an unknown or already used refresh token with 401 <c>M_UNKNOWN_TOKEN</c>, and
    /// MSC2918 says the server must. Synapse answers 403 <c>M_FORBIDDEN</c> instead when the refresh
    /// token was already used ("refresh token isn't valid anymore") or has expired ("The supplied
    /// refresh token has expired"), in <c>AuthHandler.refresh_token</c> in
    /// <c>synapse/handlers/auth.py</c> (checked October 2026). Treating that as transient would leave
    /// the client refreshing and failing on every request without ever ending the session.
    /// </para>
    /// <para>
    /// Rather than matching Synapse's exact code, any 4xx counts, so similar deviations elsewhere
    /// end the session too. <c>/refresh</c> has no other purpose, so a client error never means that
    /// trying again later would work. The exceptions are rate limiting, which is temporary, and a
    /// locked account, which has its own state (D22).
    /// </para>
    /// </remarks>
    public static bool IsFinalRefreshFailure(MatrixException exception) =>
        exception is not MatrixUserLockedException &&
        exception.ErrorCode != MatrixErrorCodes.LimitExceeded &&
        exception.StatusCode != HttpStatusCode.TooManyRequests &&
        (int)exception.StatusCode is >= 400 and < 500;
}
