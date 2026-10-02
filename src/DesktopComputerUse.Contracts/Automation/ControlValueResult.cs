namespace DesktopComputerUse.Contracts.Automation;

public sealed record ControlValueResult(string? Value, bool IsValueRedacted)
{
    public bool PrivacyMode { get; init; } = true;
    public IReadOnlyList<AutomationDiagnostic> Failures { get; init; } = [];
}
