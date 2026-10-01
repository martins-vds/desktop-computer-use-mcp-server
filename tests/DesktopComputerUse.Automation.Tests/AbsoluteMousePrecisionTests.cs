using DesktopComputerUse.Automation.Windows;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class AbsoluteMousePrecisionTests
{
    [Theory]
    [InlineData(3840)]
    [InlineData(40000)]
    [InlineData(65535)]
    [InlineData(65536)]
    public void Every_supported_pixel_round_trips_through_SendInput_bins(int width)
    {
        var desktop = new PhysicalScreenRect(-20000, -20, width, width);
        for (var x = 0; x < width; x++)
        {
            var absolute = NativeGeometryMath.ToAbsoluteMouse(new(x - 20000, x - 20), desktop);
            Assert.Equal(x, absolute.X * (long)width / 65536);
            Assert.Equal(x, absolute.Y * (long)width / 65536);
            Assert.InRange(absolute.X, 0, 65535);
        }
    }
}
