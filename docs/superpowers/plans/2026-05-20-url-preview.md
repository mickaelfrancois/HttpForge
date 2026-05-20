# URL Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the fully resolved URL (with `{{variables}}` substituted) as a muted monospace line below the URL input bar.

**Architecture:** One new static method `VariablePreview.Resolve` performs the substitution. `Home.razor` renders the result inline when the URL contains `{{`. No new components or state fields.

**Tech Stack:** Blazor Server, C# static method, CSS custom properties already defined in the theme.

---

## File Map

- Modify: `HttpForge/Services/VariablePreview.cs` — add `Resolve` method
- Modify: `HttpForge/Components/Pages/Home.razor` — add preview `<div>` after `.url-bar`
- Modify: `HttpForge/Components/Pages/Home.razor.css` — add `.url-preview` rule

---

## Task 1: URL Preview

**Files:**
- Modify: `HttpForge/Services/VariablePreview.cs`
- Modify: `HttpForge/Components/Pages/Home.razor`
- Modify: `HttpForge/Components/Pages/Home.razor.css`

### Context

`VariablePreview` is a static partial class in `HttpForge/Services/VariablePreview.cs`. It already has a `[GeneratedRegex]` source-generated `Pattern()` that matches `{{key}}` tokens. The existing `Build` method produces a tooltip string; `Resolve` is a sibling method that does full substitution instead.

`Home.razor` has this structure after the request-name row:
```razor
<div class="url-bar">
    ...method select, VariableInput, send button...
</div>

<div class="tabs">
    ...
```

The preview div goes between `.url-bar` and `.tabs`.

`_resolvedVariables` is already populated in `Home.razor` — it is an `IReadOnlyList<ResolvedVariableEntry>` built by `AppState.BuildVariables`. `_request.Url` is the raw URL string with `{{variable}}` placeholders.

`ResolvedVariableEntry` is a record with `Key`, `Value`, `IsSecret`, `Source` properties.

---

- [ ] **Step 1: Add `Resolve` to `VariablePreview.cs`**

Open `HttpForge/Services/VariablePreview.cs`. The current file ends at line 33. Add the new method inside the class, after the `Build` method:

Full file after change:
```csharp
using System.Text.RegularExpressions;

namespace HttpForge.Services;

public static partial class VariablePreview
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_\-\.]+)\s*\}\}")]
    private static partial Regex Pattern();

    public static string Build(string? input, IReadOnlyList<ResolvedVariableEntry> variables)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var matches = Pattern().Matches(input);
        if (matches.Count == 0) return string.Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (Match m in matches)
        {
            var key = m.Groups[1].Value;
            if (!seen.Add(key)) continue;
            var found = variables.FirstOrDefault(
                v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
            if (found is null)
                lines.Add($"{{{{{key}}}}} → (not defined)");
            else if (found.IsSecret)
                lines.Add($"{{{{{key}}}}} → (secret) [{found.Source}]");
            else
                lines.Add($"{{{{{key}}}}} → {found.Value} [{found.Source}]");
        }
        return string.Join("\n", lines);
    }

    public static string Resolve(string? input, IReadOnlyList<ResolvedVariableEntry> variables)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return Pattern().Replace(input, m =>
        {
            var key = m.Groups[1].Value;
            var found = variables.FirstOrDefault(
                v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
            return found is { IsSecret: false } ? found.Value : m.Value;
        });
    }
}
```

Substitution rules:
- Variable found and not secret → replace `{{key}}` with `found.Value`
- Variable is secret → leave `{{key}}` unchanged (`m.Value` is the original match)
- Variable not found → leave `{{key}}` unchanged (`m.Value`)

- [ ] **Step 2: Build to verify `Resolve` compiles**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Add preview block to `Home.razor`**

Open `HttpForge/Components/Pages/Home.razor`. Find the closing `</div>` of the `.url-bar` block (currently at line 42) and the `<div class="tabs">` that follows (currently at line 44). Insert the preview block between them:

```razor
        </div>
        @if (_request.Url?.Contains("{{") == true)
        {
            <div class="url-preview">@VariablePreview.Resolve(_request.Url, _resolvedVariables)</div>
        }

        <div class="tabs">
```

`VariablePreview` is already imported via `HttpForge.Services` which is in `_Imports.razor`. No additional `@using` needed.

- [ ] **Step 4: Add `.url-preview` CSS rule to `Home.razor.css`**

Append to the end of `HttpForge/Components/Pages/Home.razor.css`:

```css
.url-preview {
    font-size: 0.75rem;
    color: var(--text-muted);
    font-family: 'Consolas', monospace;
    padding: 2px 1rem 4px;
    word-break: break-all;
}
```

- [ ] **Step 5: Build to verify everything compiles**

```bash
dotnet build HttpForge/HttpForge.csproj
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Verify manually**

Run the app and check:
- URL with no `{{` → no preview shown
- URL `https://api.example.com/{{baseUrl}}/users/{{userId}}` with `baseUrl=staging` and `userId=42` (non-secret) → preview shows `https://api.example.com/staging/users/42`
- URL with a secret variable `{{token}}` → preview shows `{{token}}` unchanged
- URL with an undefined variable `{{missing}}` → preview shows `{{missing}}` unchanged
- Preview updates live while typing in the URL field

- [ ] **Step 7: Commit**

```bash
git add HttpForge/Services/VariablePreview.cs HttpForge/Components/Pages/Home.razor HttpForge/Components/Pages/Home.razor.css
git commit -m "feat: add resolved URL preview below URL input bar"
```
