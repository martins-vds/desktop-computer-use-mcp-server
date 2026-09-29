using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation;

public sealed class AutomationOperationException : Exception
{
    public AutomationOperationException(
        AutomationErrorCode code,
        string message,
        IReadOnlyList<ControlSummary>? candidates = null)
        : base(message)
    {
        Code = code;
        Candidates = candidates;
    }

    public AutomationErrorCode Code { get; }

    public IReadOnlyList<ControlSummary>? Candidates { get; }
}
