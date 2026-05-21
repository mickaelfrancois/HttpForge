# Insomnia Folder Import

**Date:** 2026-05-21  
**Status:** Approved

## Overview

When importing an Insomnia v5 collection, preserve the folder hierarchy instead of flattening all requests to the collection root. Empty folders are created too.

---

## Insomnia Format

In the Insomnia v5 YAML, `collection` is a list of `InsomniaNode` objects. A node is a **folder** if `Url == null`; a **request** if `Url != null`. Both can have `Children`.

The `InsomniaNode` POCO already has a `Children` property — no parsing changes required.

---

## Changes

### `InsomniaImporter.cs` only

**Remove** `FlattenNodes` (the flat enumeration that discards folder structure).

**Add** `ImportNodesAsync(AppDbContext db, List<InsomniaNode> nodes, int collectionId, int? parentFolderId, List<string> warnings)`:

- Node without `Url` → create `CollectionFolder { CollectionId = collectionId, ParentFolderId = parentFolderId, Name = node.Name ?? "Folder" }`, call `db.CollectionFolders.Add(folder)` + `await db.SaveChangesAsync()` to get the generated `Id`, then recurse into `node.Children` with `parentFolderId = folder.Id`. Increment folder count.
- Node with `Url` → call existing `MapRequest(node, collectionId, warnings)` with `FolderId = parentFolderId` set on the result. Increment request count.

**Update** `ImportCollectionAsync` to call `await ImportNodesAsync(db, file.Collection ?? [], collection.Id, null, warnings)` in place of the `foreach (var node in FlattenNodes(...))` loop.

**Update** `ImportResult` record: add `int FoldersCreated` field.

---

## Cascade on `SaveChangesAsync`

The existing `ImportCollectionAsync` calls `await db.SaveChangesAsync()` once after creating the `Collection`, then again at the call site after all nodes. The new recursive method needs the folder's DB-generated `Id` before recursing, so it calls `SaveChangesAsync` once per folder. Requests are added to the context but not flushed individually — the final `SaveChangesAsync` at the call site flushes them all.

---

## Out of Scope

- Detecting duplicate folder names (import as-is, two folders with the same name are allowed).
- Updating an existing collection on re-import (already out of scope for the importer).
