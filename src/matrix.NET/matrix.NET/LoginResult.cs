namespace TeamBanana.MatrixDotNet;

/// <summary>The result of <see cref="MatrixServer.LoginAsync"/>.</summary>
public sealed class LoginResult
{
    internal LoginResult(MatrixSession session, DiscoveryInformation? wellKnown)
    {
        Session = session;
        WellKnown = wellKnown;
    }

    /// <summary>The new session. The app is responsible for saving it (D23).</summary>
    public MatrixSession Session { get; }

    /// <summary>
    /// Discovery information the homeserver sent with the login, if any, which the spec says clients
    /// should reconfigure themselves with. It is not applied or validated: <see cref="Session"/> keeps
    /// the homeserver the login went to (D36). <see langword="null"/> if absent or malformed.
    /// </summary>
    public DiscoveryInformation? WellKnown { get; }
}
