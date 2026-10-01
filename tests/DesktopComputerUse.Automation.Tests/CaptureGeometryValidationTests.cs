using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class CaptureGeometryValidationTests
{
    private static NativeWindowGeometry Geometry => new(
        new NativeWindowTarget(10, 20), new PhysicalScreenRect(-100, 20, 300, 200),
        null, new PhysicalScreenRect(-90, 40, 280, 170), 144, true, false, false, true);

    [Fact]
    public void Unchanged_geometry_is_allowed()
        => DesktopAutomationController.RequireCaptureGeometry(Geometry, Geometry);

    [Fact]
    public void Window_movement_invalidates_redaction_coordinates()
        => AssertStale(Geometry with { WindowBounds = new(-99, 20, 300, 200) });

    [Fact]
    public void Client_geometry_change_invalidates_capture()
        => AssertStale(Geometry with { ClientBounds = new(-90, 41, 280, 170) });

    [Fact]
    public void Dpi_change_invalidates_capture()
        => AssertStale(Geometry with { Dpi = 96 });

    [Fact]
    public void Matching_uia_bounds_are_not_warned()
    {
        var bounds = new RectangleInfo(-100, 20, 300, 200);
        var result = DesktopAutomationController.AddUiaBounds(Geometry, bounds);
        Assert.Equal(bounds, result.UiaBounds);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(-99, 20, 300, 200)]
    [InlineData(-100, 21, 300, 200)]
    [InlineData(-100, 20, 299, 200)]
    [InlineData(-100, 20, 300, 199)]
    public void Every_uia_discrepancy_is_reported(int x, int y, int width, int height)
        => Assert.Single(DesktopAutomationController.AddUiaBounds(
            Geometry, new RectangleInfo(x, y, width, height)).Warnings);

    [Theory]
    [InlineData("left", NativeMouseButton.Left)]
    [InlineData("right", NativeMouseButton.Right)]
    [InlineData("middle", NativeMouseButton.Middle)]
    public void Mouse_button_parser_uses_only_allowlisted_values(string button, NativeMouseButton expected)
        => Assert.Equal(expected, DesktopAutomationController.ParseMouseButton(button));

    [Theory]
    [InlineData("LEFT")]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void Invalid_mouse_buttons_are_rejected(string? button)
        => Assert.Equal(NativeFailureCode.InvalidArgument,
            Assert.Throws<NativeOperationException>(() =>
                DesktopAutomationController.ParseMouseButton(button)).Code);

    [Fact]
    public void Control_crop_uses_physical_source_offset()
        => Assert.Equal(new PhysicalScreenRect(10, 20, 30, 40),
            DesktopAutomationController.MapControlCrop(new(-90, 40, 30, 40),
                new CapturePixelTransform(new(-100, 20, 300, 200), 300, 200)));

    [Theory]
    [InlineData(-101, 40, 30, 40)]
    [InlineData(-90, 40, 0, 40)]
    public void Invalid_crop_fails_without_returning_a_different_region(int x, int y, int width, int height)
    {
        var exception = Assert.Throws<NativeOperationException>(() =>
            DesktopAutomationController.MapControlCrop(new(x, y, width, height),
                new CapturePixelTransform(new(-100, 20, 300, 200), 300, 200)));
        Assert.Equal(NativeFailureCode.CaptureFailed, exception.Code);
        Assert.Equal("validateCrop", exception.Phase);
    }

    private static void AssertStale(NativeWindowGeometry actual)
    {
        var exception = Assert.Throws<NativeOperationException>(() =>
            DesktopAutomationController.RequireCaptureGeometry(Geometry, actual));
        Assert.Equal(NativeFailureCode.StaleCapture, exception.Code);
        Assert.Equal("verifyGeometry", exception.Phase);
    }
}
