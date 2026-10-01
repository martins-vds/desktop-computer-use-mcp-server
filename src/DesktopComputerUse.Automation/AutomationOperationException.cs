using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation;

public sealed class AutomationOperationException : Exception
{
    public AutomationOperationException(
        AutomationErrorCode code,
        string message,
        IReadOnlyList<ControlSummary>? candidates = null,
        AutomationDiagnostic? diagnostic = null,
        IReadOnlyList<AutomationDiagnostic>? failures = null)
        : base(message)
    {
        Code = code;
        Candidates = candidates;
        Diagnostic = diagnostic;
        Failures = failures ?? [];
    }

    public AutomationErrorCode Code { get; }

    public IReadOnlyList<ControlSummary>? Candidates { get; }
    public AutomationDiagnostic? Diagnostic { get; }
    public IReadOnlyList<AutomationDiagnostic> Failures { get; }
}
