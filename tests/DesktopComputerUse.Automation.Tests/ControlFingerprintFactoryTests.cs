using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ControlFingerprintFactoryTests
{
    [Fact]
    public void Create_extracts_normalized_stable_evidence()
    {
        var control = SnapshotFixtures.Control(
            "customer",
            name: "txtCustomerName",
            className: "TextBox",
            supportedPatterns: ["Value", "Text", "Value"],
            nearbyLabels:
            [
                new NearbyLabel("label-1", "Customer Name", "left", 4, 0.9),
                new NearbyLabel("label-2", "Customer", "above", 8, 0.7)
            ],
            treePath: ["MainWindow", "Customer Details", "txtCustomerName"],
            relativeBounds: new RectangleInfo(0.7, 0.4, 0.2, 0.05));

        var fingerprint = ControlFingerprintFactory.Create(control, "2.4.1");

        Assert.Equal("Edit", fingerprint.ControlType);
        Assert.Equal(["Text", "Value", "Value"], fingerprint.SupportedPatterns);
        Assert.Equal("TextBox", fingerprint.ClassName);
        Assert.Equal("WinForm", fingerprint.FrameworkId);
        Assert.Equal(["customer", "name"], fingerprint.NameTokens);
        Assert.Equal(["customer", "name"], fingerprint.NearbyLabelTokens);
        Assert.Equal(["customer", "details", "main", "window"], fingerprint.AncestorTokens);
        Assert.Equal("middle-right", fingerprint.RelativeRegion);
        Assert.Equal(0.2, fingerprint.RelativeWidth);
        Assert.Equal(0.05, fingerprint.RelativeHeight);
        Assert.Equal("2.4.1", fingerprint.ApplicationVersion);
    }

    [Theory]
    [InlineData(0.32, 0.32, "top-left")]
    [InlineData(0.33, 0.33, "middle-center")]
    [InlineData(0.66, 0.66, "bottom-right")]
    public void Create_classifies_relative_region_boundaries(
        double x,
        double y,
        string expectedRegion)
    {
        var control = SnapshotFixtures.Control(
            "control",
            relativeBounds: new RectangleInfo(x, y, 0.1, 0.1));

        var fingerprint = ControlFingerprintFactory.Create(control);

        Assert.Equal(expectedRegion, fingerprint.RelativeRegion);
    }
}
