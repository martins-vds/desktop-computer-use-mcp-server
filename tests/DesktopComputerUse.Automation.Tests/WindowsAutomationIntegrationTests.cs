using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Automation.Windows;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;
using Microsoft.Extensions.Logging.Abstractions;

namespace DesktopComputerUse.Automation.Tests;

[Collection("InteractiveWindows")]
public sealed class WindowsAutomationIntegrationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    [Trait("PrivacyMode", "Enabled")]
    public async Task TestApp_supports_launch_find_set_invoke_wait_and_detach()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            return;
        }

        var executablePath = FindTestAppExecutable();
        if (executablePath is null)
        {
            return;
        }

        Win32DesktopApi.InitializePerMonitorV2();

        var profile = TestProfile.Create("test-app", executablePath) with
        {
            MainWindow = new WindowSelector { Title = "Desktop Computer Use Test App" },
            OperationTimeoutMs = 10_000,
            PollIntervalMs = 50,
            EnableScreenshots = true,
            SensitiveAutomationIds = new HashSet<string>(
                ["CustomerPasswordTextBox"],
                StringComparer.OrdinalIgnoreCase),
            SemanticSelectors = new Dictionary<string, ControlSelector>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["customer-name"] = new() { AutomationId = "CustomerNameTextBox" },
                ["save"] = new() { AutomationId = "SaveButton" }
            },
            SchemaVersion = 2,
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["save"] = new()
                {
                    Intent = "save",
                    ExpectedControlTypes = ["Button"],
                    RequiredPatterns = ["Invoke"],
                    Strategies =
                    [
                        new SelectorStrategy
                        {
                            AutomationId = "SaveButton",
                            ControlType = "Button"
                        }
                    ]
                }
            }
        };

        var observer = new ControlObserver();
        await using var controller = new DesktopAutomationController(
            new ApplicationProfileStore([profile]),
            new MtaAutomationWorker(),
            new FlaUiAutomationFactory(),
            new ControlSelectorResolver(),
            observer,
            new ApplicationSnapshotBuilder(observer),
            new FuzzyControlResolver(),
            NullLogger<DesktopAutomationController>.Instance);

        var launched = false;
        try
        {
            var launch = await controller.LaunchAsync(profile.Id, CancellationToken.None);
            Assert.True(launch.Succeeded, launch.Error?.Message);
            launched = true;

            var field = await controller.FindControlAsync(
                new ControlSelector { SemanticKey = "customer-name" },
                CancellationToken.None);
            Assert.True(field.Succeeded, field.Error?.Message);

            var snapshot = await controller.SnapshotApplicationAsync(
                maxDepth: 5,
                maxResults: 200,
                CancellationToken.None);
            Assert.True(snapshot.Succeeded, snapshot.Error?.Message);
            Assert.NotNull(snapshot.Value);
            Assert.Contains(
                snapshot.Value.Window.Controls,
                control => control.AutomationId == "SaveButton");
            var password = Assert.Single(
                snapshot.Value.Window.Controls,
                control => control.AutomationId == "CustomerPasswordTextBox");
            Assert.True(password.IsPassword);
            Assert.True(password.IsValueRedacted);
            Assert.Null(password.Value);

            var fieldCapture = await controller.CaptureControlImageAsync(
                new ControlSelector { SemanticKey = "customer-name" },
                CancellationToken.None);
            Assert.True(fieldCapture.Succeeded, fieldCapture.Error?.Message);
            Assert.NotEmpty(fieldCapture.Value!.Base64Data);

            var passwordCapture = await controller.CaptureControlImageAsync(
                new ControlSelector { AutomationId = "CustomerPasswordTextBox" },
                CancellationToken.None);
            Assert.False(passwordCapture.Succeeded);
            Assert.Equal(
                AutomationErrorCode.ApplicationNotAllowed,
                passwordCapture.Error?.Code);

            var resolution = await controller.ResolveControlIntentAsync(
                "save",
                maximumCandidates: 10,
                CancellationToken.None);
            Assert.True(resolution.Succeeded, resolution.Error?.Message);
            Assert.Equal(ResolutionStatus.Resolved, resolution.Value?.Status);
            Assert.Equal(
                "SaveButton",
                resolution.Value?.Candidates
                    .Single(candidate =>
                        candidate.CandidateId == resolution.Value.SelectedCandidateId)
                    .Candidate.AutomationId);

            var set = await controller.SetValueAsync(
                new ControlSelector { SemanticKey = "customer-name" },
                "Integration User",
                CancellationToken.None);
            Assert.True(set.Succeeded, set.Error?.Message);

            var invoke = await controller.InvokeAsync(
                new ControlSelector { SemanticKey = "save" },
                CancellationToken.None);
            Assert.True(invoke.Succeeded, invoke.Error?.Message);

            var wait = await controller.WaitForStateAsync(
                new ControlSelector { SemanticKey = "customer-name" },
                new WaitCondition(
                    WaitProperty.Value,
                    WaitComparison.Equals,
                    "Integration User"),
                timeoutMs: 5_000,
                CancellationToken.None);
            Assert.True(wait.Succeeded, wait.Error?.Message);
        }
        finally
        {
            if (launched)
            {
                var detach = await controller.DetachAsync(
                    terminateOwnedProcess: true,
                    CancellationToken.None);
                Assert.True(detach.Succeeded, detach.Error?.Message);
            }
        }
    }

    [InteractiveWindowsFact]
    [Trait("Category", "WindowsIntegration")]
    [Trait("PrivacyMode", "Disabled")]
    public async Task TestApp_supports_sensitive_reads_writes_and_hwnd_capture_with_privacy_disabled()
    {
        Win32DesktopApi.InitializePerMonitorV2();
        var executable = Environment.GetEnvironmentVariable("DESKTOP_COMPUTER_USE_TEST_APP")!;
        var profile = TestProfile.Create("privacy-disabled-test", executable) with
        {
            MainWindow = new WindowSelector { Title = "Desktop Computer Use Test App" },
            PrivacyMode = false,
            EnableScreenshots = true,
            SensitiveAutomationIds = ["CustomerNameTextBox", "CustomerPasswordTextBox"]
        };
        var observer = new ControlObserver();
        await using var controller = new DesktopAutomationController(
            new ApplicationProfileStore([profile]), new MtaAutomationWorker(),
            new FlaUiAutomationFactory(), new ControlSelectorResolver(), observer,
            new ApplicationSnapshotBuilder(observer), new FuzzyControlResolver(),
            NullLogger<DesktopAutomationController>.Instance);
        var launch = await controller.LaunchAsync(profile.Id, CancellationToken.None);
        Assert.True(launch.Succeeded, launch.Error?.Message);
        Assert.False(launch.Value!.PrivacyMode);
        try
        {
            var selector = new ControlSelector { AutomationId = "CustomerNameTextBox" };
            const string value = "Synthetic privacy-off test value";
            var set = await controller.SetValueAsync(selector, value, CancellationToken.None);
            Assert.True(set.Succeeded, set.Error?.Message);
            var read = await controller.GetControlValueAsync(selector, CancellationToken.None);
            Assert.True(read.Succeeded, read.Error?.Message);
            Assert.False(read.Value!.PrivacyMode);
            Assert.False(read.Value.IsValueRedacted);
            Assert.Equal(value, read.Value.Value);

            var controlCapture = await controller.CaptureControlImageAsync(selector, CancellationToken.None);
            AssertUnredactedCapture(controlCapture);
            var passwordCapture = await controller.CaptureControlImageAsync(
                new ControlSelector { AutomationId = "CustomerPasswordTextBox" }, CancellationToken.None);
            AssertUnredactedCapture(passwordCapture);
            var windowCapture = await controller.CaptureApplicationWindowAsync(CancellationToken.None);
            AssertUnredactedCapture(windowCapture);
            Assert.NotNull(windowCapture.Value!.Token);
            Assert.Equal(launch.Value.ProcessId, windowCapture.Value.Token!.Target.ProcessId);
        }
        finally
        {
            var detach = await controller.DetachAsync(true, CancellationToken.None);
            Assert.True(detach.Succeeded, detach.Error?.Message);
        }
    }

    private static void AssertUnredactedCapture(AutomationResult<WindowCapture> result)
    {
        Assert.True(result.Succeeded, result.Error?.Message);
        var capture = Assert.IsType<WindowCapture>(result.Value);
        Assert.False(capture.PrivacyMode);
        Assert.Equal(0, capture.RedactedControlCount);
        Assert.True(capture.OcclusionSafe);
        Assert.Equal("image/png", capture.MimeType);
        Assert.NotEmpty(Convert.FromBase64String(capture.Base64Data));
        Assert.True(capture.Width > 0);
        Assert.True(capture.Height > 0);
    }

    private static string? FindTestAppExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(
            "DESKTOP_COMPUTER_USE_TEST_APP");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tests",
                "DesktopComputerUse.TestApp",
                "bin",
                "Debug",
                "net10.0-windows",
                "DesktopComputerUse.TestApp.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
