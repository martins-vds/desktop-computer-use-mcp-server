using DesktopComputerUse.Automation.Resolution;
using FlaUI.Core.Definitions;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ScrollAmountResolverTests
{
    [Theory]
    [InlineData(0, 0, ScrollAmount.NoAmount)]
    [InlineData(2, 0, ScrollAmount.SmallIncrement)]
    [InlineData(2, 1, ScrollAmount.SmallIncrement)]
    [InlineData(2, 2, ScrollAmount.NoAmount)]
    [InlineData(-2, 0, ScrollAmount.SmallDecrement)]
    [InlineData(-2, 1, ScrollAmount.SmallDecrement)]
    [InlineData(-2, 2, ScrollAmount.NoAmount)]
    public void Get_maps_steps_and_iteration(
        int steps,
        int iteration,
        ScrollAmount expected)
        => Assert.Equal(expected, ScrollAmountResolver.Get(steps, iteration));
}
