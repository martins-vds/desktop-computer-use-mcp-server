using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ApplicationProfileStoreTests
{
    [Fact]
    public void LoadFromDirectory_normalizes_paths_and_case_insensitive_collections()
    {
        using var directory = new TestDirectory();
        directory.Write(
            "profiles/editor.application.json",
            """
            {
              // Comments and trailing commas are intentionally supported.
              "id": "editor",
              "displayName": "Editor",
              "executablePath": "../apps/./editor.exe",
              "workingDirectory": "../work/../workspace",
              "backend": "uia2",
              "semanticSelectors": {
                "Save": { "automationId": "SaveButton" },
              },
              "sensitiveAutomationIds": [ "PasswordBox" ],
            }
            """);

        var profileDirectory = Path.Combine(directory.Path, "profiles");
        var store = ApplicationProfileStore.LoadFromDirectory(profileDirectory);

        Assert.True(store.TryGet("EDITOR", out var profile));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(profileDirectory, "../apps/editor.exe")),
            profile.ExecutablePath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(profileDirectory, "../workspace")),
            profile.WorkingDirectory);
        Assert.True(profile.SemanticSelectors.ContainsKey("save"));
        Assert.Contains("passwordbox", profile.SensitiveAutomationIds);
        Assert.Equal(1, profile.SchemaVersion);
        var legacyTarget = profile.EffectiveSemanticTargets["save"];
        Assert.Equal("Save", legacyTarget.Intent);
        Assert.Equal("SaveButton", Assert.Single(legacyTarget.Strategies).AutomationId);
        Assert.Equal(1, legacyTarget.Thresholds.MinimumConfidence);
        Assert.Equal(1, legacyTarget.Thresholds.MinimumMargin);
    }

    [Fact]
    public void LoadFromDirectory_defaults_working_directory_to_executable_parent()
    {
        using var directory = new TestDirectory();
        directory.Write(
            "default.application.json",
            """
            {
              "id": "default",
              "displayName": "Default",
              "executablePath": "bin/default.exe"
            }
            """);

        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);

        Assert.True(store.TryGet("default", out var profile));
        Assert.Equal(Path.GetDirectoryName(profile.ExecutablePath), profile.WorkingDirectory);
    }

    [Fact]
    public void LoadFromDirectory_ignores_nested_and_non_profile_json_and_sorts_results()
    {
        using var directory = new TestDirectory();
        directory.Write("z.application.json", ProfileJson("z"));
        directory.Write("A.application.json", ProfileJson("A"));
        directory.Write("ignored.json", ProfileJson("ignored"));
        directory.Write("nested/nested.application.json", ProfileJson("nested"));

        var profiles = ApplicationProfileStore.LoadFromDirectory(directory.Path).List();

        Assert.Equal(["A", "z"], profiles.Select(profile => profile.Id));
    }

    [Fact]
    public void LoadFromDirectory_returns_empty_store_for_missing_directory()
    {
        using var directory = new TestDirectory();
        var missing = Path.Combine(directory.Path, "missing");

        var store = ApplicationProfileStore.LoadFromDirectory(missing);

        Assert.Empty(store.List());
    }

    [Fact]
    public void LoadFromDirectory_rejects_missing_directory_argument()
        => Assert.Throws<ArgumentException>(() =>
            ApplicationProfileStore.LoadFromDirectory(""));

    [Fact]
    public void LoadFromDirectory_rejects_duplicate_ids_case_insensitively()
    {
        using var directory = new TestDirectory();
        directory.Write("first.application.json", ProfileJson("Editor"));
        directory.Write("second.application.json", ProfileJson("editor"));

        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileStore.LoadFromDirectory(directory.Path));

        Assert.Contains("Duplicate application profile ID 'editor'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_validates_profiles_before_storing_them()
    {
        var invalid = TestProfile.Create() with { DisplayName = "" };

        Assert.Throws<ProfileValidationException>(() =>
            new ApplicationProfileStore([invalid]));
    }

    [Fact]
    public void LoadFromDirectory_loads_schema_v2_targets_and_preserves_case_insensitive_lookup()
    {
        using var directory = new TestDirectory();
        directory.Write(
            "v2.application.json",
            """
            {
              "schemaVersion": 2,
              "id": "crm",
              "displayName": "CRM",
              "executablePath": "crm.exe",
              "semanticTargets": {
                "Customer-Name": {
                  "intent": "customer name",
                  "synonyms": [ "account holder" ],
                  "expectedControlTypes": [ "Edit" ],
                  "requiredPatterns": [ "Value" ],
                  "scope": {
                    "viewKey": "view-main",
                    "ancestorLabels": [ "Customer details" ]
                  },
                  "strategies": [
                    {
                      "automationId": "CustomerNameTextBox",
                      "controlType": "Edit",
                      "weight": 0.9
                    }
                  ],
                  "thresholds": {
                    "minimumConfidence": 0.8,
                    "minimumMargin": 0.1
                  },
                  "fingerprint": {
                    "controlType": "Edit",
                    "supportedPatterns": [ "Value" ],
                    "nameTokens": [ "customer", "name" ],
                    "relativeRegion": "top-left"
                  },
                  "allowAiAssistance": true
                }
              }
            }
            """);

        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);

        Assert.True(store.TryGet("CRM", out var profile));
        Assert.Equal(2, profile.SchemaVersion);
        Assert.True(profile.SemanticTargets.ContainsKey("customer-name"));
        var target = profile.EffectiveSemanticTargets["CUSTOMER-NAME"];
        Assert.Equal("customer name", target.Intent);
        Assert.Equal(["account holder"], target.Synonyms);
        Assert.Equal(["Edit"], target.ExpectedControlTypes);
        Assert.Equal(["Value"], target.RequiredPatterns);
        Assert.Equal("view-main", target.Scope.ViewKey);
        Assert.Equal(["Customer details"], target.Scope.AncestorLabels);
        Assert.Equal(0.9, Assert.Single(target.Strategies).Weight);
        Assert.Equal(0.8, target.Thresholds.MinimumConfidence);
        Assert.Equal(0.1, target.Thresholds.MinimumMargin);
        Assert.Equal("top-left", target.Fingerprint?.RelativeRegion);
        Assert.True(target.AllowAiAssistance);
    }

    [Fact]
    public void EffectiveSemanticTargets_prefers_v2_targets_over_legacy_selectors()
    {
        var v2Target = new SemanticTargetDefinition
        {
            Intent = "modern save",
            Strategies = [new SelectorStrategy { AutomationId = "ModernSave" }]
        };
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, DesktopComputerUse.Contracts.Automation.ControlSelector>
            {
                ["save"] = new() { AutomationId = "LegacySave" }
            },
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["save"] = v2Target
            }
        };

        Assert.Same(v2Target, profile.EffectiveSemanticTargets["save"]);
    }

    [Fact]
    public void EffectiveSemanticTargets_preserves_non_overlapping_legacy_selectors()
    {
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, DesktopComputerUse.Contracts.Automation.ControlSelector>
            {
                ["cancel"] = new() { AutomationId = "CancelButton" }
            },
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["save"] = new()
                {
                    Intent = "Save",
                    Strategies = [new SelectorStrategy { AutomationId = "SaveButton" }]
                }
            }
        };

        Assert.Equal(2, profile.EffectiveSemanticTargets.Count);
        Assert.Equal(
            "CancelButton",
            Assert.Single(profile.EffectiveSemanticTargets["cancel"].Strategies).AutomationId);
        Assert.Equal(
            "SaveButton",
            Assert.Single(profile.EffectiveSemanticTargets["save"].Strategies).AutomationId);
    }

    [Fact]
    public void SaveFile_writes_indented_valid_profile_that_can_be_reloaded()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "saved.application.json");
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["save"] = new()
                {
                    Intent = "Save",
                    Strategies = [new SelectorStrategy { AutomationId = "SaveButton" }]
                }
            }
        };

        ApplicationProfileStore.SaveFile(path, profile);
        var json = File.ReadAllText(path);
        var reloaded = ApplicationProfileStore.LoadFile(path);

        Assert.Contains(Environment.NewLine, json);
        Assert.Equal(profile.Id, reloaded.Id);
        Assert.True(reloaded.SemanticTargets.ContainsKey("save"));
    }

    [Fact]
    public void SaveFile_rejects_invalid_profile()
    {
        using var directory = new TestDirectory();

        Assert.Throws<ProfileValidationException>(() =>
            ApplicationProfileStore.SaveFile(
                Path.Combine(directory.Path, "invalid.application.json"),
                TestProfile.Create() with { Id = "" }));
    }

    private static string ProfileJson(string id)
        => $$"""
             {
               "id": "{{id}}",
               "displayName": "{{id}}",
               "executablePath": "{{id}}.exe"
             }
             """;
}
