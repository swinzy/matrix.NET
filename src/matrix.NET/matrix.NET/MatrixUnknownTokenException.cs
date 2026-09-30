using System.Net;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The homeserver did not recognise the access or refresh token (<see cref="MatrixErrorCodes.UnknownToken"/>).
/// </summary>
/// <remarks>
/// Thrown by <see cref="MatrixClient"/> once the token cannot be refreshed; the client is then
/// unusable, and the app should log in again.
/// </remarks>
public class MatrixUnknownTokenException : MatrixException
{
    /// <summary>Creates an exception whose message is the homeserver's own.</summary>
    public MatrixUnknownTokenException(HttpStatusCode statusCode, string? serverMessage, bool softLogout)
        : this(statusCode, serverMessage, softLogout, serverMessage ?? MatrixErrorCodes.UnknownToken)
    {
    }

    /// <summary>Creates an exception with a message of the library's own.</summary>
    public MatrixUnknownTokenException(HttpStatusCode statusCode, string? serverMessage, bool softLogout,
        string message, Exception? innerException = null)
        : base(statusCode, MatrixErrorCodes.UnknownToken, serverMessage, message, innerException)
    {
        SoftLogout = softLogout;
    }

    /// <summary>
    /// <see langword="true"/> if the homeserver kept the device: local data such as encryption keys
    /// may be reused by logging in again with the same device ID. <see langword="false"/> if the
    /// session was destroyed, so local data must be discarded.
    /// </summary>
    public bool SoftLogout { get; }
}