using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class LaunchPolicyParsingTests
{
    [Theory]
    [InlineData("fail", LaunchPolicy.Fail)]
    [InlineData("attach", LaunchPolicy.Attach)]
    [InlineData("launchNew", LaunchPolicy.LaunchNew)]
    public void Parses_exact_supported_values(string value, LaunchPolicy expected)
    {
        Assert.True(DesktopAutomationController.TryParseLaunchPolicy(value, out var policy));
        Assert.Equal(expected, policy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LaunchNew")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void Unsupported_values_are_rejected(string? value)
        => Assert.False(DesktopAutomationController.TryParseLaunchPolicy(value, out _));
}
