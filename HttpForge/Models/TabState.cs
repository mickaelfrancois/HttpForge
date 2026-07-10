using HttpForge.Data.Entities;
using HttpForge.Services;

namespace HttpForge.Models;

// Discriminates what a tab hosts. Request tabs are keyed by RequestId; the
// CollectionSettings tab is keyed by CollectionId. A single canonical string Key
// (see below) unifies iteration/activation/closing across both kinds without an
// integer-space collision between the two id sources.
public enum TabKind { Request, CollectionSettings }

public class TabState
{
    public TabKind Kind { get; init; } = TabKind.Request;

    public int RequestId { get; set; }
    public int CollectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    // Non-null only for request tabs; a CollectionSettings tab has no editable draft.
    public RequestDraft Draft { get; set; } = null!;
    public string ActiveSubTab { get; set; } = "Params";
    public string ActiveResponseTab { get; set; } = "Body";
    public ExecutionResult? Result { get; set; }
    public ScriptResult? ScriptResult { get; set; }
    public bool IsSending { get; set; }
    public CancellationTokenSource? SendCts { get; set; }

    // A locked tab is skipped by every bulk close and hides its close button; only the
    // explicit "Fermer l'onglet" context-menu action can still close it.
    public bool IsLocked { get; set; }

    // Auto-save status indicator (debounced background save).
    public DateTime? LastSavedAt { get; set; }
    public bool IsAutoSaving { get; set; }

    // Canonical identity across tab kinds — the single source of the key format,
    // reused by TabManagerService so a request id and a collection id never collide.
    public static string RequestKey(int requestId) => $"request:{requestId}";
    public static string CollectionKey(int collectionId) => $"collection:{collectionId}";

    public string Key => Kind == TabKind.CollectionSettings
        ? CollectionKey(CollectionId)
        : RequestKey(RequestId);
}
