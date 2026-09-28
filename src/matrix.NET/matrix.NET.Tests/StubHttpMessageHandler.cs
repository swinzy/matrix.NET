using System.Net;
using System.Text;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Returns a canned response and records the last request, so tests can run without a homeserver.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;
    private readonly string _mediaType;

    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode statusCode, string content, string mediaType = "application/json")
    {
        _statusCode = statusCode;
        _content = content;
        _mediaType = mediaType;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Request = request;
        if (request.Content is not null)
            RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, Encoding.UTF8, _mediaType)
        };
    }
}