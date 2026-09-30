using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class AutomationExceptionResultMapperTests
{
    [Fact]
    public void Map_preserves_typed_automation_error()
    {
        var result = AutomationExceptionResultMapper.Map<string>(
            new AutomationOperationException(
                AutomationErrorCode.AmbiguousControl,
                "ambiguous",
                []),
            callerCancelled: false);

        Assert.Equal(AutomationErrorCode.AmbiguousControl, result.Error?.Code);
        Assert.Equal("ambiguous", result.Error?.Message);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), false, AutomationErrorCode.Timeout)]
    [InlineData(typeof(OperationCanceledException), true, AutomationErrorCode.OperationCancelled)]
    [InlineData(typeof(UnauthorizedAccessException), false, AutomationErrorCode.AccessDenied)]
    [InlineData(typeof(InvalidOperationException), false, AutomationErrorCode.AutomationFailure)]
    public void Map_classifies_standard_exceptions(
        Type exceptionType,
        bool callerCancelled,
        AutomationErrorCode expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "failure")!;

        var result = AutomationExceptionResultMapper.Map<string>(
            exception,
            callerCancelled);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error?.Message));
    }
}
