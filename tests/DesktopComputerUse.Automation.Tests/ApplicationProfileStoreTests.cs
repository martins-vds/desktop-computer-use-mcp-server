using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Configuration;

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
    public void LoadFromDirectory_rejects_duplicate_ids_case_insensitively()
    {
        using var directory = new TestDirectory();
        directory.Write("first.application.json", ProfileJson("Editor"));
        directory.Write("second.application.json", ProfileJson("editor"));

        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileStore.LoadFromDirectory(directory.Path));

        Assert.Contains("Duplicate application profile ID 'editor'", exception.Message, StringComparison.Ordinal);
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
