using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Automation.Windows;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DesktopComputerUse.Automation.Tests;

[CollectionDefinition("InteractiveWindows", DisableParallelization = true)]
public sealed class InteractiveWindowsCollection { }

public sealed class InteractiveWindowsFactAttribute : FactAttribute
{
    public InteractiveWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            Skip = "Requires an interactive Windows desktop.";
        else if (Environment.GetEnvironmentVariable("DESKTOP_COMPUTER_USE_INTERACTIVE_TESTS") != "1")
            Skip = "Set DESKTOP_COMPUTER_USE_INTERACTIVE_TESTS=1 to allow input into the disposable test app.";
        else if (!File.Exists(Environment.GetEnvironmentVariable("DESKTOP_COMPUTER_USE_TEST_APP")))
            Skip = "Set DESKTOP_COMPUTER_USE_TEST_APP to the built WinForms test executable.";
    }
}

[Collection("InteractiveWindows")]
public sealed class WindowsNativeIntegrationTests
{
    [InteractiveWindowsFact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Native_geometry_input_and_hwnd_capture_operate_only_on_owned_fixture()
    {
        Win32DesktopApi.InitializePerMonitorV2();
        var executable = Environment.GetEnvironmentVariable("DESKTOP_COMPUTER_USE_TEST_APP")!;
        var profile = TestProfile.Create("native-test", executable) with
        {
            MainWindow = new WindowSelector { Title = "Desktop Computer Use Test App" },
            EnableScreenshots = true,
            NativeInput = new NativeInputPolicy { Enabled = true, AllowKeyboard = true },
            SensitiveAutomationIds = ["CustomerPasswordTextBox"]
        };
        var observer = new ControlObserver();
        await using var controller = new DesktopAutomationController(
            new ApplicationProfileStore([profile]), new MtaAutomationWorker(),
            new FlaUiAutomationFactory(), new ControlSelectorResolver(), observer,
            new ApplicationSnapshotBuilder(observer), new FuzzyControlResolver(),
            NullLogger<DesktopAutomationController>.Instance);
        var launch = await controller.LaunchAsync(profile.Id, CancellationToken.None);
        Assert.True(launch.Succeeded, launch.Error?.Message);
        try
        {
            await ExerciseNativeTools(controller);
        }
        finally
        {
            var detached = await controller.DetachAsync(true, CancellationToken.None);
            Assert.True(detached.Succeeded, detached.Error?.Message);
        }
    }

    private static async Task ExerciseNativeTools(DesktopAutomationController controller)
    {
        var activated = await controller.ActivateWindowAsync(true, CancellationToken.None);
        Assert.True(activated.Succeeded, activated.Error?.Message);
        Assert.True(activated.Value!.Geometry.IsForeground);
        var geometry = await controller.GetWindowGeometryAsync(CancellationToken.None);
        Assert.True(geometry.Succeeded, geometry.Error?.Message);
        Assert.True(geometry.Value!.WindowBounds.IsNonEmpty);
        var root = await controller.InspectControlsAsync(null, 1, CancellationToken.None);
        Assert.True(root.Succeeded, root.Error?.Message);
        var native = geometry.Value.WindowBounds;
        Assert.Equal(new RectangleInfo(native.X, native.Y, native.Width, native.Height), root.Value!.Control.Bounds);
        var selector = new ControlSelector { AutomationId = "CustomerNameTextBox" };
        var cleared = await controller.SetValueAsync(selector, "", CancellationToken.None);
        Assert.True(cleared.Succeeded, cleared.Error?.Message);
        var control = await controller.FindControlAsync(selector, CancellationToken.None);
        Assert.True(control.Succeeded, control.Error?.Message);
        var bounds = control.Value!.Bounds;
        var click = await controller.ClickAtPointAsync(
            checked((int)(bounds.X + bounds.Width / 2)), checked((int)(bounds.Y + bounds.Height / 2)),
            "physicalVirtualScreen", "left", CancellationToken.None);
        Assert.True(click.Succeeded, click.Error?.Message);
        var typed = await controller.TypeTextAsync("Native \u03BB", CancellationToken.None);
        Assert.True(typed.Succeeded, typed.Error?.Message);
        var observed = await controller.FindControlAsync(selector, CancellationToken.None);
        Assert.Equal("Native \u03BB", observed.Value?.Value);
        var capture = await controller.CaptureApplicationWindowAsync(CancellationToken.None);
        Assert.True(capture.Succeeded, capture.Error?.Message);
        Assert.True(capture.Value!.OcclusionSafe);
        Assert.NotNull(capture.Value.Token);
        Assert.True(capture.Value.RedactedControlCount >= 1);
        Assert.Equal(geometry.Value.Target.WindowHandle, capture.Value.Token!.Target.WindowHandle);
    }
}
