using System.Net;

namespace HttpForge.Tests.Helpers;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseBody = string.Empty;

    public void SetResponse(HttpStatusCode statusCode, string body = "")
    {
        _statusCode = statusCode;
        _responseBody = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody)
        };
        return Task.FromResult(response);
    }
}
