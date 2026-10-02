using System.Runtime.InteropServices;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControlValueReaderTests
{
    [Theory]
    [InlineData(true, "hello")]
    [InlineData(false, "hello")]
    [InlineData(true, "")]
    [InlineData(false, "")]
    public void Reads_preserve_exact_current_value_and_empty_text(bool privacy, string value)
    {
        var result = ControlValueReader.Read(TestProfile.Create() with { PrivacyMode = privacy },
            () => "ordinary", () => false, () => true, () => value);
        Assert.Equal(value, result.Value);
        Assert.False(result.IsValueRedacted);
        Assert.Equal(privacy, result.PrivacyMode);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("configured")]
    [InlineData("failedId")]
    [InlineData("failedPassword")]
    public void Protected_reads_are_redacted_without_accessing_pattern_or_value(string scenario)
    {
        var profile = TestProfile.Create() with { SensitiveAutomationIds = ["SECRET"] };
        var result = ControlValueReader.Read(profile,
            () => scenario == "failedId" ? throw new COMException() : "secret",
            () => scenario == "failedPassword" ? throw new COMException() : scenario == "password",
            () => throw new InvalidOperationException("must not probe pattern"),
            () => throw new InvalidOperationException("must not read protected value"));
        Assert.True(result.IsValueRedacted);
        Assert.True(result.PrivacyMode);
        Assert.Null(result.Value);
        Assert.Equal(scenario.StartsWith("failed", StringComparison.Ordinal) ? 1 : 0, result.Failures.Count);
    }

    [Fact]
    public void Disabled_privacy_returns_value_and_retains_failed_sensitivity_diagnostics()
    {
        var result = ControlValueReader.Read(TestProfile.Create() with { PrivacyMode = false },
            () => throw new COMException(), () => throw new COMException(), () => true, () => "secret");
        Assert.Equal("secret", result.Value);
        Assert.False(result.IsValueRedacted);
        Assert.False(result.PrivacyMode);
        Assert.Equal(["AutomationId", "IsPassword"], result.Failures.Select(failure => failure.Property));
    }

    [Theory]
    [InlineData("Patterns.Value")]
    [InlineData("Value")]
    public void Failed_value_reads_are_errors_instead_of_successful_null_values(string property)
    {
        var exception = Assert.Throws<AutomationOperationException>(() => ControlValueReader.Read(
            TestProfile.Create(), () => "ordinary", () => false,
            () => property == "Patterns.Value" ? throw new COMException("sensitive provider message") : true,
            () => throw new COMException("sensitive provider message")));
        Assert.Equal(AutomationErrorCode.ProviderFailure, exception.Code);
        Assert.Equal(property, exception.Diagnostic!.Property);
        Assert.Equal("getControlValue", exception.Diagnostic.Operation);
        Assert.Equal("COMException", exception.Diagnostic.ExceptionType);
        Assert.Equal("readProperty", exception.Diagnostic.Phase);
        Assert.Single(exception.Failures);
        Assert.Equal("The control resolved successfully, but its Value provider could not be read.", exception.Message);
        Assert.DoesNotContain("sensitive provider message", exception.Message);
    }

    [Fact]
    public void Unsupported_value_pattern_never_reads_the_value()
    {
        var exception = Assert.Throws<AutomationOperationException>(() => ControlValueReader.Read(
            TestProfile.Create(), () => "ordinary", () => false, () => false,
            () => throw new InvalidOperationException("must not read")));
        Assert.Equal(AutomationErrorCode.UnsupportedPattern, exception.Code);
        Assert.Equal("Patterns.Value", exception.Diagnostic!.Property);
        Assert.Equal("readValue", exception.Diagnostic.Phase);
        Assert.Equal("The resolved control does not support the Value pattern.", exception.Message);
    }

    [Fact]
    public void Advertised_but_rejected_value_read_returns_a_sanitized_provider_failure()
    {
        var exception = Assert.Throws<AutomationOperationException>(() => ControlValueReader.Read(
            TestProfile.Create(), () => "ordinary", () => false, () => true,
            () => throw new InvalidOperationException("private provider value")));
        Assert.Equal(AutomationErrorCode.ProviderFailure, exception.Code);
        Assert.Equal("InvalidOperationException", exception.Diagnostic!.ExceptionType);
        Assert.Equal("Value", exception.Diagnostic.Property);
        Assert.Equal("readProperty", exception.Diagnostic.Phase);
        Assert.Single(exception.Failures);
        Assert.DoesNotContain("private provider value", exception.Message);
    }

    [Fact]
    public void Value_failure_diagnostic_identifies_the_failed_read_not_an_earlier_sensitivity_failure()
    {
        var exception = Assert.Throws<AutomationOperationException>(() => ControlValueReader.Read(
            TestProfile.Create() with { PrivacyMode = false },
            () => throw new COMException(), () => false, () => true,
            () => throw new COMException()));
        Assert.Equal(["AutomationId", "Value"], exception.Failures.Select(failure => failure.Property));
        Assert.Same(exception.Failures.Last(), exception.Diagnostic);
        Assert.Equal("Value", exception.Diagnostic!.Property);
        Assert.Equal("readProperty", exception.Diagnostic.Phase);
    }

    [Fact]
    public void Read_cancellation_propagates_without_becoming_a_provider_failure()
        => Assert.Throws<OperationCanceledException>(() => ControlValueReader.Read(
            TestProfile.Create(), () => "ordinary", () => false, () => true,
            () => throw new OperationCanceledException()));
}
