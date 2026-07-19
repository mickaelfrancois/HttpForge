using System.Text;
using HttpForge.Data.Entities;

namespace HttpForge.Services;

public sealed record CurlHeader(string Key, string Value);

public sealed record CurlFormField(string Key, string Value);

// Input to Build. The caller passes already-resolved values (URL with query string,
// enabled+merged headers); VariablePreview keeps secret variables masked as {{name}} so
// they never leak into an exported command.
public sealed record CurlExportRequest(
    HttpMethodKind Method,
    string Url,
    IReadOnlyList<CurlHeader> Headers,
    BodyKind BodyKind,
    string? Body,
    IReadOnlyList<CurlFormField> FormFields);

// Builds a `curl` command from a request. Pure and stateless — no DI dependencies,
// unit-testable via `new CurlService()`.
public sealed class CurlService
{
    public string Build(CurlExportRequest request)
    {
        var sb = new StringBuilder("curl");

        // curl defaults to GET; emit -X only when the method differs, keeping output clean.
        if (request.Method != HttpMethodKind.GET)
            sb.Append(" -X ").Append(request.Method.ToString());

        sb.Append(' ').Append(Quote(request.Url));

        var headers = request.Headers
            .Where(h => !string.IsNullOrWhiteSpace(h.Key))
            .ToList();

        // Add the canonical Content-Type the BodyKind implies (unless the request already
        // carries one), so the emitted command sends the same content type it would on Send.
        void EnsureHeader(string key, string value)
        {
            if (!headers.Any(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                headers.Add(new CurlHeader(key, value));
        }

        if (request.BodyKind == BodyKind.Json)
            EnsureHeader("Content-Type", "application/json");
        else if (request.BodyKind == BodyKind.Raw && !string.IsNullOrEmpty(request.Body))
            EnsureHeader("Content-Type", "text/plain");

        foreach (var h in headers)
            sb.Append(" \\\n  -H ").Append(Quote($"{h.Key}: {h.Value}"));

        switch (request.BodyKind)
        {
            case BodyKind.Json or BodyKind.Raw:
                if (!string.IsNullOrEmpty(request.Body))
                    sb.Append(" \\\n  --data-raw ").Append(Quote(request.Body));
                break;
            case BodyKind.FormUrlEncoded:
                foreach (var f in request.FormFields.Where(f => !string.IsNullOrWhiteSpace(f.Key)))
                    sb.Append(" \\\n  --data-urlencode ").Append(Quote($"{f.Key}={f.Value}"));
                break;
        }

        return sb.ToString();
    }

    // Wraps a value in single quotes, escaping embedded single quotes via the POSIX
    // '\'' idiom so the argument survives a shell round-trip unchanged.
    private static string Quote(string s) => "'" + (s ?? string.Empty).Replace("'", "'\\''") + "'";
}
