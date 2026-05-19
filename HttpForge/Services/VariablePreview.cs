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
}
