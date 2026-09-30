using System.Net;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// An administrator has locked the account (<see cref="MatrixErrorCodes.UserLocked"/>). Unlike an
/// unknown token, the session stays valid and becomes usable again once the account is unlocked.
/// </summary>
public class MatrixUserLockedException : MatrixException
{
    /// <summary>Creates an exception whose message is the homeserver's own.</summary>
    public MatrixUserLockedException(HttpStatusCode statusCode, string? serverMessage, bool softLogout)
        : base(statusCode, MatrixErrorCodes.UserLocked, serverMessage)
    {
        SoftLogout = softLogout;
    }

    /// <summary>
    /// Whether the homeserver marked the response as a soft logout; the spec requires
    /// <see langword="true"/> for locked accounts, so local data should be kept.
    /// </summary>
    public bool SoftLogout { get; }
}