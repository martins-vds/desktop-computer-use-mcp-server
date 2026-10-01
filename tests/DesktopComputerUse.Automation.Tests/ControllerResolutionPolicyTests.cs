using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControllerResolutionPolicyTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(1, true, false)]
    public void Unique_match_requires_complete_traversal(int count, bool truncated, bool expected)
        => Assert.Equal(expected, DesktopAutomationController.CompleteUniqueMatch(count, truncated));

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    public void Search_failures_and_truncation_cannot_become_not_found_success(
        int failedCount, bool truncated, bool expected)
        => Assert.Equal(expected, DesktopAutomationController.SearchIncomplete(failedCount, truncated));

    [Fact]
    public void Selector_audit_description_uses_stable_priority_without_control_values()
    {
        var selector = new ControlSelector
        {
            SemanticKey = "semantic", AutomationId = "id", Name = "name", ControlType = "Button"
        };
        Assert.Equal("semantic", DesktopAutomationController.DescribeSelector(selector));
        selector = selector with { SemanticKey = null };
        Assert.Equal("id", DesktopAutomationController.DescribeSelector(selector));
        selector = selector with { AutomationId = null };
        Assert.Equal("name", DesktopAutomationController.DescribeSelector(selector));
        selector = selector with { Name = null };
        Assert.Equal("Button", DesktopAutomationController.DescribeSelector(selector));
        Assert.Equal("unspecified", DesktopAutomationController.DescribeSelector(new ControlSelector()));
    }
}
