using System.Text.Json;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Server.Linux;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ProfileValidationBranchTests
{
    [Theory]
    [InlineData("timeout", 100, true)]
    [InlineData("timeout", 300_000, true)]
    [InlineData("timeout", 99, false)]
    [InlineData("timeout", 300_001, false)]
    [InlineData("poll", 10, true)]
    [InlineData("poll", 10_000, true)]
    [InlineData("poll", 9, false)]
    [InlineData("poll", 10_001, false)]
    [InlineData("depth", 1, true)]
    [InlineData("depth", 20, true)]
    [InlineData("depth", 0, false)]
    [InlineData("depth", 21, false)]
    [InlineData("results", 1, true)]
    [InlineData("results", 5_000, true)]
    [InlineData("results", 0, false)]
    [InlineData("results", 5_001, false)]
    [InlineData("schema", 1, true)]
    [InlineData("schema", 2, true)]
    [InlineData("schema", 0, false)]
    [InlineData("schema", 3, false)]
    public void Both_stores_enforce_exact_configuration_boundaries(string field, int value, bool valid)
    {
        var profile = TestProfile.Create() with { Id = "editor_1-a" };
        profile = field switch
        {
            "timeout" => profile with { OperationTimeoutMs = value },
            "poll" => profile with { PollIntervalMs = value },
            "depth" => profile with { MaxTreeDepth = value },
            "results" => profile with { MaxResults = value },
            _ => profile with { SchemaVersion = value }
        };
        using var directory = new TestDirectory();
        directory.Write("test.application.json", JsonSerializer.Serialize(profile));
        if (valid)
        {
            Assert.Single(ApplicationProfileStore.LoadFromDirectory(directory.Path).List());
            Assert.Single(PortableProfileStore.LoadFromDirectory(directory.Path).List());
        }
        else
        {
            var windowsException = Assert.Throws<ProfileValidationException>(
                () => ApplicationProfileStore.LoadFromDirectory(directory.Path));
            var portableException = Assert.Throws<ProfileValidationException>(
                () => PortableProfileStore.LoadFromDirectory(directory.Path));
            Assert.False(string.IsNullOrWhiteSpace(windowsException.Message));
            Assert.False(string.IsNullOrWhiteSpace(portableException.Message));
        }
    }

    public static IEnumerable<object[]> InvalidProfiles()
    {
        var profile = TestProfile.Create();
        yield return [profile with { Id = "" }];
        yield return [profile with { Id = "invalid.dot" }];
        yield return [profile with { DisplayName = "" }];
        yield return [profile with { ExecutablePath = "" }];
        yield return [profile with { NativeInput = null! }];
        yield return [profile with { SemanticSelectors = null! }];
        yield return [profile with { SemanticTargets = null! }];
        yield return [profile with { SensitiveAutomationIds = null! }];
        foreach (var selector in new ControlSelector?[]
        {
            null, new(), new() { SemanticKey = "other" }, new() { Name = "Field", Index = -1 }
        })
        {
            yield return [profile with
            {
                SemanticSelectors = new Dictionary<string, ControlSelector> { ["field"] = selector! }
            }];
        }
        yield return [profile with
        {
            SemanticSelectors = new Dictionary<string, ControlSelector> { [""] = new() { Name = "Field" } }
        }];
        var target = ValidTarget();
        var invalidTargets = new SemanticTargetDefinition?[]
        {
            null,
            target with { Intent = "" },
            target with { Strategies = null! },
            target with { Strategies = [] },
            target with { Thresholds = null! },
            target with { Thresholds = new() { MinimumConfidence = -0.01 } },
            target with { Thresholds = new() { MinimumConfidence = 1.01 } },
            target with { Thresholds = new() { MinimumMargin = -0.01 } },
            target with { Thresholds = new() { MinimumMargin = 1.01 } },
            target with { Strategies = [null!] },
            target with { Strategies = [new()] },
            target with { Strategies = [new() { SemanticKey = "other" }] },
            target with { Strategies = [new() { AutomationId = "Field", Weight = -0.01 }] },
            target with { Strategies = [new() { AutomationId = "Field", Weight = 1.01 }] },
            target with { Strategies = [new() { AutomationId = "Field", Index = -1 }] },
            target with { ExpectedControlTypes = null! },
            target with { ExpectedControlTypes = ["InvalidType"] }
        };
        foreach (var invalid in invalidTargets)
        {
            yield return [profile with
            {
                SemanticTargets = new Dictionary<string, SemanticTargetDefinition> { ["field"] = invalid! }
            }];
        }
        yield return [profile with
        {
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition> { [""] = target }
        }];
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void Direct_validator_rejects_every_invalid_shape(ApplicationProfile invalid)
    {
        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(invalid));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void Both_stores_reject_invalid_shapes_without_publishing_any_changes(ApplicationProfile invalid)
    {
        using var directory = new TestDirectory();
        var path = directory.Write("test.application.json", JsonSerializer.Serialize(TestProfile.Create()));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        var portable = PortableProfileStore.LoadFromDirectory(directory.Path);
        File.WriteAllText(path, JsonSerializer.Serialize(invalid));
        var result = store.Reload();
        var portableResult = portable.Reload();
        Assert.False(result.Succeeded);
        Assert.False(portableResult.Succeeded);
        Assert.Single(result.Failures);
        Assert.Single(portableResult.Failures);
        Assert.False(string.IsNullOrWhiteSpace(result.Failures[0].Message));
        Assert.False(string.IsNullOrWhiteSpace(portableResult.Failures[0].Message));
        Assert.Equal(1, result.Generation);
        Assert.Equal(1, portableResult.Generation);
        Assert.Equal("test-app", Assert.Single(result.Profiles).Id);
        Assert.Equal("test-app", Assert.Single(portableResult.Profiles).Id);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void Both_stores_accept_semantic_threshold_and_strategy_boundaries(double confidence, double weight)
    {
        using var directory = new TestDirectory();
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, ControlSelector>
            {
                ["legacy"] = new() { AutomationId = "Legacy", Index = 0 }
            },
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["field"] = ValidTarget() with
                {
                    Thresholds = new() { MinimumConfidence = confidence, MinimumMargin = confidence },
                    Strategies = [new() { AutomationId = "Field", Index = 0, Weight = weight }]
                }
            }
        };
        directory.Write("test.application.json", JsonSerializer.Serialize(profile));
        Assert.Single(ApplicationProfileStore.LoadFromDirectory(directory.Path).List());
        Assert.Single(PortableProfileStore.LoadFromDirectory(directory.Path).List());
    }

    [Fact]
    public void Portable_store_accepts_every_named_windows_control_type_and_rejects_mixed_invalid_types()
    {
        using var directory = new TestDirectory();
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["field"] = ValidTarget() with
                {
                    ExpectedControlTypes = Enum.GetNames<FlaUI.Core.Definitions.ControlType>()
                }
            }
        };
        var path = directory.Write("test.application.json", JsonSerializer.Serialize(profile));
        Assert.Single(ApplicationProfileStore.LoadFromDirectory(directory.Path).List());
        var portable = PortableProfileStore.LoadFromDirectory(directory.Path);
        Assert.Single(portable.List());
        File.WriteAllText(path, JsonSerializer.Serialize(profile with
        {
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["field"] = ValidTarget() with { ExpectedControlTypes = ["Edit", "InvalidType"] }
            }
        }));
        var failure = portable.Reload();
        Assert.False(failure.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(failure.Failures).Message));
    }

    [Fact]
    public void Portable_supports_commented_trailing_comma_json_and_initial_case_insensitive_sorting()
    {
        using var directory = new TestDirectory();
        directory.Write("z.application.json",
            """{"id":"z","displayName":"Z","executablePath":"z.exe", /* comment */ }""");
        directory.Write("A.application.json",
            """{"id":"A","displayName":"A","executablePath":"a.exe","backend":"uia2"}""");
        var profiles = PortableProfileStore.LoadFromDirectory(directory.Path).List();
        Assert.Equal(["A", "z"], profiles.Select(profile => profile.Id));
        Assert.Equal(AutomationBackend.Uia2, profiles[0].Backend);
    }

    [Fact]
    public void Stale_session_remains_stale_when_its_original_disk_content_is_restored_without_reload()
    {
        using var directory = new TestDirectory();
        var originalJson = JsonSerializer.Serialize(TestProfile.Create());
        var path = directory.Write("test.application.json", originalJson);
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.True(store.TryGet("test-app", out var original));
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, JsonSerializer.Serialize(TestProfile.Create() with { DisplayName = "Changed" }));
        Assert.True(store.Reload().Succeeded);
        File.WriteAllText(path, originalJson);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.True(store.IsStale(original));
    }

    [Fact]
    public void SaveFile_creates_missing_parent_directories_and_rejects_whitespace_output_paths()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "new", "nested", "saved.application.json");
        ApplicationProfileStore.SaveFile(path, TestProfile.Create());
        Assert.Equal("test-app", ApplicationProfileStore.LoadFile(path).Id);
        Assert.Throws<ArgumentException>(() => ApplicationProfileStore.SaveFile(" ", TestProfile.Create()));
        Assert.Throws<ArgumentException>(() => PortableProfileStore.LoadFromDirectory(" "));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"id":"empty","displayName":"Empty","executablePath":"","semanticTargets":null}""")]
    public void Reload_null_and_missing_configuration_reports_nonempty_failures(string invalid)
    {
        using var directory = new TestDirectory();
        var path = directory.Write("test.application.json", JsonSerializer.Serialize(TestProfile.Create()));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        var portable = PortableProfileStore.LoadFromDirectory(directory.Path);
        File.WriteAllText(path, invalid);
        Assert.Throws<ProfileValidationException>(() => ApplicationProfileStore.LoadFile(path));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(store.Reload().Failures).Message));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(portable.Reload().Failures).Message));
    }

    [Fact]
    public void Native_defaults_and_optional_keyboard_foreground_match_the_security_contract()
    {
        var policy = new NativeInputPolicy();
        Assert.False(policy.Enabled);
        Assert.False(policy.AllowKeyboard);
        Assert.False(policy.AllowSystemKeys);
        Assert.True(policy.AllowMouse);
        Assert.True(policy.RequireForeground);
        Assert.Equal(500, policy.MaximumTextLength);
        Assert.Equal("clientArea", policy.ConstrainTo);
        Assert.Equal(["left"], policy.AllowedMouseButtons);
        foreach (var button in new[] { "left", "right", "middle" })
        {
            (policy with
            {
                Enabled = true, AllowKeyboard = false, RequireForeground = false,
                AllowedMouseButtons = [button]
            }).Validate();
        }
        Assert.Throws<ProfileValidationException>(() => (policy with
        {
            Enabled = true, AllowKeyboard = true, RequireForeground = false
        }).Validate());
    }

    [Fact]
    public void Native_button_values_and_boundaries_are_strict()
    {
        foreach (var buttons in new IReadOnlyList<string>?[]
        {
            null, [], ["Left"], [""], ["left", "right", "middle", "left"], ["right", "right"],
            ["left", "extra"]
        })
        {
            Assert.Throws<ProfileValidationException>(() =>
                (new NativeInputPolicy { AllowedMouseButtons = buttons! }).Validate());
        }
        Assert.Throws<ProfileValidationException>(() =>
            (new NativeInputPolicy { ConstrainTo = null! }).Validate());
    }

    [Fact]
    public void Unchanged_reload_preserves_revision_and_does_not_mark_active_profile_stale()
    {
        using var directory = new TestDirectory();
        directory.Write("test.application.json", JsonSerializer.Serialize(TestProfile.Create()));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.True(store.TryGet("test-app", out var original));
        Assert.True(store.Reload().Succeeded);
        Assert.Equal(2, store.Generation);
        Assert.True(store.TryGet("test-app", out var current));
        Assert.NotSame(original, current);
        Assert.Equal(original.Metadata?.Revision, current.Metadata?.Revision);
        Assert.False(store.IsStale(original));
    }

    [Fact]
    public void Portable_source_staleness_checks_timestamp_and_hash_and_profile_deletion()
    {
        using var directory = new TestDirectory();
        var profile = TestProfile.Create();
        var path = directory.Write("test.application.json", JsonSerializer.Serialize(profile));
        var portable = PortableProfileStore.LoadFromDirectory(directory.Path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, timestamp.AddSeconds(2));
        Assert.True(Assert.Single(portable.List()).IsStale);
        Assert.True(portable.Reload().Succeeded);
        timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, JsonSerializer.Serialize(profile with { DisplayName = "Changed" }));
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.True(Assert.Single(portable.List()).IsStale);
        Assert.True(portable.Reload().Succeeded);
        File.Delete(path);
        Assert.True(Assert.Single(portable.List()).IsStale);
        Assert.True(portable.Reload().Succeeded);
        Assert.Empty(portable.List());
        Assert.Equal(4, portable.Generation);
        Assert.NotNull(portable.LastReloadResult);
    }

    [Fact]
    public void Direct_validator_reports_missing_executable_separately_from_relative_executable()
    {
        var exception = Assert.Throws<ProfileValidationException>(() => ApplicationProfileValidator.Validate(
            TestProfile.Create() with { ExecutablePath = "" }));
        Assert.Contains("requires an executable path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Direct_validator_requires_a_display_name()
    {
        var exception = Assert.Throws<ProfileValidationException>(() => ApplicationProfileValidator.Validate(
            TestProfile.Create() with { DisplayName = "" }));
        Assert.Contains("display name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Portable_missing_source_can_be_created_and_constructor_rejects_duplicates()
    {
        using var directory = new TestDirectory();
        Assert.Throws<ArgumentException>(() => PortableProfileStore.LoadFromDirectory(""));
        var missing = Path.Combine(directory.Path, "missing");
        var portable = PortableProfileStore.LoadFromDirectory(missing);
        Assert.Empty(portable.List());
        directory.Write("missing/z.application.json", JsonSerializer.Serialize(TestProfile.Create("z")));
        directory.Write("missing/a.application.json", JsonSerializer.Serialize(TestProfile.Create("a")));
        directory.Write("missing/ignored.json", "{");
        directory.Write("missing/nested/ignored.application.json", "{");
        Assert.True(portable.Reload().Succeeded);
        Assert.Equal(["a", "z"], portable.List().Select(profile => profile.Id));
        directory.Write("missing/duplicate.application.json", JsonSerializer.Serialize(TestProfile.Create("A")));
        var exception = Assert.Throws<ProfileValidationException>(() => PortableProfileStore.LoadFromDirectory(missing));
        Assert.Contains("Duplicate application profile ID 'A'", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ProfileValidationException>(() => new ApplicationProfileStore(
            [TestProfile.Create("a"), TestProfile.Create("A")]));
    }

    [Fact]
    public void Summaries_project_every_profile_and_metadata_field()
    {
        using var directory = new TestDirectory();
        var profile = TestProfile.Create() with
        {
            Backend = AutomationBackend.Uia2,
            EnableScreenshots = true,
            DisplayName = "Summary profile"
        };
        directory.Write("test.application.json", JsonSerializer.Serialize(profile));
        var windows = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        var portable = PortableProfileStore.LoadFromDirectory(directory.Path);
        Assert.Null(portable.LastReloadResult);
        Assert.True(windows.TryGet(profile.Id, out var loaded));
        var metadata = Assert.IsType<ApplicationProfileMetadata>(loaded.Metadata);
        foreach (var summary in new[] { Assert.Single(windows.List()), Assert.Single(portable.List()) })
        {
            Assert.Equal(profile.Id, summary.Id);
            Assert.Equal(profile.DisplayName, summary.DisplayName);
            Assert.Equal(profile.ExecutablePath, summary.ExecutablePath);
            Assert.Equal(profile.Backend, summary.Backend);
            Assert.True(summary.EnableScreenshots);
            Assert.Equal(metadata.SourceFile, summary.SourceFile);
            Assert.Equal(metadata.Revision, summary.Revision);
            Assert.Equal(metadata.SourceLastWriteTimeUtc, summary.SourceLastWriteTimeUtc);
            Assert.Equal(1, summary.Generation);
            Assert.Equal(ProfileValidationStatus.Valid, summary.ValidationStatus);
            Assert.False(summary.IsStale);
            Assert.NotNull(summary.LoadedAtUtc);
        }
        Assert.Equal(metadata.LoadedAtUtc, Assert.Single(windows.List()).LoadedAtUtc);
        Assert.Equal(ProfileValidationStatus.Valid, metadata.ValidationStatus);
    }

    [Fact]
    public void In_memory_profiles_have_hash_metadata_and_no_source_staleness()
    {
        var store = new ApplicationProfileStore([TestProfile.Create()]);
        Assert.True(store.TryGet("test-app", out var profile));
        var metadata = Assert.IsType<ApplicationProfileMetadata>(profile.Metadata);
        Assert.Null(metadata.SourceFile);
        Assert.Null(metadata.SourceLastWriteTimeUtc);
        Assert.Equal(64, metadata.Revision.Length);
        Assert.False(store.IsStale(profile));
        Assert.False(Assert.Single(store.List()).IsStale);
        Assert.Throws<ArgumentNullException>(() => ApplicationProfileValidator.Validate(null!));
        Assert.Throws<ArgumentException>(() => ApplicationProfileStore.LoadFromDirectory(" "));
    }

    private static SemanticTargetDefinition ValidTarget()
        => new()
        {
            Intent = "field", ExpectedControlTypes = ["Edit", "button"],
            Strategies = [new() { AutomationId = "Field" }]
        };
}
