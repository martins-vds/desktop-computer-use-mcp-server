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

public sealed class LaunchOptionValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("preserveSpawned")]
    [InlineData("unknown")]
    [InlineData("TerminateSpawned")]
    public async Task Unsupported_failure_policy_is_rejected_before_profile_lookup(string? value)
    {
        var store = new EmptyProfileStore();
        await using var controller = CreateController(store);
        var result = await controller.LaunchAsync("missing", "fail", value!, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAllowed, result.Error!.Code);
        Assert.Contains("onLaunchFailure", result.Error.Message);
        Assert.Contains("terminateSpawned", result.Error.Message);
        Assert.Equal(0, store.LookupCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("Fail")]
    [InlineData("launchnew")]
    public async Task Unsupported_existing_process_policy_is_rejected_before_profile_lookup(string? value)
    {
        var store = new EmptyProfileStore();
        await using var controller = CreateController(store);
        var result = await controller.LaunchAsync("missing", value!, "terminateSpawned", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AutomationErrorCode.ApplicationNotAllowed, result.Error!.Code);
        Assert.Contains("ifAlreadyRunning", result.Error.Message);
        Assert.Contains("fail, attach, or launchNew", result.Error.Message);
        Assert.Equal(0, store.LookupCount);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("attach")]
    [InlineData("launchNew")]
    public async Task Supported_string_options_delegate_to_existing_typed_launch(string value)
    {
        var store = new EmptyProfileStore();
        await using var controller = CreateController(store);
        var result = await controller.LaunchAsync("missing", value, "terminateSpawned", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AutomationErrorCode.ProfileNotFound, result.Error!.Code);
        Assert.Equal(1, store.LookupCount);
    }

    private static DesktopAutomationController CreateController(IApplicationProfileStore store)
    {
        var observer = new ControlObserver();
        return new DesktopAutomationController(
            store,
            new MtaAutomationWorker(),
            new FlaUiAutomationFactory(),
            new ControlSelectorResolver(),
            observer,
            new ApplicationSnapshotBuilder(observer),
            new FuzzyControlResolver(),
            NullLogger<DesktopAutomationController>.Instance);
    }

    private sealed class EmptyProfileStore : IApplicationProfileStore
    {
        public int LookupCount { get; private set; }
        public IReadOnlyList<ApplicationProfileSummary> List() => [];

        public bool TryGet(string profileId, out ApplicationProfile profile)
        {
            LookupCount++;
            profile = null!;
            return false;
        }
    }
}
