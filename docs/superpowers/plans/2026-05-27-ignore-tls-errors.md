# Ignore TLS Certificate Errors (per-request) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user opt a single request out of TLS certificate validation so HttpForge (a server-side Blazor executor) can reach HTTPS services that use self-signed/untrusted certificates.

**Architecture:** A persisted boolean `IgnoreTlsErrors` on the request flows draft → save → entity. `RequestExecutor` reads it and, when set, installs a permissive `RemoteCertificateValidationCallback` on the TLS handshake. A new "Options" sub-tab in `Home.razor` exposes the toggle, with a tab dot when active. Off by default; existing behavior unchanged.

**Tech Stack:** .NET 10, Blazor Server, EF Core (SQLite + InMemory for tests), xUnit 2.9.3, raw `SslStream` test server.

**Spec:** `docs/superpowers/specs/2026-05-27-ignore-tls-errors-design.md`

**Branch:** `feat/ignore-tls-errors` (already checked out in this worktree).

---

## File Structure

- **Modify** `HttpForge/Data/Entities/HttpRequestItem.cs` — add `IgnoreTlsErrors` property.
- **Modify** `HttpForge/Models/RequestDraft.cs` — add property + `FromRequest` mapping.
- **Modify** `HttpForge/Data/SchemaUpgrader.cs` — `EnsureColumn` for the new column.
- **Modify** `HttpForge/Services/RequestSaveService.cs` — persist the field.
- **Modify** `HttpForge/Services/RequestExecutor.cs` — thread flag into the TLS handshake.
- **Modify** `HttpForge/Components/Pages/Home.razor` — Options sub-tab, handler, tab dot.
- **Modify** `HttpForge/wwwroot/app.css` — minimal styles for the Options tab.
- **Create** `HttpForge.Tests/Helpers/SelfSignedHttpsServer.cs` — test HTTPS server.
- **Modify** `HttpForge.Tests/Unit/RequestDraftTests.cs` — draft field test.
- **Modify** `HttpForge.Tests/Integration/RequestSaveServiceTests.cs` — persistence test.
- **Modify** `HttpForge.Tests/Services/RequestExecutorTests.cs` — TLS bypass tests.

All commands run from the worktree root:
`D:\Development\HttpForge\.claude\worktrees\fix-save-black-screen`

---

## Task 1: Add `IgnoreTlsErrors` to entity + draft

**Files:**
- Modify: `HttpForge/Data/Entities/HttpRequestItem.cs`
- Modify: `HttpForge/Models/RequestDraft.cs`
- Test: `HttpForge.Tests/Unit/RequestDraftTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HttpForge.Tests/Unit/RequestDraftTests.cs` (inside the class):

```csharp
    [Fact]
    public void IgnoreTlsErrors_DefaultsFalse()
    {
        var draft = MakeDraft();
        Assert.False(draft.IgnoreTlsErrors);
    }

    [Fact]
    public void FromRequest_CopiesIgnoreTlsErrors()
    {
        var request = new HttpRequestItem
        {
            Id = 1,
            Name = "Test",
            Method = HttpMethodKind.GET,
            Url = "https://example.com",
            BodyKind = BodyKind.None,
            IgnoreTlsErrors = true
        };

        var draft = RequestDraft.FromRequest(request, DateTime.UtcNow);

        Assert.True(draft.IgnoreTlsErrors);
    }
```

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~RequestDraftTests"`
Expected: FAIL — does not compile, `HttpRequestItem`/`RequestDraft` has no `IgnoreTlsErrors`.

- [ ] **Step 3: Add the property to the entity**

In `HttpForge/Data/Entities/HttpRequestItem.cs`, after the `PostScript` line:

```csharp
    public string? PostScript { get; set; }

    public bool IgnoreTlsErrors { get; set; }
```

- [ ] **Step 4: Add the property + mapping to the draft**

In `HttpForge/Models/RequestDraft.cs`, add the property after `PostScript`:

```csharp
    public string? PostScript { get; set; }
    public bool IgnoreTlsErrors { get; set; }
```

And in `FromRequest`, add the mapping after `PostScript = r.PostScript,`:

```csharp
        PostScript = r.PostScript,
        IgnoreTlsErrors = r.IgnoreTlsErrors,
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~RequestDraftTests"`
Expected: PASS (all RequestDraftTests green).

- [ ] **Step 6: Commit**

```bash
git add HttpForge/Data/Entities/HttpRequestItem.cs HttpForge/Models/RequestDraft.cs HttpForge.Tests/Unit/RequestDraftTests.cs
git commit -m "feat: add IgnoreTlsErrors to request entity and draft"
```

---

## Task 2: Persist `IgnoreTlsErrors` (save service + schema column)

**Files:**
- Modify: `HttpForge/Services/RequestSaveService.cs:48-55`
- Modify: `HttpForge/Data/SchemaUpgrader.cs:17`
- Test: `HttpForge.Tests/Integration/RequestSaveServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HttpForge.Tests/Integration/RequestSaveServiceTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task SaveAsync_PersistsIgnoreTlsErrors()
    {
        var svc = new RequestSaveService(_factory, _notifier, _userManager);
        var draft = MakeDraft(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));
        draft.IgnoreTlsErrors = true;

        var result = await svc.SaveAsync(draft, "user-1", "Alice", forceOverwrite: false);
        Assert.False(result.IsConflict);

        await using var db = await _factory.CreateDbContextAsync();
        var saved = await db.Requests.FirstAsync(r => r.Id == _requestId);
        Assert.True(saved.IgnoreTlsErrors);
    }
```

> Note: these tests use the EF **InMemory** provider with `EnsureCreated`, so the column comes from the EF model (Task 1), not `SchemaUpgrader`. The `SchemaUpgrader` change in Step 4 covers the production SQLite database.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~SaveAsync_PersistsIgnoreTlsErrors"`
Expected: FAIL — `saved.IgnoreTlsErrors` is `false` because `SaveAsync` does not copy the field yet.

- [ ] **Step 3: Persist the field in the save service**

In `HttpForge/Services/RequestSaveService.cs`, in `SaveAsync`, after `dbItem.PostScript = draft.PostScript;`:

```csharp
        dbItem.PostScript = draft.PostScript;
        dbItem.IgnoreTlsErrors = draft.IgnoreTlsErrors;
```

- [ ] **Step 4: Add the SQLite column in SchemaUpgrader**

In `HttpForge/Data/SchemaUpgrader.cs`, in `Apply`, alongside the other `Requests` columns (after the `PostScript` line at the top):

```csharp
        EnsureColumn(db, "Requests", "PostScript", "TEXT NULL");
        EnsureColumn(db, "Requests", "IgnoreTlsErrors", "INTEGER NOT NULL DEFAULT 0");
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~RequestSaveServiceTests"`
Expected: PASS (all RequestSaveServiceTests green).

- [ ] **Step 6: Commit**

```bash
git add HttpForge/Services/RequestSaveService.cs HttpForge/Data/SchemaUpgrader.cs HttpForge.Tests/Integration/RequestSaveServiceTests.cs
git commit -m "feat: persist IgnoreTlsErrors through save + schema upgrade"
```

---

## Task 3: Apply the TLS bypass in RequestExecutor

**Files:**
- Create: `HttpForge.Tests/Helpers/SelfSignedHttpsServer.cs`
- Modify: `HttpForge/Services/RequestExecutor.cs` (`ExecuteAsync`, `CreateHandler`, `ConnectAsync`)
- Test: `HttpForge.Tests/Services/RequestExecutorTests.cs`

- [ ] **Step 1: Create the self-signed HTTPS test server**

Create `HttpForge.Tests/Helpers/SelfSignedHttpsServer.cs`:

```csharp
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HttpForge.Tests.Helpers;

/// <summary>
/// A minimal HTTPS server backed by a self-signed certificate, for testing TLS
/// behavior. Listens on a loopback ephemeral port and answers every successful
/// handshake with "200 OK" / body "OK". The certificate is untrusted, so a client
/// using default validation rejects the handshake.
/// </summary>
public sealed class SelfSignedHttpsServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _cert;
    private readonly CancellationTokenSource _cts = new();

    public SelfSignedHttpsServer()
    {
        _cert = CreateSelfSignedCertificate();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string Url => $"https://127.0.0.1:{Port}/";

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            await using (var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
            {
                await ssl.AuthenticateAsServerAsync(_cert, clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);

                // Consume whatever the client sends; we don't parse the request.
                var buf = new byte[4096];
                _ = await ssl.ReadAsync(buf, ct);

                var body = "OK"u8.ToArray();
                var headers = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await ssl.WriteAsync(Encoding.ASCII.GetBytes(headers), ct);
                await ssl.WriteAsync(body, ct);
                await ssl.FlushAsync(ct);
            }
        }
        catch
        {
            // Client aborted the handshake (default validation rejected the
            // self-signed cert). Expected for the negative test.
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=HttpForge Test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());

        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Round-trip through PFX so the private key is usable by SslStream as a
        // server certificate on Windows (ephemeral keys are rejected there).
        var pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cert.Dispose();
        _cts.Dispose();
    }
}
```

- [ ] **Step 2: Write the failing tests**

Add to `HttpForge.Tests/Services/RequestExecutorTests.cs` (inside the class). The import `using HttpForge.Tests.Helpers;` is already present.

```csharp
    // ── TLS certificate validation ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SelfSignedHttps_WithoutBypass_ReturnsError()
    {
        using var server = new SelfSignedHttpsServer();
        var sut = new RequestExecutor(new VariableResolver()); // real handler, no fake
        var req = new HttpRequestItem { Url = server.Url, IgnoreTlsErrors = false };

        var result = await sut.ExecuteAsync(req, NoVars);

        Assert.Equal(0, result.StatusCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_SelfSignedHttps_WithBypass_Succeeds()
    {
        using var server = new SelfSignedHttpsServer();
        var sut = new RequestExecutor(new VariableResolver()); // real handler, no fake
        var req = new HttpRequestItem { Url = server.Url, IgnoreTlsErrors = true };

        var result = await sut.ExecuteAsync(req, NoVars);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("OK", result.Body);
        Assert.Null(result.Error);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~SelfSignedHttps"`
Expected: `WithBypass_Succeeds` FAILS — the executor ignores the flag, so the untrusted cert is still rejected (StatusCode 0, Error set). `WithoutBypass_ReturnsError` already passes.

- [ ] **Step 4: Thread the flag through `ExecuteAsync` → `CreateHandler`**

In `HttpForge/Services/RequestExecutor.cs`, change the `CreateHandler` call in `ExecuteAsync`:

```csharp
            var (handler, ownsHandler) = CreateHandler(probe, request.IgnoreTlsErrors);
```

Change the `CreateHandler` method signature and body:

```csharp
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
```

- [ ] **Step 5: Apply the bypass in `ConnectAsync`**

In `HttpForge/Services/RequestExecutor.cs`, change the `ConnectAsync` signature to accept the flag:

```csharp
    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext ctx, ConnectionProbe probe, bool ignoreTlsErrors, CancellationToken ct)
```

And in the `if (isHttps)` block, replace the inline `SslClientAuthenticationOptions` with a local that conditionally sets the callback:

```csharp
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
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test HttpForge.Tests --filter "FullyQualifiedName~RequestExecutorTests"`
Expected: PASS — both new tests green, all existing executor tests still green.

- [ ] **Step 7: Commit**

```bash
git add HttpForge/Services/RequestExecutor.cs HttpForge.Tests/Helpers/SelfSignedHttpsServer.cs HttpForge.Tests/Services/RequestExecutorTests.cs
git commit -m "feat: honor IgnoreTlsErrors in RequestExecutor TLS handshake"
```

---

## Task 4: Expose the toggle in the Options sub-tab (UI)

**Files:**
- Modify: `HttpForge/Components/Pages/Home.razor` (`_subTabs`, tab-body switch, `OnIgnoreTlsChanged`, `TabHasDot`)
- Modify: `HttpForge/wwwroot/app.css`

No automated test: `Home.razor` is `InteractiveServer` and is not mounted in the bUnit suite (it depends on auth, JS interop, and many scoped services). Verified by build + manual smoke test, consistent with the project's existing approach to UI.

- [ ] **Step 1: Add "Options" to the sub-tab list**

In `HttpForge/Components/Pages/Home.razor`, change the `_subTabs` field (around line 351):

```csharp
    private readonly string[] _subTabs = ["Params", "Headers", "Body", "Variables", "Scripts", "Options"];
```

- [ ] **Step 2: Add the Options case to the tab-body switch**

In `HttpForge/Components/Pages/Home.razor`, in the `@switch (tab.ActiveSubTab)` block, after the `case "Scripts":` ... `break;` and before the closing `}` (around line 204):

```razor
                case "Options":
                    <div class="options-tab">
                        <label class="tls-toggle">
                            <input type="checkbox"
                                   checked="@tab.Draft.IgnoreTlsErrors"
                                   disabled="@State.IsReadOnly"
                                   @onchange="OnIgnoreTlsChanged" />
                            <span>Ignorer les erreurs de certificat TLS</span>
                        </label>
                        <div class="options-hint">
                            ⚠ Désactive la validation du certificat (autorité, nom d'hôte, expiration)
                            pour cette requête. À n'utiliser que sur des services de confiance.
                        </div>
                    </div>
                    break;
```

- [ ] **Step 3: Add the change handler**

In `HttpForge/Components/Pages/Home.razor`, next to the other input handlers (after `OnUrlChanged`, around line 527):

```csharp
    private void OnIgnoreTlsChanged(ChangeEventArgs e)
    {
        if (Active is null || State.IsReadOnly) return;
        Active.Draft.IgnoreTlsErrors = e.Value is true;
        Active.Draft.MarkDirty();
    }
```

- [ ] **Step 4: Show the tab dot when active**

In `HttpForge/Components/Pages/Home.razor`, in the `TabHasDot` switch (around line 920), add the `Options` arm:

```csharp
    private bool TabHasDot(string subTab) => subTab switch
    {
        "Body"    => !string.IsNullOrWhiteSpace(Active?.Draft.BodyContent),
        "Scripts" => !string.IsNullOrWhiteSpace(Active?.Draft.PostScript),
        "Options" => Active?.Draft.IgnoreTlsErrors ?? false,
        _         => false
    };
```

- [ ] **Step 5: Add styles**

In `HttpForge/wwwroot/app.css`, append:

```css
/* Request "Options" sub-tab */
.options-tab { padding: 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
.tls-toggle { display: flex; align-items: center; gap: 0.5rem; cursor: pointer; font-size: 0.9rem; }
.tls-toggle input { width: 1rem; height: 1rem; cursor: pointer; accent-color: var(--accent-blue); }
.options-hint { font-size: 0.8rem; color: var(--text-muted); max-width: 52ch; line-height: 1.4; }
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build HttpForge`
Expected: 0 errors (pre-existing warnings only).

- [ ] **Step 7: Manual smoke test**

Run: `dotnet run --project HttpForge`, then in the browser:
1. Open a request; confirm an **Options** tab appears after Scripts.
2. Open Options, tick "Ignorer les erreurs de certificat TLS" → the **Save** button appears and a **dot** shows on the Options tab.
3. Save, reload the page, reopen the request → checkbox stays ticked, dot persists.
4. Point the request at an HTTPS service with a self-signed cert → it now executes instead of erroring.
5. Untick, save → request validates certs again (TLS error returns for the self-signed service).

- [ ] **Step 8: Commit**

```bash
git add HttpForge/Components/Pages/Home.razor HttpForge/wwwroot/app.css
git commit -m "feat: add Options sub-tab with ignore-TLS-errors toggle"
```

---

## Task 5: Final verification

- [ ] **Step 1: Run the full suite**

Run: `dotnet test HttpForge.Tests`
Expected: all tests pass (109 existing + 5 new = 114).

- [ ] **Step 2: Update CLAUDE.md docs (RequestExecutor + render notes)**

In `D:\Development\HttpForge\CLAUDE.md`, update the `RequestExecutor` row of the Services table to mention the optional TLS-validation bypass:

> `RequestExecutor` | Scoped | Builds and sends HTTP requests via a per-request `SocketsHttpHandler` whose `ConnectCallback` times DNS/TCP/TLS and, when the request's `IgnoreTlsErrors` flag is set, skips server-certificate validation; returns a `RequestTiming` waterfall on `ExecutionResult`

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: note IgnoreTlsErrors bypass in RequestExecutor"
```

---

## Notes for the implementer

- **Security:** the bypass is intentional, off by default, per-request, and surfaced via the tab dot. Do not widen it to a global setting.
- **`e.Value is true`:** Blazor binds a checkbox `@onchange` value to a boxed `bool`; the pattern match handles it without a cast.
- **Why the entity, not the draft, drives execution:** `SendAsync` saves a dirty draft before sending and reloads `_request` from the DB, so `_request.IgnoreTlsErrors` is current when passed to `RequestExecutor`. No extra wiring needed.
- **Test isolation:** `SelfSignedHttpsServer` binds to an ephemeral loopback port and is `IDisposable`; always wrap it in `using`.
