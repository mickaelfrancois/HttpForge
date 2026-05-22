# User Management Design

**Date:** 2026-05-22
**Status:** Approved

## Overview

Add multi-user, multi-team support to HttpForge. Users belong to teams; collections are owned by a team. Access is controlled by per-team roles. Authentication uses ASP.NET Core Identity with SSO providers and an invitation-based registration flow.

---

## Data Model

`AppDbContext` extends `IdentityDbContext<AppUser>` instead of `DbContext`. `EnsureCreated()` at startup creates all Identity tables alongside application tables — no separate migration system.

### New custom entities (managed via SchemaUpgrader)

| Table | Key columns |
|---|---|
| `Teams` | `Id`, `Name`, `CreatedAt` |
| `TeamMembers` | `Id`, `TeamId`, `UserId`, `Role` |
| `InvitationTokens` | `Id`, `TeamId` (nullable), `Email`, `Token`, `Role`, `ExpiresAt`, `UsedAt` |

**`Role` values in `TeamMembers`:** `TeamAdmin`, `Contributor`, `Guest`

**`AppUser`** extends `IdentityUser` — no additional fields required.

**System role in Identity:** `SuperAdmin` (a standard Identity role, distinct from team-level roles).

### Changes to existing tables

- `Collections` gains `TeamId INTEGER NULL` — NULL means orphaned (invisible to normal users, accessible only to SuperAdmin).
- Existing data is not migrated (clean break; existing collections are effectively orphaned).

---

## Authentication

### Invitation flow (required for all users)

1. A TeamAdmin or SuperAdmin creates an invitation: email + role → HMAC-signed token stored in `InvitationTokens`, valid 72 hours.
2. Invitee receives a link: `https://app/invite/{token}`
3. `/invite/{token}` validates the token (not expired, not used) and offers:
   - **Password:** enter a password → account created and logged in
   - **SSO:** login via Google / GitHub / Microsoft — the SSO email must match the invitation email; if it matches, account is created/linked and logged in
4. Token marked as used; `TeamMember` record created with the invitation role.

### SSO without invitation

Rejected. Even if the OAuth provider returns a valid email, if no pending invitation exists for that email, the login is refused with an explicit message.

### SuperAdmin bootstrap

- Environment variable `HTTPFORGE_SUPERADMIN_EMAIL` is set at deployment.
- At first startup, if no user with the `SuperAdmin` Identity role exists, a special invitation token is auto-generated for that email with the `SuperAdmin` role.
- The SuperAdmin authenticates via SSO or password exactly like any other user — no special setup page.

### Sessions

Standard ASP.NET Core Identity cookie authentication. Blazor Server consumes `AuthenticationStateProvider` for reactive auth state.

---

## Authorization

### Permission matrix

| Action | SuperAdmin | TeamAdmin | Contributor | Guest |
|---|---|---|---|---|
| Create / delete teams | ✅ | ❌ | ❌ | ❌ |
| Invite members | ✅ | ✅ (own team) | ❌ | ❌ |
| Assign orphaned collection to a team | ✅ | ✅ (own team only, orphaned collections only) | ❌ | ❌ |
| Create / edit / delete collection | ✅ | ✅ | ✅ | ❌ |
| View requests, send requests | ✅ | ✅ | ✅ | ✅ |
| Edit fields (not persisted) | — | — | — | ✅ |

### Implementation

- A scoped `PermissionService` resolves the current user's role for a given collection by joining `TeamMembers` on the collection's `TeamId`. Injected into Blazor components and services.
- Blazor components use `<AuthorizeView>` to hide edit/delete controls for Guests.
- DB mutation calls in `Home.razor` and services check `PermissionService` before writing. Guests never invoke write paths.
- `AppState` gains an `IsReadOnly` bool computed dynamically from `SelectedCollectionId` and the current user's role in that collection's team — not set once at login. A user may be Contributor in one team and Guest in another. All state mutations are gated by this flag.

### Guest sandbox mode

- A persistent banner at the top of the UI: *"Vous êtes en mode lecture — vos modifications ne sont pas enregistrées"*
- Fields remain editable (useful for composing and sending requests), but `SaveAsync` and state mutation calls are intercepted and silently skipped when `IsReadOnly` is true.
- State changes are held in component memory only; a page reload resets all edits.

---

## User Interface

### New pages

| Route | Access | Description |
|---|---|---|
| `/login` | Public | SSO buttons + email/password form |
| `/invite/{token}` | Public | Token validation, password setup or SSO link |
| `/admin` | SuperAdmin | Manage teams, assign team admins |
| `/teams/{teamId}` | SuperAdmin, TeamAdmin | Manage members, pending invitations, assigned collections |

### Changes to existing components

- **`MainLayout.razor`** — header with current user name, active team, and logout link
- **`NavMenu.razor`** — collections filtered by `TeamId` matching the user's teams; SuperAdmin sees all collections
- **`CollectionNode.razor`** — edit/rename/delete actions hidden for Guests via `<AuthorizeView>`
- **`Home.razor`** — Guest banner injected at the top when `AppState.IsReadOnly` is true; mutation calls gated
- **`Routes.razor`** — unauthenticated users redirected to `/login`; `/admin` and `/teams/{teamId}` protected by policy

---

## Technical Choices

- **ASP.NET Core Identity** with SQLite (same `httpforge.db` file) — no external user store
- **External OAuth providers:** Google, GitHub, Microsoft via standard `AddGoogle()` / `AddGitHub()` / `AddMicrosoftAccount()` middleware
- **Invitation tokens:** HMAC-SHA256 over `{tokenId}:{email}:{expiresAt}` using a server secret from config/env
- **Provider credentials** (Client ID / Secret) configured via environment variables: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, etc.
- **SchemaUpgrader** extended to create `Teams`, `TeamMembers`, `InvitationTokens` tables and add `Collections.TeamId`
