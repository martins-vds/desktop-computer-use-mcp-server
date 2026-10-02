using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Tests;

public sealed class PrivacyModeTests
{
    [Fact]
    public void Privacy_is_enabled_by_default_in_profiles_and_result_contracts()
    {
        var profile = JsonSerializer.Deserialize<ApplicationProfile>(
            """{"Id":"test","DisplayName":"Test","ExecutablePath":"app.exe"}""")!;
        Assert.True(profile.PrivacyMode);
        Assert.True(new ApplicationProfileSummary("test", "Test", "app.exe", default, false).PrivacyMode);
        Assert.True(new ApplicationState("test", 1, "app.exe", false, false, null, default).PrivacyMode);
        Assert.True(new WindowCapture("image/png", "", 1, 1).PrivacyMode);
        Assert.True(new NativeCaptureOptions().PrivacyMode);
        Assert.True(new ControlValueResult(null, true).PrivacyMode);
        Assert.Empty(new ControlValueResult(null, true).Failures);
        var summary = new ControlSummary(null, null, "Edit", null, true, false, new(0, 0, 1, 1), null, true, []);
        Assert.True(summary.PrivacyMode);
        Assert.True(SnapshotFixtures.Application([]).PrivacyMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_privacy_mode_round_trips_and_is_visible_in_profile_listing(bool enabled)
    {
        var profile = TestProfile.Create() with { PrivacyMode = enabled };
        var roundTrip = JsonSerializer.Deserialize<ApplicationProfile>(JsonSerializer.Serialize(profile))!;
        Assert.Equal(enabled, roundTrip.PrivacyMode);
        var store = new ApplicationProfileStore([roundTrip]);
        Assert.Equal(enabled, Assert.Single(store.List()).PrivacyMode);
        Assert.True(store.TryGet(profile.Id, out var loaded));
        Assert.Equal(enabled, loaded.PrivacyMode);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("configured")]
    [InlineData("failedId")]
    [InlineData("failedPassword")]
    public void Disabled_privacy_reads_values_even_when_sensitivity_is_true_or_unavailable(string scenario)
    {
        var source = new ControlObservationSource(
            () => scenario == "failedId" ? throw new COMException() : "secret",
            () => scenario == "failedPassword" ? throw new COMException() : scenario == "password",
            () => "Field", () => "Edit", () => "TextBox", () => true, () => false,
            () => new(0, 0, 100, 30), () => "unredacted",
            new Dictionary<string, Func<bool>> { ["Value"] = () => true });
        var profile = TestProfile.Create() with { PrivacyMode = false, SensitiveAutomationIds = ["SECRET"] };
        var summary = ControlObserver.Observe(source, profile,
            new SafeAutomationElementReader(new AutomationDiagnostic()));
        Assert.False(summary.PrivacyMode);
        Assert.False(summary.IsValueRedacted);
        Assert.Equal("unredacted", summary.Value);
        Assert.Equal("unredacted", ApplicationSnapshotBuilder.SnapshotValue(summary));
        Assert.Equal(scenario is "password" or "failedPassword", summary.IsPassword);
        Assert.Equal(scenario.StartsWith("failed", StringComparison.Ordinal) ? 1 : 0, summary.Failures.Count);
        Assert.Null(ApplicationSnapshotBuilder.SnapshotValue(summary with { IsValueRedacted = true }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capture_policy_only_traverses_sensitivity_when_privacy_is_enabled(bool enabled)
    {
        var reads = 0;
        var profile = TestProfile.Create() with { EnableScreenshots = true, PrivacyMode = enabled };
        var regions = CapturePrivacy.ReadWithPolicy(profile, () =>
        {
            reads++;
            return [new RectangleInfo(-10, 20, 30, 40)];
        }, CancellationToken.None);
        Assert.Equal(enabled ? 1 : 0, reads);
        Assert.Equal(enabled ? 1 : 0, regions.Count);
        if (enabled)
            Assert.Equal(new RectangleInfo(-10, 20, 30, 40), Assert.Single(regions));
    }

    [Fact]
    public void Disabled_privacy_capture_bypasses_unreadable_UIA_but_keeps_permission_and_cancellation()
    {
        var profile = TestProfile.Create() with { PrivacyMode = false, EnableScreenshots = true };
        Assert.Empty(CapturePrivacy.ReadWithPolicy(profile,
            () => throw new COMException("must not traverse UIA"), CancellationToken.None));
        var denied = Assert.Throws<AutomationOperationException>(() => CapturePrivacy.ReadWithPolicy(
            profile with { EnableScreenshots = false }, () => [], CancellationToken.None));
        Assert.Equal(AutomationErrorCode.ApplicationNotAllowed, denied.Code);
        Assert.Equal("Image capture is disabled by the application profile.", denied.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            CapturePrivacy.ReadWithPolicy(profile, () => [], cancellation.Token));
        Assert.Throws<COMException>(() => CapturePrivacy.ReadWithPolicy(
            profile with { PrivacyMode = true }, () => throw new COMException(), CancellationToken.None));
    }

    [Fact]
    public void Reloaded_privacy_policy_does_not_mutate_the_active_profile()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "test.application.json");
        var profile = TestProfile.Create();
        File.WriteAllText(path, JsonSerializer.Serialize(profile));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.True(store.TryGet(profile.Id, out var active));
        File.WriteAllText(path, JsonSerializer.Serialize(profile with { PrivacyMode = false }));
        Assert.True(store.Reload().Succeeded);
        Assert.True(active.PrivacyMode);
        Assert.True(store.IsStale(active));
        Assert.False(Assert.Single(store.List()).PrivacyMode);
        Assert.True(store.TryGet(profile.Id, out var next));
        Assert.False(next.PrivacyMode);
        Assert.NotEqual(active.Metadata?.Revision, next.Metadata?.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreground_activation_profile_is_mapped_without_disabling_safeguards(bool activate)
    {
        var policy = DesktopAutomationController.NativePolicy(new NativeInputPolicy
        {
            Enabled = true, AllowKeyboard = true, ActivateBeforeInput = activate
        });
        Assert.Equal(activate, policy.AllowActivate);
        Assert.True(policy.RequireForeground);
        Assert.True(policy.AllowKeyboard);
        Assert.False(policy.AllowRestore);
    }
}
