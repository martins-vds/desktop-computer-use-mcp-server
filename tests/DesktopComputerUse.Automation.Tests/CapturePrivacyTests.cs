using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Tests;

public sealed class CapturePrivacyTests
{
    [Fact]
    public void Fractional_sensitive_bounds_round_outward_including_negative_origins()
        => Assert.Equal(new PhysicalScreenRect(-11, 20, 3, 3),
            CapturePrivacy.ToPhysicalRegion(new RectangleInfo(-10.2, 20.3, 2, 2)));

    [Fact]
    public void Integer_sensitive_bounds_are_unchanged()
        => Assert.Equal(new PhysicalScreenRect(-10, 20, 2, 3),
            CapturePrivacy.ToPhysicalRegion(new RectangleInfo(-10, 20, 2, 3)));

    [Theory]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1, 1)]
    [InlineData(1, 1, double.PositiveInfinity, 1)]
    [InlineData(1, 1, 1, double.PositiveInfinity)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(-2147483648.5, 0, 1, 1)]
    [InlineData(0, -2147483648.5, 1, 1)]
    [InlineData(-2147483648, 0, 4294967295, 1)]
    [InlineData(0, -2147483648, 1, 4294967295)]
    public void Unmappable_sensitive_bounds_fail_closed(double x, double y, double width, double height)
        => Assert.Equal(NativeFailureCode.SensitiveGeometryUnavailable,
            Assert.Throws<NativeOperationException>(() =>
                CapturePrivacy.ToPhysicalRegion(new RectangleInfo(x, y, width, height))).Code);

    [Fact]
    public void Collects_sensitive_root_and_descendants_without_reading_other_bounds()
    {
        var bounds = new RectangleInfo(-10, 20, 100, 30);
        var result = CapturePrivacy.ReadSensitiveBounds(
            0, 3, node => node != 1,
            _ => bounds,
            node => node == 0 ? [1, 2] : Array.Empty<int>(),
            CancellationToken.None);
        Assert.Equal(new[] { bounds, bounds }, result);
    }

    [Fact]
    public void Exact_limit_is_allowed_but_traversal_overflow_fails_closed()
    {
        Assert.Empty(Read(2));
        var exception = Assert.Throws<AutomationOperationException>(() => Read(1));
        Assert.Equal(AutomationErrorCode.ApplicationNotAllowed, exception.Code);
        Assert.Contains("element limit", exception.Message);

        IReadOnlyList<RectangleInfo> Read(int limit)
            => CapturePrivacy.ReadSensitiveBounds(
                0, limit, _ => false, _ => throw new InvalidOperationException(),
                node => node == 0 ? [1] : Array.Empty<int>(), CancellationToken.None);
    }

    [Theory]
    [InlineData("sensitivity")]
    [InlineData("bounds")]
    [InlineData("children")]
    public void Unreadable_provider_data_fails_closed_without_exposing_values(string phase)
    {
        var exception = Assert.Throws<AutomationOperationException>(() =>
            CapturePrivacy.ReadSensitiveBounds(
                0, 2,
                _ => phase == "sensitivity" ? throw new InvalidOperationException("secret") : true,
                _ => phase == "bounds" ? throw new InvalidOperationException("secret") : new RectangleInfo(1, 2, 3, 4),
                _ => phase == "children" ? throw new InvalidOperationException("secret") : Array.Empty<int>(),
                CancellationToken.None));
        Assert.Equal(AutomationErrorCode.ApplicationNotAllowed, exception.Code);
        Assert.Contains("InvalidOperationException", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    [Fact]
    public void Cancellation_stops_discovery()
    {
        Assert.Throws<OperationCanceledException>(() =>
            CapturePrivacy.ReadSensitiveBounds(
                0, 1, _ => false, _ => new RectangleInfo(0, 0, 1, 1),
                _ => Array.Empty<int>(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Provider_cancellation_is_not_disguised_as_redaction_failure()
        => Assert.Throws<OperationCanceledException>(() =>
            CapturePrivacy.ReadSensitiveBounds(
                0, 1, _ => throw new OperationCanceledException(),
                _ => new RectangleInfo(0, 0, 1, 1),
                _ => Array.Empty<int>(), CancellationToken.None));

    [Fact]
    public void Known_automation_failure_is_preserved()
    {
        var expected = new AutomationOperationException(AutomationErrorCode.AccessDenied, "Denied");
        var actual = Assert.Throws<AutomationOperationException>(() =>
            CapturePrivacy.ReadSensitiveBounds(
                0, 1, _ => throw expected,
                _ => new RectangleInfo(0, 0, 1, 1),
                _ => Array.Empty<int>(), CancellationToken.None));
        Assert.Same(expected, actual);
    }
}
