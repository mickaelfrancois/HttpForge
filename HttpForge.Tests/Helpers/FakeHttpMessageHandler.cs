using System.Net;
using System.Net.Http.Headers;

namespace HttpForge.Tests.Helpers;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    /// <summary>
    /// A snapshot of the last request sent through this handler.
    /// Content is buffered into a new <see cref="ByteArrayContent"/> so it remains
    /// readable after <see cref="RequestExecutor"/> disposes the original
    /// <see cref="HttpRequestMessage"/> via its <c>using</c> block.
    /// </summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = string.Empty;

    public void SetResponse(HttpStatusCode statusCode, string body = "")
    {
        _statusCode = statusCode;
        _responseBody = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Build a snapshot HttpRequestMessage that won't be disposed by the caller's
        // `using` block. We copy URI, method, request headers, and buffer the content.
        var snapshot = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            snapshot.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var buffered = new ByteArrayContent(bytes);
            // Copy all content headers (Content-Type, Content-Length, etc.)
            foreach (var header in request.Content.Headers)
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            snapshot.Content = buffered;
        }

        LastRequest = snapshot;

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody)
        };
    }
}
