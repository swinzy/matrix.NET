namespace TeamBanana.MatrixDotNet;

/// <summary>How discovery failed, in the spec's terms, which say what the app should do next.</summary>
public enum DiscoveryFailureKind
{
    /// <summary>
    /// The discovery information is missing or unusable, e.g. an error status, invalid JSON, no
    /// <c>base_url</c>, or the request failed. Tell the user that discovery failed and ask for the
    /// homeserver URL.
    /// </summary>
    FailPrompt,

    /// <summary>
    /// The discovery information is valid but points to no usable homeserver: its URL is invalid, or
    /// nothing answered <c>GET /versions</c> there. Stop the login; the user should decide what to do.
    /// </summary>
    FailError
}

/// <summary>
/// Server discovery failed (D36). <see cref="Kind"/> says whether to ask the user for the homeserver
/// URL or to stop; the cause, if any, is the inner exception.
/// </summary>
public class MatrixDiscoveryException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MatrixDiscoveryException(DiscoveryFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>How discovery failed.</summary>
    public DiscoveryFailureKind Kind { get; }
}
