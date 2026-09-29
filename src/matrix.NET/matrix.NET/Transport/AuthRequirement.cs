namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Whether an endpoint needs an access token, as declared by the spec.
/// </summary>
internal enum AuthRequirement
{
    /// <summary>Never send a token, even if one is available.</summary>
    None,

    /// <summary>Send a token if one is available.</summary>
    Optional,

    /// <summary>A token must be sent.</summary>
    Required
}