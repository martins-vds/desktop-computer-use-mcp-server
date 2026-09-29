using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DesktopComputerUse.Automation.Tests;

public sealed class WindowsAutomationIntegrationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
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

        var profile = TestProfile.Create("test-app", executablePath) with
        {
            MainWindow = new WindowSelector { Title = "Desktop Computer Use Test App" },
            OperationTimeoutMs = 10_000,
            PollIntervalMs = 50,
            SemanticSelectors = new Dictionary<string, ControlSelector>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["customer-name"] = new() { AutomationId = "CustomerNameTextBox" },
                ["save"] = new() { AutomationId = "SaveButton" }
            }
        };

        await using var controller = new DesktopAutomationController(
            new ApplicationProfileStore([profile]),
            new MtaAutomationWorker(),
            new FlaUiAutomationFactory(),
            new ControlSelectorResolver(),
            new ControlObserver(),
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
                "net8.0-windows",
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
