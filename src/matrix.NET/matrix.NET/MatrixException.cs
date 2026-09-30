using System.Net;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// An error response from the homeserver, or an HTTP failure that is not a Matrix error, whose
/// <see cref="ErrorCode"/> is then <see cref="MatrixErrorCodes.Unknown"/>.
/// </summary>
public class MatrixException : Exception
{
    /// <summary>Creates an exception whose message is the homeserver's own.</summary>
    public MatrixException(HttpStatusCode statusCode, string errorCode, string? serverMessage)
        : this(statusCode, errorCode, serverMessage, serverMessage ?? errorCode)
    {
    }

    /// <summary>Creates an exception with a message of the library's own.</summary>
    public MatrixException(HttpStatusCode statusCode, string errorCode, string? serverMessage, string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ServerMessage = serverMessage;
    }

    /// <summary>The HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The Matrix error code, e.g. <see cref="MatrixErrorCodes.Forbidden"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>The homeserver's own human-readable message, if it sent one.</summary>
    public string? ServerMessage { get; }
}