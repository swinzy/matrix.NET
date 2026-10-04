namespace TeamBanana.MatrixDotNet;

/// <summary>The outcome of sending an event.</summary>
/// <param name="EventId">The ID the homeserver gave the event.</param>
/// <param name="TransactionId">
/// The transaction ID the request used, given or created (D28). Your own event carries it as
/// <c>unsigned.transaction_id</c> when it comes back through sync.
/// </param>
public sealed record SendEventResult(string EventId, string TransactionId);
