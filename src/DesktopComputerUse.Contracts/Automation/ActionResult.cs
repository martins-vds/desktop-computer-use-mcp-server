namespace DesktopComputerUse.Contracts.Automation;

public sealed record ActionResult(
    string Action,
    ControlSummary Target,
    string? ObservedValue,
    DateTimeOffset CompletedAt);
