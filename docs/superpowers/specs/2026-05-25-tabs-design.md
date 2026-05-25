# Tabs — Design Spec
_Date: 2026-05-25_

## Résumé

Ajouter un système d'onglets à HttpForge : chaque requête s'ouvre dans un onglet dédié. Un seul onglet par requête. Les onglets persistent entre sessions via localStorage. L'interface est de style navigateur, premium.

---

## Choix UX

| Décision | Valeur retenue |
|---|---|
| Position de la barre | En haut de la zone d'édition, style navigateur (bordure colorée sur onglet actif) |
| Débordement | Scroll horizontal (pas de limite de nombre d'onglets) |
| Indicateur dirty | `✱` jaune avant le nom de la requête |
| Fermeture avec dirty | Modal de confirmation (Sauvegarder / Fermer sans sauvegarder / Annuler) |
| Fermer tous | Menu clic-droit sur un onglet → "Fermer tous les onglets" (sans confirmation) |
| Persistance | `localStorage["forge_tabs"]` — liste d'IDs + ID actif |

---

## Architecture

### Nouveaux éléments

**`TabState`** — Représente un onglet ouvert (classe, pas d'entité DB) :

```csharp
public class TabState
{
    public int RequestId { get; set; }
    public string Name { get; set; }        // nom affiché dans l'onglet
    public string Method { get; set; }      // GET, POST… pour le badge coloré
    public RequestDraft Draft { get; set; }
    public string ActiveSubTab { get; set; } = "Params";
    public ExecutionResult? Result { get; set; }
    public bool IsSending { get; set; }
}
```

**`TabManagerService`** — Service Scoped, source de vérité pour les onglets :

```csharp
public class TabManagerService
{
    public IReadOnlyList<TabState> Tabs { get; }
    public TabState? ActiveTab { get; }
    public event Action? OnChange;
    public event Func<TabState, Task>? OnCloseRequested; // dirty → modal

    public Task OpenTabAsync(int requestId);  // no-op si déjà ouvert
    public void ActivateTab(int requestId);
    public void CloseTab(int requestId);      // vérifie IsDirty → event si besoin
    public void CloseAllTabs();               // force, sans confirmation
    public Task InitFromLocalStorageAsync();  // appelé au démarrage
}
```

**`TabBar.razor`** — Composant dans `Components/Layout/` :
- Injecte `TabManagerService`
- Rend la barre scrollable avec les onglets
- Gère : clic pour activer, `✕` pour fermer, clic-droit pour menu contextuel
- Menu contextuel : "Fermer l'onglet" / "Fermer les autres" / "Fermer tous les onglets"

---

## Cycle de vie d'un onglet

### Ouverture (`OpenTabAsync`)
1. Si un `TabState` existe déjà pour ce `requestId` → `ActivateTab()`, fin.
2. Sinon : charge `HttpRequestItem` depuis la DB (avec includes Headers, QueryParams, FormFields, Variables).
3. Crée un `RequestDraft` (logique migrée depuis `Home.razor.LoadRequestAsync()`).
4. Ajoute le `TabState` à la liste, l'active, persiste en localStorage.

### Fermeture (`CloseTab`)
1. Si `tab.Draft.IsDirty` → déclenche `OnCloseRequested`. `Home.razor` écoute et affiche le modal de confirmation :
   - **Sauvegarder** → `RequestSaveService.SaveAsync()` puis retire l'onglet
   - **Fermer sans sauvegarder** → retire l'onglet
   - **Annuler** → ne fait rien
2. Si `!IsDirty` → retire l'onglet sans modal.
3. Après retrait : active l'onglet à gauche (ou à droite s'il n'y en a pas).

### Fermeture forcée (`CloseAllTabs`)
Vide `_tabs` sans vérification dirty. Appelé depuis le menu clic-droit.

### Persistance localStorage
- Clé : `forge_tabs`
- Format : `{ tabs: [{requestId, activeSubTab}], activeRequestId }`
- Persiste après chaque mutation (open / close / activate / changement de sous-onglet).
- Restauration au démarrage : `InitFromLocalStorageAsync()` ré-ouvre les onglets dans l'ordre en rechargeant les drafts depuis la DB. L'état dirty n'est pas restauré (normal : non sauvegardé = perdu au reload).

---

## Refactor de Home.razor

### Avant
```csharp
private RequestDraft? _draft;
private string _activeTab = "Params";
private ExecutionResult? _result;
private bool _sending;
```

### Après
Ces champs migrent dans `TabState`. `Home.razor` lit l'onglet actif :
```csharp
@inject TabManagerService Tabs
private TabState? Active => Tabs.ActiveTab;
```
Tout le template remplace `_draft` par `Active.Draft`, `_result` par `Active.Result`, etc.

### Ouverture depuis la sidebar
`RequestRow.razor` injecte `TabManagerService` et appelle `await Tabs.OpenTabAsync(Request.Id)` au lieu de setter `AppState.SelectedRequestId`. `AppState.SelectedRequestId` reste synchronisé avec `Tabs.ActiveTab?.RequestId` pour le highlight de la sidebar.

---

## UI — Tab Bar

Style navigateur premium, dark theme cohérent avec l'existant :

```
┌─────────────────────────────────────────────────────────┐
│ ✱ GET /users ✕ │ POST /auth/login ✕ │ DEL /users/{id} ✕ │ …
└─────────────────────────────────────────────────────────┘
  ▲ barre jaune      barre violette
  (dirty)            (propre, actif)
```

- Badge méthode coloré : GET=vert, POST=bleu, PUT=orange, DELETE=rouge, PATCH=violet
- Onglet actif : fond distinct + barre de 2px en haut (jaune si dirty, violet si propre)
- `✱` jaune devant le nom si `IsDirty`
- `✕` visible au hover et sur l'onglet actif
- Scroll horizontal natif (no scrollbar visible)
- Menu clic-droit : "Fermer l'onglet" / "Fermer les autres" / ── / "Fermer tous les onglets"

---

## Intégration RequestChangeNotifier

Le `RequestChangeNotifier` (Singleton) notifie quand un autre utilisateur sauvegarde une requête. Si l'onglet correspondant est ouvert et dirty, le conflit doit être signalé tab par tab (le modal de conflit existant reste fonctionnel, ciblé sur le `TabState` concerné).

---

## Fichiers impactés

| Fichier | Changement |
|---|---|
| `Services/TabManagerService.cs` | **Nouveau** |
| `Models/TabState.cs` | **Nouveau** |
| `Components/Layout/TabBar.razor(.css)` | **Nouveau** |
| `Components/Pages/Home.razor` | Refactor : lit `TabManagerService.ActiveTab` |
| `Components/Pages/Home.razor.css` | Ajout styles tab-bar |
| `Components/Layout/RequestRow.razor` | Appelle `TabManagerService.OpenTabAsync` |
| `wwwroot/forge.js` | Ajout section `forge.tabs` (localStorage r/w) |
| `Program.cs` | `AddScoped<TabManagerService>()` |

---

## Hors scope

- Drag-and-drop pour réordonner les onglets
- Split view (deux onglets côte à côte)
- Raccourcis clavier (Ctrl+W, Ctrl+Tab)
