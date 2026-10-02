using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation.FlaUi;

internal static class ControlValueReader
{
    internal static ControlValueResult Read(AutomationElement element, ApplicationProfile profile)
        => Read(profile, () => element.AutomationId, () => element.Properties.IsPassword.Value,
            () => element.Patterns.Value.IsSupported,
            () => element.Patterns.Value.Pattern.Value.Value);

    internal static ControlValueResult Read(
        ApplicationProfile profile,
        Func<string?> automationId,
        Func<bool> isPassword,
        Func<bool> supportsValue,
        Func<string> readValue)
    {
        var reader = new SafeAutomationElementReader(new AutomationDiagnostic
        {
            Operation = "getControlValue", ProfileId = profile.Id,
            ProfileRevision = profile.Metadata?.Revision
        });
        var sensitivity = ControlObserver.ReadSensitivity(reader, profile, automationId, isPassword);
        if (sensitivity.IsSensitive)
        {
            return Result(null, true, profile, reader);
        }

        RequireRead(reader, "Patterns.Value", supportsValue, out var supported);
        if (!supported)
        {
            throw new AutomationOperationException(AutomationErrorCode.UnsupportedPattern,
                "The resolved control does not support the Value pattern.",
                diagnostic: reader.Context with { Phase = "readValue", Property = "Patterns.Value" });
        }

        RequireRead(reader, "Value", readValue, out var value);
        return Result(value, false, profile, reader);
    }

    private static void RequireRead<T>(
        SafeAutomationElementReader reader, string property, Func<T> getter, out T value)
    {
        try
        {
            if (reader.TryRead(property, getter, out value))
                return;
        }
        catch (InvalidOperationException exception)
        {
            reader.Failures.Add(AutomationExceptionResultMapper.CreateDiagnostic(exception,
                reader.Context with { Phase = "readProperty", Property = property }));
        }

        throw new AutomationOperationException(AutomationErrorCode.ProviderFailure,
            "The control resolved successfully, but its Value provider could not be read.",
            diagnostic: reader.Failures.Last(), failures: reader.Failures.ToArray());
    }

    private static ControlValueResult Result(
        string? value, bool redacted, ApplicationProfile profile, SafeAutomationElementReader reader)
        => new(value, redacted) { PrivacyMode = profile.PrivacyMode, Failures = reader.Failures.ToArray() };
}
