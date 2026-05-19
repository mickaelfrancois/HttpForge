using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using HttpForge.Data.Entities;

namespace HttpForge.Services;

public record ExecutionResult(
    int StatusCode,
    string ReasonPhrase,
    string Body,
    Dictionary<string, string> Headers,
    long ElapsedMs,
    long BodyBytes,
    string? Error);

public class RequestExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VariableResolver _resolver;

    public RequestExecutor(IHttpClientFactory httpClientFactory, VariableResolver resolver)
    {
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        HttpRequestItem request,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
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

            var client = _httpClientFactory.CreateClient("forge");
            client.Timeout = TimeSpan.FromMinutes(2);

            using var response = await client.SendAsync(msg, HttpCompletionOption.ResponseContentRead, ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in response.Headers)
                headers[h.Key] = string.Join(", ", h.Value);
            foreach (var h in response.Content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);

            sw.Stop();
            return new ExecutionResult(
                (int)response.StatusCode,
                response.ReasonPhrase ?? string.Empty,
                body,
                headers,
                sw.ElapsedMilliseconds,
                Encoding.UTF8.GetByteCount(body),
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExecutionResult(0, string.Empty, string.Empty, new(), sw.ElapsedMilliseconds, 0, ex.Message);
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
}
