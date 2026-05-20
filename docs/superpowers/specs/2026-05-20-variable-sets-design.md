# Variable Sets Design

**Date:** 2026-05-20  
**Feature:** Base + named sub-sets at global and collection levels

## Problem

The current implementation treats collection variables as a flat list and global variables as a single named "environment". With ~50 collections, users need per-collection variable sets (e.g., for different base URLs per environment), and the ability to have a persistent "base" that shared variables don't need to be duplicated across sets.

## Solution: Base + Sub-sets at Each Level

Each variable scope level (global and collection) gets:
- A **base set** — always applied, holds variables common to all configurations
- **Named sub-sets** — user-defined names, override individual base variables when active

The request level remains a flat list (no sub-sets).

## Data Model

### Global level

`AppEnvironment` gains an `IsBase bool` column (default false):
- Exactly one `AppEnvironment` with `IsBase=true` = the **global base** (auto-created if absent, named "Base")
- All other `AppEnvironment` entries = **global sub-sets** (freely named by user)

`AppSettings` table (single row, Id=1):
- `ActiveGlobalSubsetId int?` — persists which global sub-set is active (null = base only)

`EnvironmentVariable` — unchanged.

### Collection level

`CollectionVariable` (flat list) is replaced by two new entities:

**`CollectionVariableSet`** (Id, CollectionId, Name, IsBase bool)
- One entry with `IsBase=true` per collection = the **collection base** (auto-created)
- Other entries = **collection sub-sets** (freely named)

**`CollectionVariableEntry`** (Id, CollectionVariableSetId, Key, Value, IsSecret)

`Collection` gains `ActiveCollectionVariableSetId int?` — persists the active sub-set per collection (null = base only).

### Migration

`SchemaUpgrader` handles:
1. Add `IsBase` column to `AppEnvironments` (default 0)
2. Auto-insert one `AppEnvironment` with `IsBase=1, Name='Base'` if none exists
3. Create `AppSettings` table, insert default row (Id=1, ActiveGlobalSubsetId=NULL)
4. Create `CollectionVariableSets` table
5. Create `CollectionVariableEntries` table
6. Add `ActiveCollectionVariableSetId` column to `Collections`
7. Data migration: for each distinct `CollectionId` in `CollectionVariables`, create a `CollectionVariableSet` (IsBase=1, Name='Base') if not present, then copy its `CollectionVariable` rows into `CollectionVariableEntry`

`CollectionVariable` table is left in place but no longer used after migration.

### Request level

`RequestVariable` — unchanged (flat list per request).

## Resolution Logic

Priority from lowest to highest:

```
global base
  → global active sub-set  (overrides global base per key)
    → collection base       (overrides global)
      → collection sub-set  (overrides collection base per key)
        → request variables (overrides everything)
```

`AppState.BuildVariables(globalBase, globalSubset, collectionBase, collectionSubset, request)` merges in this order, each step overwriting matching keys. The source color shown in autocomplete corresponds to the winning set:
- `Global` (blue) — key came from global base or global sub-set
- `Collection` (orange) — key came from collection base or collection sub-set
- `Request` (green) — key came from the request

## UI

### Global (sidebar / nav)

- The existing environment selector becomes a **global sub-set dropdown** (lists non-base AppEnvironments + "None")
- Selecting "None" = global base only
- A ⚙ button opens the global variable editor:
  - **Base** section: variables always active (existing inline editor)
  - **Sub-sets** section: list of named sub-sets, each editable inline, with a "+" button to create new ones

### Collection (⚙ per collection in sidebar)

- The existing flat-list editor becomes:
  - **Base** section: base variables for this collection (always active)
  - **Active sub-set** dropdown (sub-set names + "None") + "+" to create new sub-set
  - If a sub-set is selected: its variables displayed inline for editing
- A badge `[sub-set name]` next to the collection name in the sidebar when a sub-set is active

### Request (Variables tab in editor)

Unchanged — flat list of request variables.

## Edge Cases

- **Deleting an active sub-set:** if the deleted sub-set was the active one (global or collection), the active selection resets to null (base only) automatically.
- **Sub-set naming:** names are free-form; the base set is identified by `IsBase=true`, not by name. Users may name a sub-set "Base" without conflict.

## Acceptance Criteria

- Creating a global sub-set and selecting it overrides matching keys from the global base
- Selecting "None" at global level applies only the global base
- Creating a collection sub-set and selecting it overrides matching base variables for that collection
- Collection active sub-set selection persists across app restarts
- Global active sub-set selection persists across app restarts
- Existing `CollectionVariable` data is migrated to the base set on first run — no data loss
- Variable autocomplete shows the correct winning source color
- Resolution order: request > collection sub-set > collection base > global sub-set > global base
