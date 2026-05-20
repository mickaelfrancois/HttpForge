# URL Preview Design

**Date:** 2026-05-20
**Feature:** Resolved URL preview below the URL input

## Problem

The URL bar accepts `{{variable}}` placeholders but shows only raw text. Users must hover to see individual variable values via the tooltip. There is no way to see the full composed URL at a glance.

## Solution

Display the fully resolved URL as a single line of muted monospace text immediately below the URL bar. The preview is visible only when the URL contains at least one `{{` token.

## Resolution Rules

- Variable found, not secret → replace `{{key}}` with its value
- Variable is secret → leave `{{key}}` unchanged (no value leaked)
- Variable not defined → leave `{{key}}` unchanged

## Implementation

### `VariablePreview.Resolve`

New static method on the existing `VariablePreview` class:

```csharp
public static string Resolve(string? input, IReadOnlyList<ResolvedVariableEntry> variables)
```

Uses the existing `Pattern()` regex. For each match, substitutes the value if the variable exists and is not secret; otherwise leaves the original `{{key}}` token intact. Returns the full string with all applicable substitutions applied.

### `Home.razor`

One block added immediately after the `.url-bar` div:

```razor
@if (_request.Url?.Contains("{{") == true)
{
    <div class="url-preview">@VariablePreview.Resolve(_request.Url, _resolvedVariables)</div>
}
```

### `Home.razor.css`

```css
.url-preview {
    font-size: 0.75rem;
    color: var(--text-muted);
    font-family: monospace;
    padding: 2px 4px;
    word-break: break-all;
}
```

## Files Changed

- `HttpForge/Services/VariablePreview.cs` — add `Resolve` method
- `HttpForge/Components/Pages/Home.razor` — add preview block after url-bar
- `HttpForge/Components/Pages/Home.razor.css` — add `.url-preview` rule

## Acceptance Criteria

- URL with no `{{` tokens → preview not shown
- URL with `{{key}}` where key is defined and not secret → preview shows value substituted
- URL with `{{secret}}` → preview shows `{{secret}}` unchanged
- URL with `{{undefined}}` → preview shows `{{undefined}}` unchanged
- Preview updates live as the user types in the URL field
- Long resolved URLs wrap rather than overflow
