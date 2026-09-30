using System.Text.RegularExpressions;
using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Applications;

public static class WindowSelectorMatcher
{
    public static bool Matches(
        string? title,
        string? className,
        WindowSelector selector)
        => MatchesOptional(title, selector.Title) &&
            MatchesOptional(className, selector.ClassName) &&
            MatchesRegex(title, selector.TitleRegex);

    private static bool MatchesOptional(string? actual, string? expected)
        => string.IsNullOrWhiteSpace(expected) ||
            string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool MatchesRegex(string? actual, string? pattern)
        => string.IsNullOrWhiteSpace(pattern) ||
            Regex.IsMatch(
                actual ?? string.Empty,
                pattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
}
