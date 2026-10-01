namespace DesktopComputerUse.Contracts.Automation;

public sealed record AutomationError(
    AutomationErrorCode Code,
    string Message,
    IReadOnlyList<ControlSummary>? Candidates = null)
{
    public AutomationDiagnostic? Diagnostic { get; init; }
    public IReadOnlyList<AutomationDiagnostic> Failures { get; init; } = [];
}
