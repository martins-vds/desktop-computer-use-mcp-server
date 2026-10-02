using System.Text.Json;
using DesktopComputerUse.Automation.Tests;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Server.Linux;
using Xunit;

namespace DesktopComputerUse.Server.Linux.Tests;

public sealed class PortableProfileStoreTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Profile_listing_exposes_privacy_mode_and_reload_adopts_only_valid_policy(bool privacy)
    {
        using var directory = new TestDirectory();
        Write(directory, Profile("one") with { PrivacyMode = privacy });
        var store = PortableProfileStore.LoadFromDirectory(directory.Path);
        Assert.Equal(privacy, Assert.Single(store.List()).PrivacyMode);
        Write(directory, Profile("one") with { PrivacyMode = !privacy });
        Assert.True(store.Reload().Succeeded);
        Assert.Equal(!privacy, Assert.Single(store.List()).PrivacyMode);
    }

    [Fact]
    public void Reload_is_atomic_and_publishes_revisions_and_staleness()
    {
        using var directory = new TestDirectory();
        var file = Write(directory, Profile("one"));
        var store = PortableProfileStore.LoadFromDirectory(directory.Path);
        var first = Assert.Single(store.List());
        Assert.False(first.IsStale);
        Assert.Equal(1, first.Generation);
        Assert.Equal(64, first.Revision!.Length);
        Assert.NotNull(first.LoadedAtUtc);
        Assert.NotNull(first.SourceLastWriteTimeUtc);
        Write(directory, Profile("one") with { DisplayName = "Changed" });
        Assert.True(Assert.Single(store.List()).IsStale);
        var reload = LinuxNativeTools.ReloadApplicationProfiles(store);
        Assert.True(reload.Succeeded);
        Assert.Equal(2, store.Generation);
        Assert.NotEqual(first.Revision, Assert.Single(store.List()).Revision);
        Assert.False(Assert.Single(store.List()).IsStale);
        File.WriteAllText(file, "{invalid");
        var invalid = LinuxNativeTools.ReloadApplicationProfiles(store);
        Assert.False(invalid.Succeeded);
        Assert.Equal(AutomationErrorCode.InvalidProfile, invalid.Error!.Code);
        Assert.Equal(2, store.Generation);
        Assert.Single(invalid.Value!.Failures);
        Assert.Equal(invalid.Value, store.LastReloadResult);
        Assert.True(Assert.Single(store.List()).IsStale);
    }

    [Fact]
    public void Missing_directory_retains_previous_registry_and_missing_files_are_stale()
    {
        using var directory = new TestDirectory();
        Write(directory, Profile("one"));
        var store = PortableProfileStore.LoadFromDirectory(directory.Path);
        File.Delete(Path.Combine(directory.Path, "one.application.json"));
        Assert.True(Assert.Single(store.List()).IsStale);
        Directory.Delete(directory.Path);
        var result = store.Reload();
        Assert.False(result.Succeeded);
        Assert.Single(result.Profiles);
    }

    [Fact]
    public void Empty_or_absent_initial_directory_is_an_explicit_empty_registry()
    {
        using var directory = new TestDirectory();
        var store = PortableProfileStore.LoadFromDirectory(Path.Combine(directory.Path, "missing"));
        Assert.Empty(store.List());
        Assert.True(store.Reload().Succeeded);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Duplicate_ids_reject_the_entire_reload()
    {
        using var directory = new TestDirectory();
        var file = Write(directory, Profile("one"));
        var store = PortableProfileStore.LoadFromDirectory(directory.Path);
        File.Copy(file, Path.Combine(directory.Path, "duplicate.application.json"));
        Assert.Throws<ProfileValidationException>(() => PortableProfileStore.LoadFromDirectory(directory.Path));
        Assert.False(store.Reload().Succeeded);
        Assert.Single(store.List());
    }

    [Fact]
    public void Valid_v2_targets_and_legacy_selectors_are_supported()
    {
        using var directory = new TestDirectory();
        Write(directory, Profile("two") with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, ControlSelector>
            {
                ["old"] = new() { Name = "Save", Ancestor = new() { Name = "Form" } }
            },
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["save"] = new()
                {
                    Intent = "Save",
                    ExpectedControlTypes = ["Button"],
                    Strategies = [new SelectorStrategy { Name = "Save", ControlType = "Button" }]
                }
            }
        });
        Assert.Single(PortableProfileStore.LoadFromDirectory(directory.Path).List());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("id")]
    [InlineData("display")]
    [InlineData("executable")]
    [InlineData("backend")]
    [InlineData("timeout")]
    [InlineData("poll")]
    [InlineData("depth")]
    [InlineData("results")]
    [InlineData("window")]
    [InlineData("native")]
    [InlineData("selectors")]
    [InlineData("targets")]
    [InlineData("sensitive")]
    public void Invalid_identity_limits_and_collections_fail_validation(string field)
    {
        using var directory = new TestDirectory();
        var profile = Invalid(Profile("bad"), field);
        Write(directory, profile);
        Assert.Throws<ProfileValidationException>(() => PortableProfileStore.LoadFromDirectory(directory.Path));
    }

    private static ApplicationProfile Invalid(ApplicationProfile profile, string field) => field switch
    {
        "schema" => profile with { SchemaVersion = 3 },
        "id" => profile with { Id = "" },
        "display" => profile with { DisplayName = "" },
        "executable" => profile with { ExecutablePath = "" },
        "backend" => profile with { Backend = (AutomationBackend)99 },
        "timeout" => profile with { OperationTimeoutMs = 0 },
        "poll" => profile with { PollIntervalMs = 0 },
        "depth" => profile with { MaxTreeDepth = 0 },
        "results" => profile with { MaxResults = 0 },
        "window" => profile with { MainWindow = null! },
        "native" => profile with { NativeInput = null! },
        "selectors" => profile with { SemanticSelectors = null! },
        "targets" => profile with { SemanticTargets = null! },
        "sensitive" => profile with { SensitiveAutomationIds = null! },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    [Theory]
    [InlineData("emptySelector")]
    [InlineData("invalidType")]
    [InlineData("index")]
    [InlineData("nullTarget")]
    [InlineData("intent")]
    [InlineData("threshold")]
    [InlineData("weight")]
    public void Invalid_semantic_targets_are_rejected(string field)
    {
        using var directory = new TestDirectory();
        var selector = field switch
        {
            "emptySelector" => new ControlSelector(),
            "invalidType" => new ControlSelector { ControlType = "NotAControl" },
            "index" => new ControlSelector { Name = "Save", Index = -1 },
            _ => new ControlSelector { Name = "Save" }
        };
        var target = new SemanticTargetDefinition
        {
            Intent = field == "intent" ? "" : "Save",
            Strategies = [new SelectorStrategy { Name = "Save", Weight = field == "weight" ? 2 : 1 }],
            Thresholds = new ResolutionThresholds { MinimumConfidence = field == "threshold" ? 2 : .7 }
        };
        var profile = Profile("bad") with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, ControlSelector> { ["save"] = selector },
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["save"] = field == "nullTarget" ? null! : target
            }
        };
        Write(directory, profile);
        Assert.Throws<ProfileValidationException>(() => PortableProfileStore.LoadFromDirectory(directory.Path));
    }

    private static ApplicationProfile Profile(string id)
        => new() { Id = id, DisplayName = id, ExecutablePath = "fixture.exe" };

    private static string Write(TestDirectory directory, ApplicationProfile profile)
    {
        var path = Path.Combine(directory.Path, profile.Id.Length == 0 ? "invalid.application.json" : $"{profile.Id}.application.json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }
}
