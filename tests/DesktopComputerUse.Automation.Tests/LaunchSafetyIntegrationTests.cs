using System.Diagnostics;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DesktopComputerUse.Automation.Tests;

public sealed class LaunchSafetyIntegrationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Automation_factory_failure_terminates_spawned_process_and_reports_pid()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var profile = SleepingCommandProfile() with { Backend = (AutomationBackend)999 };
        await using var controller = CreateController(profile);
        var result = await controller.LaunchAsync(profile.Id, LaunchPolicy.LaunchNew, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AutomationErrorCode.InvalidProfile, result.Error!.Code);
        Assert.NotNull(result.Error.Diagnostic!.ProcessId);
        Assert.Contains(result.Error.Diagnostic.CleanupOutcome, new[] { "alreadyExited", "closed", "killed" });
        AssertProcessExited(result.Error.Diagnostic.ProcessId!.Value);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Failed_attach_never_terminates_preexisting_process()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var profile = SleepingCommandProfile() with { Backend = (AutomationBackend)999 };
        using var process = Process.Start(new ProcessStartInfo(profile.ExecutablePath, profile.Arguments!)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        try
        {
            await using var controller = CreateController(profile);
            var result = await controller.AttachAsync(profile.Id, process.Id, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("notOwned", result.Error!.Diagnostic!.CleanupOutcome);
            Assert.False(process.HasExited);
        }
        finally
        {
            var cleanup = ProcessLifecycle.CleanupOwnedProcess(process, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            Assert.True(cleanup.Succeeded, cleanup.Outcome);
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Missing_main_window_terminates_spawned_process()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return;

        var profile = SleepingCommandProfile() with
        {
            MainWindow = new WindowSelector { Title = Guid.NewGuid().ToString() },
            OperationTimeoutMs = 500,
            PollIntervalMs = 25
        };
        await using var controller = CreateController(profile);
        var result = await controller.LaunchAsync(profile.Id, LaunchPolicy.LaunchNew, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error!.Diagnostic!.ProcessId);
        Assert.Contains(result.Error.Diagnostic.CleanupOutcome, new[] { "alreadyExited", "closed", "killed" });
        AssertProcessExited(result.Error.Diagnostic.ProcessId!.Value);
    }

    private static ApplicationProfile SleepingCommandProfile()
        => TestProfile.Create("launch-safety", Path.Combine(Environment.SystemDirectory, "cmd.exe")) with
        {
            Arguments = "/c ping -n 30 127.0.0.1 >nul",
            AllowMultipleInstances = true
        };

    private static DesktopAutomationController CreateController(ApplicationProfile profile)
    {
        var observer = new ControlObserver();
        return new DesktopAutomationController(
            new UnvalidatedProfileStore(profile),
            new MtaAutomationWorker(),
            new FlaUiAutomationFactory(),
            new ControlSelectorResolver(),
            observer,
            new ApplicationSnapshotBuilder(observer),
            new FuzzyControlResolver(),
            NullLogger<DesktopAutomationController>.Instance);
    }

    private static void AssertProcessExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException)
        {
            // Windows has already removed the terminated process.
        }
    }

    private sealed class UnvalidatedProfileStore(ApplicationProfile profile) : IApplicationProfileStore
    {
        public IReadOnlyList<ApplicationProfileSummary> List() => [];

        public bool TryGet(string profileId, out ApplicationProfile result)
        {
            result = profile;
            return profileId == profile.Id;
        }
    }
}
