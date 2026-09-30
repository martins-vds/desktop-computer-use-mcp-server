using System.Text;
using System.Text.RegularExpressions;

namespace DesktopComputerUse.Automation.Resolution;

public static partial class TextNormalizer
{
    private static readonly HashSet<string> ControlPrefixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "txt",
            "btn",
            "cmb",
            "ddl",
            "lbl"
        };

    public static IReadOnlyList<string> Tokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = Tokenize(value);
        RemoveControlPrefix(tokens);
        return tokens.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static List<string> Tokenize(string value)
    {
        var expanded = CamelCaseBoundary().Replace(value.Normalize(), "$1 $2");
        var builder = new StringBuilder(expanded.Length);
        foreach (var character in expanded)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static void RemoveControlPrefix(List<string> tokens)
    {
        if (RemoveSeparatedPrefix(tokens))
        {
            return;
        }

        RemoveCompactPrefix(tokens);
    }

    private static bool RemoveSeparatedPrefix(List<string> tokens)
    {
        if (tokens.Count <= 1 || !ControlPrefixes.Contains(tokens[0]))
        {
            return false;
        }

        tokens.RemoveAt(0);
        return true;
    }

    private static void RemoveCompactPrefix(List<string> tokens)
    {
        if (tokens.Count != 1)
        {
            return;
        }

        var token = tokens[0];
        var prefix = ControlPrefixes.FirstOrDefault(candidate =>
            token.Length > candidate.Length + 2 &&
            token.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (prefix is not null)
        {
            tokens[0] = token[prefix.Length..];
        }
    }

    public static double Similarity(string? left, string? right)
        => Similarity(Tokens(left), Tokens(right));

    public static double Similarity(
        IEnumerable<string> left,
        IEnumerable<string> right)
    {
        var leftSet = left.ToHashSet(StringComparer.Ordinal);
        var rightSet = right.ToHashSet(StringComparer.Ordinal);
        if (leftSet.Count == 0 || rightSet.Count == 0)
        {
            return 0;
        }

        if (leftSet.SetEquals(rightSet))
        {
            return 1;
        }

        var intersection = leftSet.Count(rightSet.Contains);
        var union = leftSet.Count + rightSet.Count - intersection;
        var jaccard = union == 0 ? 0 : (double)intersection / union;

        var prefixMatches = leftSet.Count(leftToken =>
            rightSet.Any(rightToken =>
                leftToken.StartsWith(rightToken, StringComparison.Ordinal) ||
                rightToken.StartsWith(leftToken, StringComparison.Ordinal)));
        var prefixScore = (double)prefixMatches / Math.Max(leftSet.Count, rightSet.Count);

        return Math.Max(jaccard, prefixScore * 0.9);
    }

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelCaseBoundary();
}
