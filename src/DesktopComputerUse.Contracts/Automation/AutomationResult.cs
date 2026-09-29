namespace DesktopComputerUse.Contracts.Automation;

public sealed record AutomationResult<T>(
    bool Succeeded,
    T? Value,
    AutomationError? Error)
{
    public static AutomationResult<T> Success(T value) => new(true, value, null);

    public static AutomationResult<T> Failure(
        AutomationErrorCode code,
        string message,
        IReadOnlyList<ControlSummary>? candidates = null)
        => new(false, default, new AutomationError(code, message, candidates));
}

public sealed record AutomationResult(
    bool Succeeded,
    AutomationError? Error)
{
    public static AutomationResult Success() => new(true, null);

    public static AutomationResult Failure(AutomationErrorCode code, string message)
        => new(false, new AutomationError(code, message));
}
