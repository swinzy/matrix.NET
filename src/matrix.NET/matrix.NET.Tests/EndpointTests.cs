using System.Net;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

public class EndpointTests
{
    private static readonly Endpoint Template =
        Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/rooms/{roomId}/state/{eventType}", AuthRequirement.Required);

    [Fact]
    public void Bind_FillsParametersInOrderAndEncodesThem()
    {
        var bound = Template.Bind("#alias:example.org", "m.room/name");

        Assert.Equal("_matrix/client/v3/rooms/%23alias%3Aexample.org/state/m.room%2Fname", bound.Path);
        Assert.False(bound.HasParameters);
        Assert.Equal(Template.Method, bound.Method);
        Assert.Equal(Template.Auth, bound.Auth);
    }

    [Fact]
    public void WithQuery_EncodesAndRepeatsNames()
    {
        var endpoint = Template.Bind("!a:b", "m.x").WithQuery([("via", "a.example"), ("via", "b&c"), ("q", "x y")]);

        Assert.Equal("via=a.example&via=b%26c&q=x%20y", endpoint.Query);
        Assert.Equal(endpoint.Path + "?" + endpoint.Query, endpoint.PathAndQuery);
    }

    [Fact]
    public void WithQuery_WithoutParametersChangesNothing()
    {
        var bound = Template.Bind("!a:b", "m.x");

        Assert.Same(bound, bound.WithQuery([]));
        Assert.Equal(bound.Path, bound.PathAndQuery);
    }

    [Fact]
    public void Bind_RejectsWrongNumberOfValues()
    {
        Assert.Throws<ArgumentException>(() => Template.Bind("!abc:example.org"));
    }

    [Fact]
    public void Bind_RejectsEmptyValuesByParameterName()
    {
        var exception = Assert.Throws<ArgumentException>(() => Template.Bind("!abc:example.org", ""));

        Assert.Equal("eventType", exception.ParamName);
    }

    [Fact]
    public async Task Transport_RefusesUnboundTemplates()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var transport = new MatrixTransport(new Uri("https://matrix.example.org/"), () => new HttpClient(handler), () => "token");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendAsync<WhoAmIResponse>(Template, TestContext.Current.CancellationToken));

        Assert.Null(handler.Request);
    }
}
