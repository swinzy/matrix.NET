using System.Net;

namespace TeamBanana.MatrixDotNet.Tests;

public class SupportedLoginTypesTests
{
    private static MatrixServer CreateServer(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://matrix.example.org/") });

    [Fact]
    public async Task GetSupportedLoginTypesAsync_GetsLoginEndpoint()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"flows":[]}""");

        await CreateServer(handler).GetSupportedLoginTypesAsync();

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/login", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetSupportedLoginTypesAsync_ParsesFlows()
    {
        // Response example from the spec
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"flows":[{"type":"m.login.password"},{"type":"m.login.token","get_login_token":true}]}""");

        var flows = await CreateServer(handler).GetSupportedLoginTypesAsync();

        Assert.Collection(flows,
            flow =>
            {
                Assert.Equal("m.login.password", flow.Type);
                Assert.Null(flow.GetLoginToken);
            },
            flow =>
            {
                Assert.Equal("m.login.token", flow.Type);
                Assert.True(flow.GetLoginToken);
            });
    }

    [Fact]
    public async Task GetSupportedLoginTypesAsync_ReturnsEmptyWhenFlowsAbsent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        var flows = await CreateServer(handler).GetSupportedLoginTypesAsync();

        Assert.Empty(flows);
    }

    [Fact]
    public async Task GetSupportedLoginTypesAsync_ThrowsWhenLegacyLoginUnsupported()
    {
        // Returned by homeservers that only support OAuth 2.0 authentication
        var handler = new StubHttpMessageHandler(HttpStatusCode.NotFound,
            """{"errcode":"M_UNRECOGNIZED","error":"OAuth 2.0 authentication is in use on this homeserver."}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateServer(handler).GetSupportedLoginTypesAsync());

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("M_UNRECOGNIZED", exception.ErrorCode);
    }
}