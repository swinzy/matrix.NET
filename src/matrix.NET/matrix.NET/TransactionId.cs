namespace TeamBanana.MatrixDotNet;

/// <summary>
/// Creates transaction IDs, which let the homeserver recognise a repeated request so that, e.g., a
/// message is not sent twice (D28).
/// </summary>
/// <remarks>
/// Endpoints that take a <c>transactionId</c> create one if none is given. Create it yourself
/// before sending when you may need it after a failure: to retry the request safely, pass the same
/// ID again; to match your own message when it comes back through sync, compare it with the
/// event's <c>unsigned.transaction_id</c>.
/// </remarks>
public static class TransactionId
{
    /// <summary>Returns a new, random transaction ID: a version 4 UUID as 32 hexadecimal digits.</summary>
    public static string New() => Guid.NewGuid().ToString("N");
}
