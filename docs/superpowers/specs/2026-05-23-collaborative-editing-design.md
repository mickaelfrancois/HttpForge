# Collaborative Editing Design

**Date:** 2026-05-23
**Status:** Approved

## Overview

Enable multiple developers to work on the same HttpForge instance without silently overwriting each other's changes. The solution introduces three mechanisms:

1. **Local draft model** — edits are held in memory until an explicit Save
2. **Optimistic concurrency** — detect conflicts at save time using `UpdatedAt`
3. **Real-time notifications** — notify users when a request they have open is saved by someone else
4. **Per-user variable values** — variable keys are shared (team schema), values are personal

---

## Data Model

### `HttpRequestItem` — new column

| Column | Type | Notes |
|---|---|---|
| `UpdatedByUserId` | `TEXT NULL` | FK → `AppUser.Id`. Set on every save. Used in conflict messages. |

`UpdatedAt` already exists. Both are updated atomically on every successful save.

Added via `SchemaUpgrader.EnsureColumn`.

### New table: `UserVariableValues`

Stores personal variable values per user. Variable keys (names) remain in existing shared tables.

| Column | Type | Notes |
|---|---|---|
| `Id` | `INTEGER PK` | |
| `UserId` | `TEXT NOT NULL` | FK → `AppUser.Id` |
| `ScopeType` | `TEXT NOT NULL` | `global_env`, `collection_varset`, or `request` |
| `ScopeId` | `INTEGER NOT NULL` | FK → `AppEnvironment.Id`, `CollectionVariableSet.Id`, or `HttpRequestItem.Id` |
| `VariableKey` | `TEXT NOT NULL` | Matches the key name in the shared table |
| `Value` | `TEXT NOT NULL` | |
| `IsSecret` | `INTEGER NOT NULL` | Boolean |

Unique constraint: `(UserId, ScopeType, ScopeId, VariableKey)`.

Added via `SchemaUpgrader.EnsureTable`.

### New singleton service: `RequestChangeNotifier`

```csharp
public class RequestChangeNotifier
{
    // requestId, savedByUserId, savedByUserName
    public event Func<int, string, string, Task>? RequestSaved;

    public async Task NotifyAsync(int requestId, string savedByUserId, string savedByUserName)
    {
        if (RequestSaved is not null)
            await RequestSaved.Invoke(requestId, savedByUserId, savedByUserName);
    }
}
```

Registered as `Singleton` in `Program.cs`.

---

## Draft System

### `RequestDraft` class

```csharp
public class RequestDraft
{
    public int RequestId { get; init; }
    public DateTime LoadedAt { get; init; }
    public string Name { get; set; } = "";
    public HttpMethodKind Method { get; set; }
    public string Url { get; set; } = "";
    public BodyKind BodyKind { get; set; }
    public string? BodyContent { get; set; }
    public string? PostScript { get; set; }
    public List<HeaderItem> Headers { get; set; } = [];
    public List<QueryParamItem> QueryParams { get; set; } = [];
    public List<FormFieldItem> FormFields { get; set; } = [];
    public List<RequestVariable> Variables { get; set; } = [];
    public bool IsDirty { get; set; }
}
```

### Lifecycle

1. **Open request** — `Home.razor` creates a `RequestDraft` from the loaded `HttpRequestItem`, with `LoadedAt = DateTime.UtcNow`. No DB write.
2. **Edit any field** — mutates `_draft.X`, sets `_draft.IsDirty = true`. No DB write.
3. **Click Save** — triggers conflict check then DB write (see below).
4. **Navigate away with unsaved changes** — `Home.razor` watches `AppState.OnChange`. When `SelectedRequestId` changes and `_draft.IsDirty`, the component intercepts before loading the new draft and shows a confirmation modal: "Modifications non sauvegardées — Quitter sans sauvegarder ou Sauvegarder d'abord ?"

The draft lives entirely in component state — not in `AppState`. `AppState.BuildVariables()` receives a new `userValues` parameter (a list of `UserVariableValues` for the current user) but no draft state is stored in `AppState`.

---

## Conflict Detection

On Save:

```
1. Load fresh HttpRequestItem from DB (new DbContext)
2. If dbItem.UpdatedAt > _draft.LoadedAt:
     → Show conflict modal (see below)
   Else:
     → Write draft to DB
     → Set UpdatedAt = DateTime.UtcNow, UpdatedByUserId = currentUserId
     → Reset IsDirty = false
     → Fire RequestChangeNotifier.NotifyAsync(requestId, currentUserName)
```

### Conflict modal

> **"Cette requête a été modifiée par [Prénom] à [heure]"**
>
> [Écraser leurs modifications]   [Annuler]

- **Écraser** — forces the save regardless, updating `UpdatedAt` and `UpdatedByUserId`. Then fires notification.
- **Annuler** — closes the modal, draft is preserved, user can keep editing or manually reload.

No automatic merge. The user decides.

---

## Real-Time Notifications

Uses an in-process singleton event bus — no external WebSocket hub required, since Blazor Server runs entirely server-side.

### Component subscription (Home.razor)

```csharp
// OnInitializedAsync:
_notifier.RequestSaved += OnRequestSavedByOther;

// Handler — userId comparison prevents false negatives when two users share a display name:
private async Task OnRequestSavedByOther(int requestId, string savedByUserId, string savedByUserName)
{
    if (requestId == _draft?.RequestId && savedByUserId != _currentUserId)
        await InvokeAsync(() =>
        {
            _reloadToast = $"{savedByUserName} vient de sauvegarder cette requête.";
            StateHasChanged();
        });
}

// Dispose:
_notifier.RequestSaved -= OnRequestSavedByOther;
```

### Toast UI

A dismissible banner above the request editor:

> **"Alice vient de sauvegarder cette requête."**  [Recharger]  [Ignorer]

- **Recharger** — discards local draft, reloads from DB, resets `LoadedAt`
- **Ignorer** — dismisses the toast, draft preserved

The toast does not appear if the save was triggered by the current user (same-user multi-tab case).

---

## Per-User Variable Values

### Separation of concerns

- **Keys (names)** — remain in existing shared tables (`EnvironmentVariable`, `CollectionVariableEntry`, `RequestVariable`). Edited as part of the request draft; shared across the team.
- **Values** — stored in `UserVariableValues`, loaded per-user. Saved immediately on change (no draft — values are always personal).

### Variable resolution

`AppState.BuildVariables()` gains a `userValues` parameter. After the existing merge (Global → Collection → Request keys), a final pass overlays the current user's values from `UserVariableValues`:

```
Shared keys merge (existing logic)
        ↓
Overlay: for each entry, if UserVariableValues has a row for (currentUserId, scope, key) → replace Value
```

If no personal value exists for a key, the shared value is used as the default (displayed in grey in the UI as a placeholder).

### Variable editor UI

- **Value column** — binds to the user's personal value (from `UserVariableValues`). Saves immediately on blur via upsert.
- **"Personnel" badge** — shown on rows that have a personal value overriding the shared default.
- **Shared default display** — rows without a personal value show the shared value as greyed-out placeholder text.

---

## Testing

### Unit tests (xUnit)

| Test | What it verifies |
|---|---|
| `RequestDraft_IsDirty_SetOnFirstEdit` | `IsDirty` starts false, becomes true after any field mutation |
| `BuildVariables_PersonalValueOverridesShared` | User's value in `UserVariableValues` replaces the shared key value |
| `BuildVariables_NoPersonalValue_UsesSharedDefault` | Shared value used when no personal row exists |
| `ConflictDetection_UpdatedAtAfterLoadedAt_ReturnsConflict` | Given `UpdatedAt > LoadedAt`, conflict is detected |
| `ConflictDetection_UpdatedAtBeforeLoadedAt_NoConflict` | Given `UpdatedAt <= LoadedAt`, no conflict |

### Integration tests (SQLite in-memory via EF Core)

| Test | What it verifies |
|---|---|
| `SaveRequest_NoConflict_WritesToDb` | DB row updated, `UpdatedAt` and `UpdatedByUserId` set |
| `SaveRequest_Conflict_DbNotModified` | DB unchanged when conflict is detected and save is aborted |
| `UserVariableValues_Upsert_InsertsOnFirst` | First personal value write creates a new row |
| `UserVariableValues_Upsert_UpdatesOnSecond` | Second write updates the existing row |
| `UserVariableValues_IsolatedPerUser` | User A's values do not appear in User B's resolved variables |

### Component tests (bUnit)

| Test | What it verifies |
|---|---|
| `SaveButton_DisabledWhenNotDirty` | Button is disabled when `IsDirty = false` |
| `SaveButton_EnabledAfterEdit` | Button enabled after any field change |
| `Toast_AppearsWhenOtherUserSaves` | Firing `RequestChangeNotifier` with a different user shows the toast |
| `Toast_HiddenWhenSameUserSaves` | Toast does not appear when current user triggers the notification |
| `UnsavedChangesModal_AppearsOnNavigateAway` | Navigating to another request with dirty draft shows the confirmation modal |

---

## Implementation Notes

- `SchemaUpgrader` handles both `UpdatedByUserId` column and `UserVariableValues` table — no EF migration needed.
- `RequestChangeNotifier` is registered as Singleton; all other services remain Scoped.
- The draft is component-local state — `AppState` stores no draft. `AppState.BuildVariables()` gains a `userValues` parameter but holds no mutable draft data.
- `AppState.IsReadOnly` (Guest mode) still gates Save: Guests cannot call the save path.
- `RequestDraft` is a plain class, not a record, to allow in-place mutation from bound form fields.
