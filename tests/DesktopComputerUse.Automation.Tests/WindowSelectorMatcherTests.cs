using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Tests;

public sealed class WindowSelectorMatcherTests
{
    [Fact]
    public void Matches_accepts_empty_selector()
        => Assert.True(WindowSelectorMatcher.Matches(
            "Customer 123",
            "MainWindow",
            new WindowSelector()));

    [Theory]
    [InlineData("Customer", "Main", "Customer", null, "Main", true)]
    [InlineData("Customer", "Main", "Other", null, "Main", false)]
    [InlineData("Customer", "Main", "Customer", null, "Other", false)]
    [InlineData("Customer 123", "Main", null, "^Customer \\d+$", null, true)]
    [InlineData("Customer ABC", "Main", null, "^Customer \\d+$", null, false)]
    public void Matches_applies_title_class_and_regex(
        string title,
        string className,
        string? expectedTitle,
        string? titleRegex,
        string? expectedClass,
        bool expected)
        => Assert.Equal(
            expected,
            WindowSelectorMatcher.Matches(
                title,
                className,
                new WindowSelector
                {
                    Title = expectedTitle,
                    TitleRegex = titleRegex,
                    ClassName = expectedClass
                }));
}
