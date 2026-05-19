# Scoped Variables — Design Spec

**Date:** 2026-05-19
**Status:** Approved

## Problem

Avec une cinquantaine de collections, les variables globales (sets d'environnement) deviennent ingérables pour stocker des valeurs spécifiques à chaque collection (ex: `base_url` différente par collection). Il faut un système de variables à trois niveaux de portée.

## Terminologie

| Terme | Définition |
|---|---|
| Set global | Ce qui était appelé "environment" — sélectionné dans la sidebar, partagé par toutes les collections |
| Set de collection | Variables fixes propres à une collection |
| Set de requête | Variables propres à une requête, surcharge les deux autres |

## Ordre de résolution

```
request > collection > global
```

Une variable définie dans le set de requête remplace la même clé dans le set de collection, qui elle-même remplace celle du set global. Les couches inférieures servent de fallback.

## Modèle de données

### Nouvelles entités

```csharp
// CollectionVariable — miroir de EnvironmentVariable
public class CollectionVariable
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}

// RequestVariable — miroir de EnvironmentVariable
public class RequestVariable
{
    public int Id { get; set; }
    public int HttpRequestItemId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
```

### Navigation properties ajoutées

- `Collection.Variables` → `List<CollectionVariable>`
- `HttpRequestItem.Variables` → `List<RequestVariable>`

### Migration SQLite

Deux nouvelles tables : `CollectionVariables` et `RequestVariables`.

## Résolution des variables

`AppState` expose une méthode de fusion qui produit un dictionnaire avec métadonnées de source :

```csharp
public enum VariableSource { Global, Collection, Request }

public record ResolvedVariable(string Value, VariableSource Source, bool IsSecret);

public Dictionary<string, ResolvedVariable> BuildVariables(
    AppEnvironment? env,
    Collection? collection,
    HttpRequestItem? request)
```

Fusion dans l'ordre `global → collection → request` : chaque couche supérieure écrase la même clé. `VariableResolver` reste inchangé — il reçoit un `IReadOnlyDictionary<string, string>` extrait du résultat.

## Interface utilisateur

### Variables de collection (sidebar)

- Bouton ⚙ ajouté à côté du nom de chaque collection dans la sidebar
- Éditeur inline identique à l'éditeur d'environnement existant : liste `key / value`, support `IsSecret`, bouton "+ add variable"
- Édition rapide des valeurs directement dans l'interface sans navigation

### Variables de requête (éditeur principal)

- Nouvel onglet **"Variables"** ajouté dans l'éditeur (`Params` | `Headers` | `Body` | **`Variables`**)
- Réutilise le composant `KeyValueGrid` existant

### Coloration dans autocomplete et tooltip

Les suggestions et previews indiquent la source par couleur :

| Source | Couleur |
|---|---|
| Global | 🔵 Bleu |
| Collection | 🟠 Orange |
| Request | 🟢 Vert |

`VariableInput` et `VariablePreview` reçoivent les métadonnées de source pour afficher la bonne couleur. Une variable surchargée par une couche supérieure affiche la couleur de la couche qui gagne.

### Chargement dans `Home.razor`

Au `LoadRequestAsync`, on inclut `_request.Variables` et on dérive la collection active via `_request.CollectionId` pour charger `_collection.Variables`. Les trois couches sont passées à `AppState.BuildVariables`.
