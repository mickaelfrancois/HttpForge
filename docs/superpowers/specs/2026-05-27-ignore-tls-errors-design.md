# Ignore TLS certificate errors (per-request)

**Date:** 2026-05-27
**Status:** Approved design

## Problem

HttpForge runs as a Blazor Server app: requests are executed server-side by
`RequestExecutor`, not in the user's browser. When users target an HTTPS service
with a self-signed or otherwise untrusted certificate (typically a local/dev
service reached by IP), `ConnectAsync` performs `AuthenticateAsClientAsync` with
default certificate validation. The handshake fails (untrusted authority, name
mismatch, or expiry) and the request errors out, with no way to proceed.

Mainstream HTTP clients (Postman, Insomnia) solve this with a "disable SSL
verification" switch. HttpForge has no equivalent.

## Goal

Let a user opt a **single request** out of TLS certificate validation, so the
request can reach services with untrusted certificates. Off by default; the
current behavior is unchanged for every existing request.

## Non-goals

- No global / app-wide "disable verification" setting. The toggle is per-request.
- No per-collection or per-environment inheritance.
- No custom CA / client-certificate management. Out of scope.

## Design

### Data (persisted, shared like every other request property)

- `HttpRequestItem.IgnoreTlsErrors` — `bool`, default `false`.
- `SchemaUpgrader.Apply` adds the column:
  `EnsureColumn(db, "Requests", "IgnoreTlsErrors", "INTEGER NOT NULL DEFAULT 0")`.
- `RequestDraft` gains an `IgnoreTlsErrors` property, mapped in
  `RequestDraft.FromRequest`.
- `RequestSaveService.SaveAsync` writes `dbItem.IgnoreTlsErrors = draft.IgnoreTlsErrors`.

The value lives in the shared SQLite DB, so it syncs to the team exactly like
the URL, headers, and body. This is intentional: "this target uses an untrusted
cert" is a property of the request, not of one user's session.

### Execution (`RequestExecutor`)

`ExecuteAsync` already receives the `HttpRequestItem`. It reads
`request.IgnoreTlsErrors` and threads the flag through `CreateHandler` into
`ConnectAsync`. When the flag is set **and** the scheme is HTTPS, the TLS
handshake uses:

```csharp
RemoteCertificateValidationCallback = (_, _, _, _) => true
```

This accepts any server certificate — untrusted authority, hostname mismatch,
and expiry all pass. When the flag is unset, no callback is supplied and
validation behaves exactly as today.

The existing `_handlerFactory` test seam bypasses the production
`SocketsHttpHandler` (and therefore `ConnectAsync`), so unit tests that inject a
fake handler do not exercise this path; the bypass is covered by an integration
test against a real TLS server (see Testing).

### UI (`Home.razor`)

- Add `"Options"` to `_subTabs`, after `"Scripts"`.
- The `"Options"` tab body renders a single checkbox bound to
  `tab.Draft.IgnoreTlsErrors`. Toggling it sets the draft value, calls
  `MarkDirty()`, and triggers a re-render — so the standard **Save** button
  appears and the value persists through the normal save flow.
- Label: "Ignorer les erreurs de certificat TLS", with a short warning that
  this disables certificate validation (authority, hostname, expiry) for this
  request and should only be used with trusted services.
- `TabHasDot("Options")` returns `true` when `tab.Draft.IgnoreTlsErrors` is set,
  showing the existing tab dot so an enabled bypass is visible without opening
  the tab.

The checkbox is rendered but `disabled` in read-only (guest) mode, consistent
with the other editors.

## Testing

1. **Persistence round-trip** (unit): a `RequestDraft` saved with
   `IgnoreTlsErrors = true` and reloaded from the DB retains the value;
   default is `false`.
2. **TLS bypass** (integration): stand up a minimal HTTPS endpoint with a
   self-signed certificate.
   - With `IgnoreTlsErrors = false`, `ExecuteAsync` returns an error result
     (TLS validation failure).
   - With `IgnoreTlsErrors = true`, `ExecuteAsync` reaches the endpoint and
     returns its response. Uses the real `SocketsHttpHandler` (no
     `_handlerFactory`).

## Security considerations

This feature deliberately weakens transport security. Mitigations:

- **Off by default**; every existing request is unaffected.
- **Per-request scope** — no blast radius beyond the one request.
- **Visible** — the tab dot signals an active bypass; the tab body carries a
  warning.

This trade-off matches the tool's purpose (a developer HTTP client) and the
behavior of comparable tools.
