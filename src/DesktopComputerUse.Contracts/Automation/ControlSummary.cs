namespace DesktopComputerUse.Contracts.Automation;

public sealed record ControlSummary(
    string? Name,
    string? AutomationId,
    string ControlType,
    string? ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    RectangleInfo Bounds,
    string? Value,
    bool IsValueRedacted,
    IReadOnlyList<string> SupportedPatterns);
