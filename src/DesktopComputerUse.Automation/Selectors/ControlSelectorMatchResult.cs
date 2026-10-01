using System.Collections;
using DesktopComputerUse.Contracts.Automation;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.Selectors;

public sealed class ControlSelectorMatchResult(
    IReadOnlyList<AutomationElement> matches,
    IReadOnlyList<AutomationDiagnostic> failures,
    bool truncated) : IReadOnlyList<AutomationElement>
{
    public IReadOnlyList<AutomationDiagnostic> Failures { get; } = failures;
    public bool Partial => Failures.Count > 0;
    public bool Truncated { get; } = truncated;
    public bool IsComplete => !Truncated && !Failures.Any(failure => failure.Phase == "enumerateChildren");
    public int Count => matches.Count;
    public AutomationElement this[int index] => matches[index];
    public IEnumerator<AutomationElement> GetEnumerator() => matches.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void EnsureCompleteForAction()
    {
        if (IsComplete)
            return;
        var diagnostic = Failures.FirstOrDefault(failure => failure.Phase == "enumerateChildren") ??
            new AutomationDiagnostic { Operation = "resolveSelector", Phase = "traversalLimit" };
        throw new AutomationOperationException(
            AutomationErrorCode.IncompleteControlSearch,
            "The control search was incomplete; uniqueness cannot be established. Retry inspection before acting.",
            diagnostic: diagnostic with { Code = AutomationErrorCode.IncompleteControlSearch },
            failures: Failures);
    }
}
