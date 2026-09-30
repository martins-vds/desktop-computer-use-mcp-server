using FlaUI.Core.Definitions;

namespace DesktopComputerUse.Automation.Resolution;

public static class ScrollAmountResolver
{
    public static ScrollAmount Get(int steps, int iteration)
    {
        if (steps == 0 || iteration >= Math.Abs(steps))
        {
            return ScrollAmount.NoAmount;
        }

        return steps > 0
            ? ScrollAmount.SmallIncrement
            : ScrollAmount.SmallDecrement;
    }
}
