using System.Runtime.InteropServices;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ProviderActionExecutorTests
{
    [Fact]
    public void A_successful_action_is_executed_exactly_once()
    {
        var calls = 0;
        ProviderActionExecutor.Execute("invoke", "Invoke", () => calls++);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("set_value", "Value.SetValue", true, AutomationErrorCode.UnsupportedPattern)]
    [InlineData("invoke", "Invoke", true, AutomationErrorCode.UnsupportedPattern)]
    [InlineData("invoke", "Invoke", false, AutomationErrorCode.ProviderFailure)]
    [InlineData("set_value", "Value.SetValue", false, AutomationErrorCode.ProviderFailure)]
    public void Action_failures_distinguish_execution_and_never_retry_or_leak_provider_messages(
        string operation, string pattern, bool rejected, AutomationErrorCode code)
    {
        var calls = 0;
        var exception = Assert.Throws<AutomationOperationException>(() =>
            ProviderActionExecutor.Execute(operation, pattern, () =>
            {
                calls++;
                if (rejected)
                    throw new InvalidOperationException("private supplied value");
                throw new COMException("private supplied value");
            }));
        Assert.Equal(1, calls);
        Assert.Equal(code, exception.Code);
        Assert.Equal(operation, exception.Diagnostic!.Operation);
        Assert.Equal("executePattern", exception.Diagnostic.Phase);
        Assert.Equal(pattern, exception.Diagnostic.Property);
        Assert.Equal(code, exception.Diagnostic.Code);
        Assert.Equal(rejected ? "InvalidOperationException" : "COMException", exception.Diagnostic.ExceptionType);
        Assert.False(exception.Diagnostic.Retryable);
        Assert.True(exception.Diagnostic.InputMayHaveOccurred);
        Assert.DoesNotContain("private supplied value", exception.Message);
        Assert.Contains(rejected ? "provider rejected" : "resolved successfully", exception.Message);
        var result = AutomationExceptionResultMapper.Map<ActionResult>(exception, false);
        Assert.Equal(code, result.Error!.Code);
        Assert.True(result.Error.Diagnostic!.InputMayHaveOccurred);
        Assert.False(result.Error.Diagnostic.Retryable);
    }

    [Fact]
    public void Transient_provider_failure_does_not_authorize_action_replay()
    {
        var exception = Assert.Throws<AutomationOperationException>(() =>
            ProviderActionExecutor.Execute("invoke", "Invoke",
                () => throw new COMException("busy", unchecked((int)0x80010001))));
        Assert.False(exception.Diagnostic!.Retryable);
        Assert.True(exception.Diagnostic.InputMayHaveOccurred);
        Assert.Equal("0x80010001", exception.Diagnostic.HResult);
    }

    [Fact]
    public void Cancellation_and_unexpected_programming_errors_propagate()
    {
        Assert.Throws<OperationCanceledException>(() => ProviderActionExecutor.Execute(
            "invoke", "Invoke", () => throw new OperationCanceledException()));
        Assert.Throws<ArgumentException>(() => ProviderActionExecutor.Execute(
            "invoke", "Invoke", () => throw new ArgumentException()));
    }
}
