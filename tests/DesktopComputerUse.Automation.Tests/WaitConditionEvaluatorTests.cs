using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class WaitConditionEvaluatorTests
{
    private static readonly ControlSummary Control = new(
        "Customer name",
        "CustomerNameTextBox",
        "Edit",
        "WindowsForms10.EDIT",
        true,
        false,
        new RectangleInfo(1, 2, 3, 4),
        "Ada",
        false,
        ["Value"]);

    [Fact]
    public void Matches_evaluates_existence()
    {
        Assert.True(WaitConditionEvaluator.Matches(
            Control,
            new WaitCondition(WaitProperty.Exists, WaitComparison.True)));
        Assert.True(WaitConditionEvaluator.Matches(
            null,
            new WaitCondition(WaitProperty.Exists, WaitComparison.False)));
        Assert.False(WaitConditionEvaluator.Matches(
            null,
            new WaitCondition(WaitProperty.Exists, WaitComparison.True)));
    }

    [Fact]
    public void Matches_rejects_invalid_existence_comparison()
    {
        var exception = Assert.Throws<AutomationOperationException>(() =>
            WaitConditionEvaluator.Matches(
                Control,
                new WaitCondition(WaitProperty.Exists, WaitComparison.Equals)));

        Assert.Equal(AutomationErrorCode.AutomationFailure, exception.Code);
        Assert.Equal(
            "Exists waits require the True or False comparison.",
            exception.Message);
    }

    [Theory]
    [InlineData(WaitProperty.Name, WaitComparison.Equals, "Customer name", true)]
    [InlineData(WaitProperty.Name, WaitComparison.NotEquals, "Search", true)]
    [InlineData(WaitProperty.Value, WaitComparison.Contains, "Ad", true)]
    [InlineData(WaitProperty.Value, WaitComparison.Contains, "Grace", false)]
    [InlineData(WaitProperty.IsEnabled, WaitComparison.True, null, true)]
    [InlineData(WaitProperty.IsEnabled, WaitComparison.False, null, false)]
    [InlineData(WaitProperty.IsOffscreen, WaitComparison.False, null, true)]
    public void Matches_evaluates_control_properties(
        WaitProperty property,
        WaitComparison comparison,
        string? expected,
        bool result)
        => Assert.Equal(
            result,
            WaitConditionEvaluator.Matches(
                Control,
                new WaitCondition(property, comparison, expected)));

    [Fact]
    public void Matches_returns_false_for_missing_non_existence_property()
        => Assert.False(WaitConditionEvaluator.Matches(
            null,
            new WaitCondition(
                WaitProperty.Value,
                WaitComparison.Equals,
                "Ada")));

    [Fact]
    public void Matches_true_comparison_rejects_false_boolean()
        => Assert.False(WaitConditionEvaluator.Matches(
            Control,
            new WaitCondition(
                WaitProperty.IsOffscreen,
                WaitComparison.True)));

    [Fact]
    public void Matches_returns_false_for_unknown_comparison()
        => Assert.False(WaitConditionEvaluator.Matches(
            Control,
            new WaitCondition(
                WaitProperty.Name,
                (WaitComparison)999,
                "Customer name")));
}
