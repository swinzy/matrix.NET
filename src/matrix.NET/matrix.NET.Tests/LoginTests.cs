using System.Net;
using System.Text.Json;

namespace TeamBanana.MatrixDotNet.Tests;

public class LoginTests
{
    private static Server CreateServer(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://matrix.example.org/") });

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

        await CreateServer(handler).LoginAsync(PasswordLogin());

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/login", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task LoginAsync_SerialisesPasswordLoginAsSpecified()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);
        var request = PasswordLogin();
        request.InitialDeviceDisplayName = "Jungle Phone";

        await CreateServer(handler).LoginAsync(request);

        // Matches the request example in the spec; null fields must not be sent
        Assert.Equal(
            """{"type":"m.login.password","identifier":{"type":"m.id.user","user":"cheeky_monkey"},"password":"ilovebananas","initial_device_display_name":"Jungle Phone"}""",
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

        await CreateServer(handler).LoginAsync(request);

        Assert.Equal(
            """{"type":"m.login.token","token":"login-token","device_id":"GHTYAJCE","refresh_token":true}""",
            handler.RequestBody);
    }

    [Fact]
    public async Task LoginAsync_ParsesFullResponse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@cheeky_monkey:matrix.org","access_token":"abc123","refresh_token":"def456","expires_in_ms":60000,"device_id":"GHTYAJCE","home_server":"matrix.org"}""");

        var response = await CreateServer(handler).LoginAsync(PasswordLogin());

        Assert.Equal("@cheeky_monkey:matrix.org", response.UserId);
        Assert.Equal("abc123", response.AccessToken);
        Assert.Equal("GHTYAJCE", response.DeviceId);
        Assert.Equal("def456", response.RefreshToken);
        Assert.Equal(60000, response.ExpiresInMs);
    }

    [Fact]
    public async Task LoginAsync_LeavesOptionalFieldsNullWhenAbsent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, MinimalResponse);

        var response = await CreateServer(handler).LoginAsync(PasswordLogin());

        Assert.Null(response.RefreshToken);
        Assert.Null(response.ExpiresInMs);
    }

    [Fact]
    public async Task LoginAsync_ThrowsWhenRequiredResponseFieldMissing()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@cheeky_monkey:matrix.org","device_id":"GHTYAJCE"}""");

        await Assert.ThrowsAsync<JsonException>(() => CreateServer(handler).LoginAsync(PasswordLogin()));
    }

    [Fact]
    public async Task LoginAsync_ThrowsMatrixExceptionOnMatrixError()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden,
            """{"errcode":"M_FORBIDDEN","error":"Invalid username or password"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).LoginAsync(PasswordLogin()));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("M_FORBIDDEN", exception.ErrorCode);
        Assert.Equal("Invalid username or password", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_UsesErrorCodeAsMessageWhenErrorAbsent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden, """{"errcode":"M_FORBIDDEN"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).LoginAsync(PasswordLogin()));

        Assert.Equal("M_FORBIDDEN", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_ThrowsUnknownErrorOnNonJsonErrorBody()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "text/html");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).LoginAsync(PasswordLogin()));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("M_UNKNOWN", exception.ErrorCode);
    }
}