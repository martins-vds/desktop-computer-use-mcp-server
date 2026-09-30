using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class NearbyLabelGeometryTests
{
    [Fact]
    public void Create_prefers_left_label_with_vertical_overlap()
    {
        var result = NearbyLabelGeometry.Create(
            "label",
            "Customer name",
            new RectangleInfo(10, 100, 80, 20),
            new RectangleInfo(100, 95, 200, 30));

        Assert.NotNull(result);
        Assert.Equal("left", result.Relation);
        Assert.Equal(10, result.Distance);
        Assert.Equal(0.9 - 10d / 600d, result.Confidence, 10);
    }

    [Fact]
    public void Create_uses_above_label_when_not_left()
    {
        var result = NearbyLabelGeometry.Create(
            "label",
            "Customer name",
            new RectangleInfo(100, 60, 100, 20),
            new RectangleInfo(100, 100, 200, 30));

        Assert.NotNull(result);
        Assert.Equal("above", result.Relation);
        Assert.Equal(20, result.Distance);
        Assert.Equal(0.8 - 20d / 400d, result.Confidence, 10);
    }

    [Theory]
    [InlineData(500, 100, 40, 20)]
    [InlineData(0, 0, 20, 20)]
    [InlineData(100, 110, 50, 10)]
    public void Create_rejects_unrelated_or_overlapping_labels(
        double x,
        double y,
        double width,
        double height)
        => Assert.Null(NearbyLabelGeometry.Create(
            "label",
            "Unrelated",
            new RectangleInfo(x, y, width, height),
            new RectangleInfo(100, 100, 200, 30)));

    [Fact]
    public void Create_accepts_left_boundary_and_uses_width_based_distance()
    {
        var result = NearbyLabelGeometry.Create(
            "label",
            "Boundary",
            new RectangleInfo(-500, 100, 600, 10.5),
            new RectangleInfo(100, 100, 200, 30));

        Assert.NotNull(result);
        Assert.Equal("left", result.Relation);
        Assert.Equal(0, result.Distance);
    }

    [Fact]
    public void Create_accepts_distance_between_fixed_and_scaled_left_limits()
    {
        var result = NearbyLabelGeometry.Create(
            "label",
            "Far left",
            new RectangleInfo(-400, 100, 100, 30),
            new RectangleInfo(100, 100, 200, 30));

        Assert.NotNull(result);
        Assert.Equal(400, result.Distance);
    }

    [Fact]
    public void Create_accepts_distance_between_fixed_and_scaled_above_limits()
    {
        var result = NearbyLabelGeometry.Create(
            "label",
            "Far above",
            new RectangleInfo(100, -100, 200, 20),
            new RectangleInfo(100, 100, 200, 50));

        Assert.NotNull(result);
        Assert.Equal("above", result.Relation);
        Assert.Equal(180, result.Distance);
    }
}
