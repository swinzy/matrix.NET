using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixClientTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static MatrixClient CreateClient(StubHttpMessageHandler handler) =>
        new(Session, new HttpClient(handler));

    [Fact]
    public void Constructor_SendsNothing()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        _ = CreateClient(handler);

        Assert.Null(handler.Request);
    }

    [Fact]
    public void Constructor_RejectsNullSession()
    {
        Assert.Throws<ArgumentNullException>(() => new MatrixClient(null!));
    }

    [Fact]
    public void Properties_ExposeTheSession()
    {
        var client = new MatrixClient(Session);

        Assert.Same(Session, client.Session);
        Assert.Equal("@joe:example.org", client.UserId);
        Assert.Equal("ABC1234", client.DeviceId);
        Assert.True(client.Options.AutoRefreshToken);
        Assert.True(client.Options.AutomaticDecompression);
    }

    [Fact]
    public async Task WhoAmIAsync_SendsTokenToSessionHomeserver()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"user_id":"@joe:example.org"}""");

        await CreateClient(handler).WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/account/whoami", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task WhoAmIAsync_ParsesSpecExample()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"user_id":"@joe:example.org","device_id":"ABC1234"}""");

        var response = await CreateClient(handler).WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal("@joe:example.org", response.UserId);
        Assert.Equal("ABC1234", response.DeviceId);
        // Absent is_guest means not a guest
        Assert.False(response.IsGuest);
    }

    [Fact]
    public async Task WhoAmIAsync_ParsesGuestAndMissingDevice()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"user_id":"@guest:example.org","is_guest":true}""");

        var response = await CreateClient(handler).WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsGuest);
        Assert.Null(response.DeviceId);
    }

    // The same endpoint on MatrixServer sends no token (D19)
    [Fact]
    public async Task GetVersionsAsync_SendsToken()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"versions":["v1.1"]}""");

        await CreateClient(handler).GetVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://matrix.example.org/_matrix/client/versions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task HttpClientSource_IsCalledPerRequest()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"user_id":"@joe:example.org"}""");
        var clientsCreated = 0;
        var client = new MatrixClient(Session, () =>
        {
            clientsCreated++;
            return new HttpClient(handler);
        });

        await client.WhoAmIAsync(TestContext.Current.CancellationToken);
        await client.WhoAmIAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, clientsCreated);
    }
}