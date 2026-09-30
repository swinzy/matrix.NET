using System.Net;
using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Tests;

public class LoginTests
{
    private static MatrixServer CreateServer(StubHttpMessageHandler handler) =>
        new(new Uri("https://matrix.example.org/"), new HttpClient(handler));

    private static LoginRequest PasswordLogin() => new()
    {
        Type = "m.login.password",
        Identifier = new UserIdentifier { User = "cheeky_monkey" },
        Password = "ilovebananas"
    };

    private const string MinimalResponse =
        """{"user_id":"@cheeky_monkey:matrix.org","access_token":"abc123","device_id":"GHTYAJCE"}""";

    [Fact]
    public async Task LoginAsync_PostsToLoginEndpoint()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);

        await CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/login", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task LoginAsync_SerialisesPasswordLoginAsSpecified()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);
        var request = PasswordLogin();
        request.InitialDeviceDisplayName = "Jungle Phone";

        await CreateServer(handler).LoginAsync(request, TestContext.Current.CancellationToken);

        // The spec's request example plus refresh_token, which is requested by default (D12);
        // null fields must not be sent
        Assert.Equal(
            """{"type":"m.login.password","identifier":{"type":"m.id.user","user":"cheeky_monkey"},"password":"ilovebananas","initial_device_display_name":"Jungle Phone","refresh_token":true}""",
            handler.RequestBody);
    }

    [Fact]
    public async Task LoginAsync_CanOptOutOfRefreshToken()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);
        var request = PasswordLogin();
        request.RefreshToken = false;

        await CreateServer(handler).LoginAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"type":"m.login.password","identifier":{"type":"m.id.user","user":"cheeky_monkey"},"password":"ilovebananas","refresh_token":false}""",
            handler.RequestBody);
    }

    [Fact]
    public async Task LoginAsync_SerialisesTokenLoginOptions()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);
        var request = new LoginRequest
        {
            Type = "m.login.token",
            Token = "login-token",
            DeviceId = "GHTYAJCE",
            RefreshToken = true
        };

        await CreateServer(handler).LoginAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"type":"m.login.token","token":"login-token","device_id":"GHTYAJCE","refresh_token":true}""",
            handler.RequestBody);
    }

    [Fact]
    public async Task LoginAsync_ParsesFullResponse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@cheeky_monkey:matrix.org","access_token":"abc123","refresh_token":"def456","expires_in_ms":60000,"device_id":"GHTYAJCE","home_server":"matrix.org"}""");

        var before = DateTimeOffset.UtcNow;
        var session = await CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(new Uri("https://matrix.example.org/"), session.Homeserver);
        Assert.Equal("@cheeky_monkey:matrix.org", session.UserId);
        Assert.Equal("abc123", session.AccessToken);
        Assert.Equal("GHTYAJCE", session.DeviceId);
        Assert.Equal("def456", session.RefreshToken);
        // expires_in_ms is converted to an absolute time when the response arrives
        Assert.InRange(session.ExpiresAt!.Value, before.AddMilliseconds(60000), after.AddMilliseconds(60000));
    }

    [Fact]
    public async Task LoginAsync_LeavesOptionalFieldsNullWhenAbsent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);

        var session = await CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken);

        Assert.Null(session.RefreshToken);
        Assert.Null(session.ExpiresAt);
    }

    [Fact]
    public async Task LoginAsync_ThrowsWhenRequiredResponseFieldMissing()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@cheeky_monkey:matrix.org","device_id":"GHTYAJCE"}""");

        await Assert.ThrowsAsync<JsonException>(() => CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_ThrowsMatrixExceptionOnMatrixError()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden,
            """{"errcode":"M_FORBIDDEN","error":"Invalid username or password"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("M_FORBIDDEN", exception.ErrorCode);
        Assert.Equal("Invalid username or password", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_UsesErrorCodeAsMessageWhenErrorAbsent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden, """{"errcode":"M_FORBIDDEN"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken));

        Assert.Equal("M_FORBIDDEN", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_ThrowsUnknownErrorOnNonJsonErrorBody()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "text/html");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).LoginAsync(PasswordLogin(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("M_UNKNOWN", exception.ErrorCode);
    }
}