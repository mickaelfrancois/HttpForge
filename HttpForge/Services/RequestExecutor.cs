using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using HttpForge.Data.Entities;

namespace HttpForge.Services;

public record RequestTiming(
    long DnsMs,
    long ConnectMs,
    long TlsMs,
    long WaitingMs,
    long DownloadMs,
    long TotalMs,
    string? TlsProtocol,
    string? TlsCipher,
    string? NegotiatedAlpn,
    string HttpVersion);

public record ExecutionResult(
    int StatusCode,
    string ReasonPhrase,
    string Body,
    Dictionary<string, string> Headers,
    long ElapsedMs,
    long BodyBytes,
    string? Error,
    RequestTiming? Timing = null);

public class RequestExecutor
{
    private readonly VariableResolver _resolver;

    // Test seam: when provided, this handler is used instead of the production
    // SocketsHttpHandler (so unit tests can inject a fake). Timing is skipped.
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public RequestExecutor(VariableResolver resolver, Func<HttpMessageHandler>? handlerFactory = null)
    {
        _resolver = resolver;
        _handlerFactory = handlerFactory;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        HttpRequestItem request,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var probe = new ConnectionProbe();
        try
        {
            var url = BuildUrl(request, variables);
            var method = new HttpMethod(request.Method.ToString());

            using var msg = new HttpRequestMessage(method, url);

            HttpContent? content = BuildContent(request, variables);
            if (content is not null) msg.Content = content;

            foreach (var h in request.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
            {
                var key = _resolver.Resolve(h.Key, variables);
                var value = _resolver.Resolve(h.Value, variables);
                if (!msg.Headers.TryAddWithoutValidation(key, value))
                {
                    msg.Content?.Headers.TryAddWithoutValidation(key, value);
                }
            }

            var (handler, ownsHandler) = CreateHandler(probe, request.IgnoreTlsErrors);
            using var client = new HttpClient(handler, disposeHandler: ownsHandler);
            client.Timeout = TimeSpan.FromMinutes(2);

            using var response = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            var headersAtMs = sw.ElapsedMilliseconds;

            var body = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();
            var totalMs = sw.ElapsedMilliseconds;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in response.Headers)
                headers[h.Key] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);

            RequestTiming? timing = probe.Captured
                ? new RequestTiming(
                    probe.DnsMs,
                    probe.ConnectMs,
                    probe.TlsMs,
                    Math.Max(0, headersAtMs - (probe.DnsMs + probe.ConnectMs + probe.TlsMs)),
                    Math.Max(0, totalMs - headersAtMs),
                    totalMs,
                    probe.TlsProtocol,
                    probe.TlsCipher,
                    probe.Alpn,
                    $"{response.Version.Major}.{response.Version.Minor}")
                : null;

            return new ExecutionResult(
                (int)response.StatusCode,
                response.ReasonPhrase ?? string.Empty,
                body,
                headers,
                totalMs,
                Encoding.UTF8.GetByteCount(body),
                null,
                timing);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecutionResult(0, string.Empty, string.Empty, new(), sw.ElapsedMilliseconds, 0, ex.Message);
        }
    }

    private (HttpMessageHandler handler, bool owns) CreateHandler(ConnectionProbe probe, bool ignoreTlsErrors)
    {
        if (_handlerFactory is not null)
            return (_handlerFactory(), false);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false,
            ConnectCallback = (ctx, ct) => ConnectAsync(ctx, probe, ignoreTlsErrors, ct)
        };
        return (handler, true);
    }

    // Establishes the connection manually so each phase can be timed.
    // For HTTPS we perform the TLS handshake here and return the authenticated
    // SslStream; SocketsHttpHandler (since .NET 7) detects this and does not
    // layer a second TLS session on top.
    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext ctx, ConnectionProbe probe, bool ignoreTlsErrors, CancellationToken ct)
    {
        var ep = ctx.DnsEndPoint;
        var isHttps = string.Equals(
            ctx.InitialRequestMessage.RequestUri?.Scheme, "https", StringComparison.OrdinalIgnoreCase);

        var swDns = Stopwatch.StartNew();
        var addresses = await Dns.GetHostAddressesAsync(ep.Host, ct);
        swDns.Stop();

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            var swConnect = Stopwatch.StartNew();
            await socket.ConnectAsync(addresses, ep.Port, ct);
            swConnect.Stop();

            Stream stream = new NetworkStream(socket, ownsSocket: true);
            long tlsMs = 0;
            string? proto = null, cipher = null, alpn = null;

            if (isHttps)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = ep.Host,
                    ApplicationProtocols = [SslApplicationProtocol.Http11]
                };
                if (ignoreTlsErrors)
                    sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

                var swTls = Stopwatch.StartNew();
                await ssl.AuthenticateAsClientAsync(sslOptions, ct);
                swTls.Stop();

                tlsMs = swTls.ElapsedMilliseconds;
                proto = ssl.SslProtocol.ToString();
                try { cipher = ssl.NegotiatedCipherSuite.ToString(); } catch { /* not supported on all OSes */ }
                var negotiated = ssl.NegotiatedApplicationProtocol.ToString();
                alpn = string.IsNullOrEmpty(negotiated) ? null : negotiated;
                stream = ssl;
            }

            probe.Set(swDns.ElapsedMilliseconds, swConnect.ElapsedMilliseconds, tlsMs, proto, cipher, alpn);
            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private string BuildUrl(HttpRequestItem r, IReadOnlyDictionary<string, string> variables)
    {
        var baseUrl = _resolver.Resolve(r.Url, variables);

        var enabledParams = r.QueryParams
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => (Key: _resolver.Resolve(p.Key, variables), Value: _resolver.Resolve(p.Value, variables)))
            .ToList();

        if (enabledParams.Count == 0) return baseUrl;

        var qs = string.Join("&", enabledParams.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return baseUrl.Contains('?') ? $"{baseUrl}&{qs}" : $"{baseUrl}?{qs}";
    }

    private HttpContent? BuildContent(HttpRequestItem r, IReadOnlyDictionary<string, string> variables)
    {
        switch (r.BodyKind)
        {
            case BodyKind.None:
                return null;
            case BodyKind.Json:
            {
                var json = _resolver.Resolve(r.BodyContent, variables);
                var c = new StringContent(json ?? string.Empty, Encoding.UTF8);
                c.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return c;
            }
            case BodyKind.Raw:
            {
                var raw = _resolver.Resolve(r.BodyContent, variables);
                return new StringContent(raw ?? string.Empty, Encoding.UTF8, "text/plain");
            }
            case BodyKind.FormUrlEncoded:
            {
                var pairs = r.FormFields
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
                    .Select(f => new KeyValuePair<string, string>(
                        _resolver.Resolve(f.Key, variables),
                        _resolver.Resolve(f.Value, variables)));
                return new FormUrlEncodedContent(pairs);
            }
            default:
                return null;
        }
    }

    private sealed class ConnectionProbe
    {
        public bool Captured { get; private set; }
        public long DnsMs { get; private set; }
        public long ConnectMs { get; private set; }
        public long TlsMs { get; private set; }
        public string? TlsProtocol { get; private set; }
        public string? TlsCipher { get; private set; }
        public string? Alpn { get; private set; }

        public void Set(long dns, long connect, long tls, string? proto, string? cipher, string? alpn)
        {
            DnsMs = dns;
            ConnectMs = connect;
            TlsMs = tls;
            TlsProtocol = proto;
            TlsCipher = cipher;
            Alpn = alpn;
            Captured = true;
        }
    }
}
