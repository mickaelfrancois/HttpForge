using System.Text.RegularExpressions;

namespace HttpForge.Services;

public partial class VariableResolver
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_\-\.]+)\s*\}\}")]
    private static partial Regex VariablePattern();

    public string Resolve(string? input, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;

        return VariablePattern().Replace(input, m =>
        {
            var key = m.Groups[1].Value;
            return variables.TryGetValue(key, out var v) ? v : m.Value;
        });
    }
}
