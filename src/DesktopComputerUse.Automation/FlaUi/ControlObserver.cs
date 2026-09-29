using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.FlaUi;

public sealed class ControlObserver
{
    public ControlSummary Observe(
        AutomationElement element,
        ApplicationProfile profile)
    {
        var isSensitive = !string.IsNullOrWhiteSpace(element.AutomationId) &&
            profile.SensitiveAutomationIds.Contains(
                element.AutomationId,
                StringComparer.OrdinalIgnoreCase);

        return new ControlSummary(
            NullIfEmpty(element.Name),
            NullIfEmpty(element.AutomationId),
            element.ControlType.ToString(),
            NullIfEmpty(element.ClassName),
            element.IsEnabled,
            element.IsOffscreen,
            new RectangleInfo(
                element.BoundingRectangle.X,
                element.BoundingRectangle.Y,
                element.BoundingRectangle.Width,
                element.BoundingRectangle.Height),
            isSensitive ? null : ReadValue(element),
            isSensitive,
            GetSupportedPatterns(element));
    }

    private static string? ReadValue(AutomationElement element)
    {
        if (element.Patterns.Value.TryGetPattern(out var pattern))
        {
            return NullIfEmpty(pattern.Value.ValueOrDefault);
        }

        return null;
    }

    private static IReadOnlyList<string> GetSupportedPatterns(AutomationElement element)
    {
        var patterns = new List<string>(5);
        AddIfSupported(patterns, element.Patterns.Invoke.IsSupported, "Invoke");
        AddIfSupported(patterns, element.Patterns.Value.IsSupported, "Value");
        AddIfSupported(patterns, element.Patterns.SelectionItem.IsSupported, "SelectionItem");
        AddIfSupported(patterns, element.Patterns.ExpandCollapse.IsSupported, "ExpandCollapse");
        AddIfSupported(patterns, element.Patterns.Scroll.IsSupported, "Scroll");
        return patterns;
    }

    private static void AddIfSupported(
        ICollection<string> patterns,
        bool isSupported,
        string name)
    {
        if (isSupported)
        {
            patterns.Add(name);
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}
