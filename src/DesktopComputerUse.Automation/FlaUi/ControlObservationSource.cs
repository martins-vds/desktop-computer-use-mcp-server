namespace DesktopComputerUse.Automation.FlaUi;

internal sealed record ControlObservationSource(
    Func<string?> AutomationId,
    Func<bool> IsPassword,
    Func<string?> Name,
    Func<string> ControlType,
    Func<string?> ClassName,
    Func<bool> IsEnabled,
    Func<bool> IsOffscreen,
    Func<System.Drawing.Rectangle> Bounds,
    Func<string?> Value,
    IReadOnlyDictionary<string, Func<bool>> Patterns);
