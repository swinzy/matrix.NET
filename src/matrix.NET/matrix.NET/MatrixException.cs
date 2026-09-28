using System.Net;

namespace TeamBanana.MatrixDotNet;

public class MatrixException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ErrorCode { get; }

    public MatrixException(HttpStatusCode statusCode, string errorCode, string? error)
        : base(error ?? errorCode)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}