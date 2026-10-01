using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Selectors;

internal sealed record SelectorCandidateMatchResult<T>(
    T[] Matches,
    IReadOnlyList<AutomationDiagnostic> Failures,
    bool Truncated);
