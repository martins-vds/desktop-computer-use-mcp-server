using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Resolution;

public static class WaitConditionEvaluator
{
    public static bool Matches(
        ControlSummary? control,
        WaitCondition condition)
    {
        if (condition.Property == WaitProperty.Exists)
        {
            return MatchesExistence(control, condition.Comparison);
        }

        return control is not null &&
            Compare(GetValue(control, condition.Property), condition);
    }

    private static bool MatchesExistence(
        ControlSummary? control,
        WaitComparison comparison)
        => comparison switch
        {
            WaitComparison.True => control is not null,
            WaitComparison.False => control is null,
            _ => throw new AutomationOperationException(
                AutomationErrorCode.AutomationFailure,
                "Exists waits require the True or False comparison.")
        };

    private static string? GetValue(
        ControlSummary control,
        WaitProperty property)
        => property switch
        {
            WaitProperty.Name => control.Name,
            WaitProperty.Value => control.Value,
            WaitProperty.IsEnabled => control.IsEnabled.ToString(),
            WaitProperty.IsOffscreen => control.IsOffscreen.ToString(),
            _ => null
        };

    private static bool Compare(
        string? actual,
        WaitCondition condition)
    {
        var comparisons = new Dictionary<WaitComparison, Func<bool>>
        {
            [WaitComparison.Equals] = () => string.Equals(
                actual,
                condition.ExpectedValue,
                StringComparison.Ordinal),
            [WaitComparison.NotEquals] = () => !string.Equals(
                actual,
                condition.ExpectedValue,
                StringComparison.Ordinal),
            [WaitComparison.Contains] = () => actual?.Contains(
                condition.ExpectedValue ?? string.Empty,
                StringComparison.Ordinal) is true,
            [WaitComparison.True] = () => ParseBoolean(actual, expected: true),
            [WaitComparison.False] = () => ParseBoolean(actual, expected: false)
        };
        return comparisons.TryGetValue(condition.Comparison, out var comparison) &&
            comparison();
    }

    private static bool ParseBoolean(string? value, bool expected)
        => bool.TryParse(value, out var parsed) && parsed == expected;
}
