using System.Net;
using TeamBanana.MatrixDotNet;

// Exercises the library's serialisation paths in a Native AOT build. Offline checks always run;
// the homeserver checks run when MATRIX_TEST_HOMESERVER, _USER and _PASSWORD are set, and expect
// the local Synapse from tools/synapse/synapse.sh (2-second refreshable tokens, no rate limits).
var failures = 0;

async Task Check(string name, Func<Task> check)
{
    try
    {
        await check();
        Console.WriteLine($"ok   {name}");
    }
    catch (Exception e)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}");
    }
}

void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

await Check("session round trip", () =>
{
    var session = new MatrixSession
    {
        Homeserver = new Uri("https://matrix.example.org/"), UserId = "@a:example.org", DeviceId = "D",
        AccessToken = "t", RefreshToken = "r", ExpiresAt = DateTimeOffset.UnixEpoch
    };
    Require(MatrixSession.FromJson(session.ToJson()) == session, "round trip changed the session");
    return Task.CompletedTask;
});

await Check("session without format_version", () =>
{
    var session = MatrixSession.FromJson(
        """{"homeserver":"https://matrix.example.org/","user_id":"@a:example.org","device_id":"D","access_token":"t"}""");
    Require(session.FormatVersion == 1, $"format version {session.FormatVersion}");
    return Task.CompletedTask;
});

var homeserver = Environment.GetEnvironmentVariable("MATRIX_TEST_HOMESERVER");
var user = Environment.GetEnvironmentVariable("MATRIX_TEST_USER");
var password = Environment.GetEnvironmentVariable("MATRIX_TEST_PASSWORD");
if (homeserver is null || user is null || password is null)
{
    Console.WriteLine("skip homeserver checks: MATRIX_TEST_* not set");
    return failures == 0 ? 0 : 1;
}

var server = new MatrixServer(new Uri(homeserver));

await Check("versions", async () =>
    Require((await server.GetVersionsAsync()).Versions.Count > 0, "no versions"));

await Check("login flows", async () =>
    Require((await server.GetSupportedLoginTypesAsync()).Any(f => f.Type == "m.login.password"), "no password login"));

await Check("error response", async () =>
{
    try
    {
        await server.LoginAsync(new LoginRequest
        {
            Type = "m.login.password", Identifier = new UserIdentifier { User = user }, Password = password + "-wrong"
        });
        throw new InvalidOperationException("wrong password was accepted");
    }
    catch (MatrixException e) when (e.StatusCode == HttpStatusCode.Forbidden)
    {
        Require(e.ErrorCode == MatrixErrorCodes.Forbidden, e.ErrorCode);
    }
});

MatrixClient? client = null;
var refreshed = 0;
await Check("login and whoami", async () =>
{
    var session = await server.LoginAsync(new LoginRequest
    {
        Type = "m.login.password", Identifier = new UserIdentifier { User = user }, Password = password,
        InitialDeviceDisplayName = "matrix.NET AOT smoke test"
    });
    client = new MatrixClient(session, new ClientOptions
    {
        SessionRefreshHandler = new CountingHandler(() => refreshed++)
    });
    Require((await client.WhoAmIAsync()).UserId == session.UserId, "whoami returned another user");
});

if (client is not null)
{
    await Check("joined rooms", async () => await client.GetJoinedRoomsAsync());

    await Check("error on unknown room", async () =>
    {
        try
        {
            await client.LeaveRoomAsync("!nonexistent:localhost");
            throw new InvalidOperationException("leaving an unknown room succeeded");
        }
        catch (MatrixException e)
        {
            Require(e.StatusCode == HttpStatusCode.NotFound, $"{(int)e.StatusCode} {e.ErrorCode}");
        }
    });

    await Check("token refresh", async () =>
    {
        var expiresAt = client.Session.ExpiresAt ?? throw new InvalidOperationException("token does not expire");
        await Task.Delay(expiresAt - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        await client.WhoAmIAsync();
        Require(refreshed == 1, $"refreshed {refreshed} times");
    });

    await Check("logout", async () => await client.LogoutAsync());
}

return failures == 0 ? 0 : 1;

sealed class CountingHandler(Action onRefreshed) : ISessionRefreshHandler
{
    public ValueTask OnSessionRefreshedAsync(MatrixSession session, CancellationToken cancellationToken)
    {
        onRefreshed();
        return ValueTask.CompletedTask;
    }
}
