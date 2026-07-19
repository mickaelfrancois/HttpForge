using System.Text;
using HttpForge.Data.Entities;

namespace HttpForge.Services;

public sealed record CurlHeader(string Key, string Value);

public sealed record CurlFormField(string Key, string Value);

// Result of parsing a cURL command. Content-Type is folded into BodyKind (and stripped
// from Headers) so the request carries exactly one canonical content type, mirroring how
// RequestExecutor emits bodies.
public sealed record CurlParseResult(
    HttpMethodKind Method,
    string Url,
    IReadOnlyList<CurlHeader> Headers,
    BodyKind BodyKind,
    string? Body,
    IReadOnlyList<CurlFormField> FormFields,
    bool IgnoreTlsErrors,
    IReadOnlyList<string> Warnings);

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

// Parses a pasted `curl` command into a request, and builds a `curl` command from a request.
// Pure and stateless — no DI dependencies, unit-testable via `new CurlService()`. Supports
// the flags that appear in real docs / DevTools "Copy as cURL" output; unknown flags are
// reported as warnings rather than failing the whole parse. Bash-style syntax only
// (backslash line continuations, single/double quotes); cmd.exe `^` continuations are not
// supported.
public sealed class CurlService
{
    public CurlParseResult Parse(string curl)
    {
        var warnings = new List<string>();
        var tokens = Tokenize(curl ?? string.Empty);

        string? url = null;
        string? method = null;
        var headers = new List<CurlHeader>();
        var dataParts = new List<string>();        // -d / --data / --data-raw / --data-binary
        var urlEncodedParts = new List<string>();  // --data-urlencode
        var forceGet = false;
        var insecure = false;
        string? contentType = null;

        var i = 0;
        if (tokens.Count > 0 && tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase)) i = 1;

        // Consumes and returns the token following a flag; warns (and returns null) if absent.
        string? TakeValue(string flag)
        {
            if (i + 1 < tokens.Count) return tokens[++i];
            warnings.Add($"Option « {flag} » sans valeur, ignorée.");
            return null;
        }

        void AddHeader(string raw)
        {
            var idx = raw.IndexOf(':');
            if (idx <= 0)
            {
                warnings.Add($"En-tête ignoré (format invalide) : {raw}");
                return;
            }
            var key = raw[..idx].Trim();
            var value = raw[(idx + 1)..].Trim();
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value;
            headers.Add(new CurlHeader(key, value));
        }

        for (; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t)
            {
                case "-X" or "--request":
                    method = TakeValue(t) ?? method;
                    break;
                case "-H" or "--header":
                    if (TakeValue(t) is { } h) AddHeader(h);
                    break;
                case "-A" or "--user-agent":
                    if (TakeValue(t) is { } ua) headers.Add(new CurlHeader("User-Agent", ua));
                    break;
                case "-e" or "--referer":
                    if (TakeValue(t) is { } re) headers.Add(new CurlHeader("Referer", re));
                    break;
                case "-b" or "--cookie":
                    if (TakeValue(t) is { } ck) headers.Add(new CurlHeader("Cookie", ck));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-ascii" or "--data-binary":
                    if (TakeValue(t) is { } d)
                    {
                        if (d.StartsWith('@')) warnings.Add($"Donnée depuis un fichier non supportée, ignorée : {d}");
                        else dataParts.Add(d);
                    }
                    break;
                case "--data-urlencode":
                    if (TakeValue(t) is { } due) urlEncodedParts.Add(due);
                    break;
                case "-u" or "--user":
                    if (TakeValue(t) is { } cred)
                        headers.Add(new CurlHeader("Authorization",
                            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(cred))));
                    break;
                case "--url":
                    url = TakeValue(t) ?? url;
                    break;
                case "-G" or "--get":
                    forceGet = true;
                    break;
                case "-k" or "--insecure":
                    insecure = true;
                    break;
                // No-argument flags with no effect on the request itself — ignored quietly.
                case "-s" or "--silent" or "-S" or "--show-error" or "-L" or "--location"
                    or "-v" or "--verbose" or "-i" or "--include" or "--compressed"
                    or "-#" or "--progress-bar" or "-f" or "--fail" or "-g" or "--globoff"
                    or "-O" or "--remote-name" or "-j" or "--junk-session-cookies":
                    break;
                // Argument-bearing flags we don't model — consume the value and warn.
                case "-o" or "--output" or "-m" or "--max-time" or "--connect-timeout"
                    or "--retry" or "--cacert" or "--cert" or "--key" or "-x" or "--proxy"
                    or "-w" or "--write-out" or "--resolve":
                    warnings.Add($"Option non supportée ignorée : {t} {TakeValue(t)}");
                    break;
                default:
                    if (t.StartsWith('-'))
                        warnings.Add($"Option cURL non reconnue ignorée : {t}");
                    else if (url is null)
                        url = t;
                    else
                        warnings.Add($"Argument ignoré : {t}");
                    break;
            }
        }

        if (url is null) warnings.Add("Aucune URL trouvée dans la commande cURL.");

        var hasBody = dataParts.Count > 0 || urlEncodedParts.Count > 0;

        HttpMethodKind httpMethod;
        if (forceGet)
        {
            httpMethod = HttpMethodKind.GET;
        }
        else if (method is not null)
        {
            if (!Enum.TryParse(method, ignoreCase: true, out httpMethod))
            {
                warnings.Add($"Méthode HTTP « {method} » non reconnue (GET utilisé).");
                httpMethod = hasBody ? HttpMethodKind.POST : HttpMethodKind.GET;
            }
        }
        else
        {
            // curl implies POST when data is present and no explicit method is given.
            httpMethod = hasBody ? HttpMethodKind.POST : HttpMethodKind.GET;
        }

        var bodyKind = BodyKind.None;
        string? body = null;
        var formFields = new List<CurlFormField>();

        var isForm = urlEncodedParts.Count > 0
            || (contentType?.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ?? false);

        if (isForm)
        {
            bodyKind = BodyKind.FormUrlEncoded;
            foreach (var p in urlEncodedParts) formFields.Add(SplitPair(p));
            foreach (var segment in dataParts.SelectMany(d => d.Split('&')))
                formFields.Add(SplitPair(segment));
        }
        else if (hasBody)
        {
            body = string.Join("&", dataParts);
            if (contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                bodyKind = BodyKind.Json;
            }
            else if (contentType is not null)
            {
                bodyKind = BodyKind.Raw;
                if (!contentType.Contains("text/", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Type de contenu « {contentType} » traité comme corps brut.");
            }
            else
            {
                var trimmed = body.TrimStart();
                bodyKind = trimmed.StartsWith('{') || trimmed.StartsWith('[')
                    ? BodyKind.Json
                    : BodyKind.Raw;
            }
        }

        // The content type is now represented by BodyKind; drop the header so the request
        // never carries a duplicate/conflicting Content-Type when re-sent.
        if (bodyKind != BodyKind.None)
            headers.RemoveAll(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));

        return new CurlParseResult(httpMethod, url ?? string.Empty, headers, bodyKind, body,
            formFields, insecure, warnings);
    }

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

        // Re-add the canonical Content-Type the BodyKind implies so a round-trip parse
        // restores the same kind (parse strips Content-Type into BodyKind).
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

    private static CurlFormField SplitPair(string segment)
    {
        var idx = segment.IndexOf('=');
        return idx < 0
            ? new CurlFormField(segment, string.Empty)
            : new CurlFormField(segment[..idx], segment[(idx + 1)..]);
    }

    // Wraps a value in single quotes, escaping embedded single quotes via the POSIX
    // '\'' idiom so the argument survives a shell round-trip unchanged.
    private static string Quote(string s) => "'" + (s ?? string.Empty).Replace("'", "'\\''") + "'";

    // Splits a command line into tokens the way a POSIX shell would: single quotes are
    // literal, double quotes allow \" \\ \$ \` escapes, an unquoted backslash escapes the
    // next char, and a backslash before a newline is a line continuation.
    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inToken = false;
        var i = 0;
        var n = input.Length;

        while (i < n)
        {
            var c = input[i];

            if (c == '\'')
            {
                inToken = true;
                i++;
                while (i < n && input[i] != '\'') sb.Append(input[i++]);
                if (i < n) i++; // closing quote
                continue;
            }

            if (c == '"')
            {
                inToken = true;
                i++;
                while (i < n && input[i] != '"')
                {
                    if (input[i] == '\\' && i + 1 < n)
                    {
                        var nx = input[i + 1];
                        if (nx is '"' or '\\' or '$' or '`') { sb.Append(nx); i += 2; continue; }
                        if (nx == '\n') { i += 2; continue; }
                    }
                    sb.Append(input[i++]);
                }
                if (i < n) i++; // closing quote
                continue;
            }

            if (c == '\\')
            {
                if (i + 1 < n)
                {
                    var nx = input[i + 1];
                    if (nx == '\n') { i += 2; continue; }
                    if (nx == '\r' && i + 2 < n && input[i + 2] == '\n') { i += 3; continue; }
                    sb.Append(nx);
                    inToken = true;
                    i += 2;
                    continue;
                }
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken) { tokens.Add(sb.ToString()); sb.Clear(); inToken = false; }
                i++;
                continue;
            }

            sb.Append(c);
            inToken = true;
            i++;
        }

        if (inToken) tokens.Add(sb.ToString());
        return tokens;
    }
}
