using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Automation.Selectors;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControllerResolutionPolicyTests
{
    [Fact]
    public void Unsupported_sibling_properties_do_not_make_completed_search_incomplete()
    {
        var failures = new[]
        {
            new AutomationDiagnostic { Phase = "readProperty", Property = "Name" },
            new AutomationDiagnostic { Phase = "readProperty", Property = "AutomationId" }
        };
        var result = new ControlSelectorMatchResult([], failures, false);
        DesktopAutomationController.ThrowIfSearchIncomplete([result]);
        Assert.True(result.IsComplete);
        Assert.True(result.Partial);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unknown_subtrees_and_truncation_still_block_search(bool truncated)
    {
        var failure = new AutomationDiagnostic { Phase = "enumerateChildren" };
        var result = new ControlSelectorMatchResult([], truncated ? [] : [failure], truncated);
        var exception = Assert.Throws<AutomationOperationException>(() =>
            DesktopAutomationController.ThrowIfSearchIncomplete([result]));
        Assert.Equal(AutomationErrorCode.ProviderFailure, exception.Code);
        Assert.Equal(truncated ? 0 : 1, exception.Failures.Count);
    }

    [Fact]
    public void Subtree_failure_is_not_lost_when_property_diagnostics_exceed_output_limit()
    {
        var failures = Enumerable.Range(0, 100)
            .Select(_ => new AutomationDiagnostic { Phase = "readProperty" })
            .Append(new AutomationDiagnostic { Phase = "enumerateChildren" }).ToArray();
        var exception = Assert.Throws<AutomationOperationException>(() =>
            DesktopAutomationController.ThrowIfSearchIncomplete([new([], failures, false)]));
        Assert.Equal(100, exception.Failures.Count);
    }

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
