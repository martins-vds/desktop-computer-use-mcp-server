using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.FlaUi;

public sealed class SafeAutomationElementReader
{
    public SafeAutomationElementReader(
        AutomationDiagnostic context,
        ICollection<AutomationDiagnostic>? failures = null)
    {
        Context = context;
        Failures = failures ?? new List<AutomationDiagnostic>();
    }

    public AutomationDiagnostic Context { get; }
    public ICollection<AutomationDiagnostic> Failures { get; }

    public T Read<T>(string property, Func<T> getter, T fallback)
        => TryRead(property, getter, out var value) ? value : fallback;

    public bool TryRead<T>(string property, Func<T> getter, out T value)
        => Try("readProperty", property, getter, out value);

    public T[] ReadChildren<T>(Func<T[]> getter)
        => Try("enumerateChildren", null, getter, out var children) ? children : [];

    public bool Try<T>(string phase, string? property, Func<T> getter, out T value)
    {
        try
        {
            value = getter();
            return true;
        }
        catch (Exception exception) when (AutomationExceptionResultMapper.IsExpectedProviderException(exception))
        {
            Failures.Add(AutomationExceptionResultMapper.CreateDiagnostic(
                exception, Context with { Phase = phase, Property = property }));
            value = default!;
            return false;
        }
    }
}
