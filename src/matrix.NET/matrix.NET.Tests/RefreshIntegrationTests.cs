namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Lets an access token expire on a real Synapse and checks the automatic refresh (D12, D23).
/// Local only: tools/synapse/synapse.sh issues 2-second tokens, while other homeservers use
/// minutes, too long to wait for.
/// </summary>
public class RefreshIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExpiredToken_IsRefreshedSavedAndUsed()
    {
        var settings = TestSettings.Load();
        Assert.SkipUnless(settings.IsConfigured, TestSettings.NotConfiguredMessage);
        Assert.SkipUnless(settings.IsLocal, "Needs the local Synapse's short token lifetime");

        // A device of its own, deleted by the logout at the end
        var session = (await new MatrixServer(new Uri(settings.Homeserver!)).LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = settings.User! },
            Password = settings.Password,
            InitialDeviceDisplayName = "matrix.NET refresh test"
        }, cancellationToken: Ct)).Session;
        Assert.NotNull(session.RefreshToken);
        var expiresAt = Assert.NotNull(session.ExpiresAt);
        Assert.InRange(expiresAt - DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromSeconds(30));

        var saved = new List<MatrixSession>();
        var client = new MatrixClient(session, new ClientOptions
        {
            SessionRefreshHandler = new SavingHandler(saved)
        });
        var changes = new List<SessionChangeKind>();
        client.SessionChanged += (_, e) => changes.Add(e.Kind);

        await Task.Delay(expiresAt - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1), Ct);
        var response = await client.WhoAmIAsync(cancellationToken: Ct);

        Assert.Equal(session.UserId, response.UserId);
        Assert.Equal(session.DeviceId, response.DeviceId);
        var refreshed = Assert.Single(saved);
        Assert.Same(refreshed, client.Session);
        Assert.NotEqual(session.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);
        Assert.Equal([SessionChangeKind.TokensRefreshed], changes);

        await client.LogoutAsync(cancellationToken: Ct);
    }

    // The spec's soft logout recovery: the session ended, but its device and local data may be kept,
    // so logging in again names the same device
    [Fact]
    public async Task SoftLogout_LoggingInAgainKeepsTheDevice()
    {
        var settings = TestSettings.Load();
        Assert.SkipUnless(settings.IsConfigured, TestSettings.NotConfiguredMessage);
        Assert.SkipUnless(settings.IsLocal, "Needs the local Synapse's short token lifetime");
        var server = new MatrixServer(new Uri(settings.Homeserver!));
        LoginRequest Login(string? deviceId) => new()
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = settings.User! },
            Password = settings.Password,
            DeviceId = deviceId,
            InitialDeviceDisplayName = "matrix.NET soft logout test"
        };

        var session = (await server.LoginAsync(Login(null), cancellationToken: Ct)).Session;
        var expiresAt = Assert.NotNull(session.ExpiresAt);
        // Without refresh, the expired token ends the session as a soft logout
        var client = new MatrixClient(session, new ClientOptions { AutoRefreshToken = false });
        var changes = new List<SessionChangedEventArgs>();
        client.SessionChanged += (_, e) => changes.Add(e);

        await Task.Delay(expiresAt - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1), Ct);
        var rejection = await Assert.ThrowsAsync<MatrixUnknownTokenException>(() => client.WhoAmIAsync(cancellationToken: Ct));

        Assert.True(rejection.SoftLogout);
        Assert.Equal(MatrixClientState.Invalidated, client.State);
        var change = Assert.Single(changes);
        Assert.Equal(SessionChangeKind.Invalidated, change.Kind);
        Assert.True(change.SoftLogout);

        var again = (await server.LoginAsync(Login(client.DeviceId), cancellationToken: Ct)).Session;
        var resumed = new MatrixClient(again, new ClientOptions { AutoRefreshToken = false });

        Assert.Equal(session.DeviceId, again.DeviceId);
        Assert.Equal(session.DeviceId, (await resumed.WhoAmIAsync(cancellationToken: Ct)).DeviceId);

        await resumed.LogoutAsync(cancellationToken: Ct);
    }

    private sealed class SavingHandler(List<MatrixSession> saved) : ISessionRefreshHandler
    {
        public ValueTask OnSessionRefreshedAsync(MatrixSession session, CancellationToken cancellationToken)
        {
            saved.Add(session);
            return ValueTask.CompletedTask;
        }
    }
}
