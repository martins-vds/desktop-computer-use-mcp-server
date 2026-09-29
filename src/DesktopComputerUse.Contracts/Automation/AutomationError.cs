namespace DesktopComputerUse.Contracts.Automation;

public sealed record AutomationError(
    AutomationErrorCode Code,
    string Message,
    IReadOnlyList<ControlSummary>? Candidates = null);
