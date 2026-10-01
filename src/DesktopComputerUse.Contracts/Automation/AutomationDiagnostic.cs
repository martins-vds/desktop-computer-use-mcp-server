namespace DesktopComputerUse.Contracts.Automation;

public sealed record AutomationDiagnostic
{
    public AutomationErrorCode Code { get; init; } = AutomationErrorCode.AutomationFailure;
    public string? Operation { get; init; }
    public string? Phase { get; init; }
    public string? Property { get; init; }
    public string? ExceptionType { get; init; }
    public string? HResult { get; init; }
    public int? Win32Error { get; init; }
    public string? ProfileId { get; init; }
    public string? ProfileRevision { get; init; }
    public int? ProcessId { get; init; }
    public string? CleanupOutcome { get; init; }
    public IReadOnlyList<MatchingApplicationProcess> MatchingProcesses { get; init; } = [];
    public long? Hwnd { get; init; }
    public int? ForegroundProcessId { get; init; }
    public long? ForegroundHwnd { get; init; }
    public uint? DispatchedInputCount { get; init; }
    public bool InputMayHaveOccurred { get; init; }
    public string? CandidateId { get; init; }
    public int? Depth { get; init; }
    public bool Retryable { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
