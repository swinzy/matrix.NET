[assembly: AssemblyFixture(typeof(TeamBanana.MatrixDotNet.Tests.LoggedInClientFixture))]

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Logs in to the configured homeserver at most once per test run and shares the client, so
/// integration tests do not trip the homeserver's login rate limit.
/// </summary>
/// <remarks>
/// The login happens lazily, on the first test that asks for the client, so runs without such
/// tests never log in. A failed login is cached as well, so it is not retried by every test.
/// </remarks>
public sealed class LoggedInClientFixture
{
    private readonly Lazy<Task<MatrixClient>> _client;

    public LoggedInClientFixture()
    {
        _client = new Lazy<Task<MatrixClient>>(LogInAsync);
    }

    public TestSettings Settings { get; } = TestSettings.Load();

    /// <summary>Returns the shared client, skipping the calling test if no homeserver is configured.</summary>
    public Task<MatrixClient> GetClientAsync()
    {
        Assert.SkipUnless(Settings.IsConfigured, "Real homeserver not configured; see testsettings.example.json");
        return _client.Value;
    }

    private async Task<MatrixClient> LogInAsync()
    {
        var server = new MatrixServer(new Uri(Settings.Homeserver!));

        // Not tied to any single test's cancellation, as the result is shared
        var session = await server.LoginAsync(new LoginRequest
        {
            Type = "m.login.password",
            Identifier = new UserIdentifier { User = Settings.User! },
            Password = Settings.Password,
            DeviceId = Settings.DeviceId,
            InitialDeviceDisplayName = "matrix.NET integration tests",
            // A non-expiring token keeps the session valid for the whole run
            RefreshToken = false
        });

        return new MatrixClient(session, new ClientOptions { AutoRefreshToken = false });
    }
}