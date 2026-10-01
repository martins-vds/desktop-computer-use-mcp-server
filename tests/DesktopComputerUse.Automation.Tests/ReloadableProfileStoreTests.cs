using System.Security.Cryptography;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Server.Linux;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ReloadableProfileStoreTests
{
    [Fact]
    public void Reload_updates_revision_and_generation_without_mutating_active_profile()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("editor.application.json", Json("editor", "Original"));
        IReloadableApplicationProfileStore store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.Null(store.LastReloadResult);
        Assert.True(store.TryGet("editor", out var original));
        var initial = Assert.Single(store.List());
        Assert.Equal(1, initial.Generation);
        Assert.Equal(path, initial.SourceFile);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), initial.Revision);
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(path)), initial.SourceLastWriteTimeUtc);
        Assert.NotNull(initial.LoadedAtUtc);
        Assert.False(initial.IsStale);
        Assert.Equal(ProfileValidationStatus.Valid, initial.ValidationStatus);

        directory.Write("editor.application.json", Json("editor", "Changed"));
        Assert.True(Assert.Single(store.List()).IsStale);
        var result = store.Reload();

        Assert.True(result.Succeeded);
        Assert.Same(result, store.LastReloadResult);
        Assert.Empty(result.Failures);
        Assert.Equal(2, store.Generation);
        Assert.True(store.TryGet("editor", out var current));
        Assert.Equal("Changed", current.DisplayName);
        Assert.Equal(store.Generation, current.Metadata?.Generation);
        Assert.Equal("Original", original.DisplayName);
        Assert.Equal(1, original.Metadata?.Generation);
        Assert.NotEqual(original.Metadata?.Revision, current.Metadata?.Revision);
        Assert.True(store.IsStale(original));
        Assert.False(store.IsStale(current));
        Assert.False(Assert.Single(result.Profiles).IsStale);
        Assert.Equal(result.AttemptedAtUtc, current.Metadata?.LoadedAtUtc);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"id":"broken","displayName":"","executablePath":"app.exe"}""")]
    [InlineData("""{"id":"broken","displayName":"Broken","executablePath":"app.exe","nativeInput":null}""")]
    [InlineData("""{"id":"broken","displayName":"Broken","executablePath":"app.exe","semanticTargets":{"target":null}}""")]
    public void Reload_rejects_the_entire_invalid_batch_and_keeps_last_valid_registry(string invalid)
    {
        using var directory = new TestDirectory();
        directory.Write("editor.application.json", Json("editor", "Original"));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.True(store.TryGet("editor", out var original));
        directory.Write("editor.application.json", Json("editor", "Changed"));
        var invalidPath = directory.Write("broken.application.json", invalid);

        var failed = store.Reload();

        Assert.False(failed.Succeeded);
        Assert.Equal(1, failed.Generation);
        Assert.Equal(invalidPath, Assert.Single(failed.Failures).SourceFile);
        Assert.Equal(ProfileValidationStatus.Invalid, failed.Failures[0].ValidationStatus);
        Assert.True(store.TryGet("editor", out var retained));
        Assert.Same(original, retained);
        Assert.Equal("Original", Assert.Single(store.List()).DisplayName);
        Assert.True(Assert.Single(store.List()).IsStale);

        File.Delete(invalidPath);
        Assert.True(store.Reload().Succeeded);
        Assert.Equal(2, store.Generation);
    }

    [Fact]
    public void Reload_adds_and_removes_profiles_and_detects_deleted_active_profiles()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("old.application.json", Json("old", "Old"));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.True(store.TryGet("old", out var old));
        File.Delete(path);
        Assert.True(Assert.Single(store.List()).IsStale);
        directory.Write("new.application.json", Json("new", "New"));

        Assert.True(store.Reload().Succeeded);
        Assert.False(store.TryGet("old", out _));
        Assert.True(store.IsStale(old));
        Assert.Equal("new", Assert.Single(store.List()).Id);
        File.Delete(Path.Combine(directory.Path, "new.application.json"));
        Assert.True(store.Reload().Succeeded);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Reload_duplicate_ids_is_atomic_and_collects_multiple_failures()
    {
        using var directory = new TestDirectory();
        directory.Write("a.application.json", Json("editor", "Original"));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        directory.Write("b.application.json", Json("EDITOR", "Duplicate"));
        directory.Write("c.application.json", "{");

        var result = store.Reload();

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, failure => Assert.False(string.IsNullOrWhiteSpace(failure.Message)));
        Assert.Equal("Original", Assert.Single(store.List()).DisplayName);
        Assert.Equal(1, store.Generation);
    }

    [Fact]
    public void Staleness_detects_edits_even_when_last_write_time_is_preserved()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("editor.application.json", Json("editor", "Original"));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        directory.Write("editor.application.json", Json("editor", "Modified"));
        File.SetLastWriteTimeUtc(path, timestamp);

        Assert.True(Assert.Single(store.List()).IsStale);
    }

    [Fact]
    public void Reload_missing_directory_can_later_load_new_profiles()
    {
        using var directory = new TestDirectory();
        var source = Path.Combine(directory.Path, "profiles");
        var store = ApplicationProfileStore.LoadFromDirectory(source);
        Assert.Empty(store.List());
        directory.Write("profiles/editor.application.json", Json("editor", "Editor"));

        Assert.True(store.Reload().Succeeded);
        Assert.Equal("editor", Assert.Single(store.List()).Id);
    }

    [Fact]
    public void Reload_unavailable_source_directory_does_not_erase_registry()
    {
        using var directory = new TestDirectory();
        directory.Write("profiles/editor.application.json", Json("editor", "Editor"));
        var source = Path.Combine(directory.Path, "profiles");
        var store = ApplicationProfileStore.LoadFromDirectory(source);
        var portable = PortableProfileStore.LoadFromDirectory(source);
        Directory.Delete(source, recursive: true);

        Assert.False(store.Reload().Succeeded);
        Assert.False(portable.Reload().Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(store.LastReloadResult!.Failures).Message));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(portable.LastReloadResult!.Failures).Message));
        Assert.Single(store.List());
        Assert.Single(portable.List());
        Assert.Equal(1, store.Generation);
        Assert.Equal(1, portable.Generation);
    }

    [Fact]
    public void Load_and_save_native_input_policy_does_not_persist_runtime_metadata()
    {
        using var directory = new TestDirectory();
        directory.Write("native.application.json",
            """
            {
              "id": "native", "displayName": "Native", "executablePath": "native.exe",
              "nativeInput": {
                "enabled": true, "allowMouse": true, "allowKeyboard": true,
                "allowedMouseButtons": ["left", "right"], "constrainTo": "clientArea",
                "requireForeground": true, "maximumTextLength": 100, "allowSystemKeys": false
              }
            }
            """);
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        Assert.True(store.TryGet("native", out var profile));
        Assert.True(profile.NativeInput.Enabled);
        Assert.True(profile.NativeInput.AllowKeyboard);
        Assert.Equal(100, profile.NativeInput.MaximumTextLength);
        var path = Path.Combine(directory.Path, "saved.json");
        ApplicationProfileStore.SaveFile(path, profile);
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("Metadata", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Revision", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(profile.NativeInput.MaximumTextLength,
            ApplicationProfileStore.LoadFile(path).NativeInput.MaximumTextLength);
    }

    [Fact]
    public void Reload_in_memory_store_fails_without_discarding_profiles()
    {
        var store = new ApplicationProfileStore([TestProfile.Create()]);
        Assert.False(store.Reload().Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(store.LastReloadResult!.Failures).Message));
        Assert.Single(store.List());
        Assert.Equal(1, store.Generation);
    }

    [Fact]
    public async Task Concurrent_reloads_publish_complete_generations()
    {
        using var directory = new TestDirectory();
        directory.Write("a.application.json", Json("a", "A"));
        directory.Write("b.application.json", Json("b", "B"));
        var store = ApplicationProfileStore.LoadFromDirectory(directory.Path);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => store.Reload())).ToArray();
        for (var i = 0; i < 30; i++)
        {
            var profiles = store.List();
            Assert.Equal(2, profiles.Count);
            Assert.Single(profiles.Select(profile => profile.Generation).Distinct());
        }
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(9, store.Generation);
        Assert.Equal(8, results.Select(result => result.Generation).Distinct().Count());
    }

    [Fact]
    public void Portable_reload_tracks_metadata_and_retains_invalid_reload_registry()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("editor.application.json", Json("editor", "Original"));
        var store = PortableProfileStore.LoadFromDirectory(directory.Path);
        var initial = Assert.Single(store.List());
        Assert.Equal(path, initial.SourceFile);
        Assert.NotNull(initial.Revision);
        Assert.Equal(1, initial.Generation);
        directory.Write("editor.application.json", Json("editor", "Changed"));
        directory.Write("bad.application.json", "{");
        Assert.True(Assert.Single(store.List()).IsStale);

        var failed = store.Reload();
        Assert.False(failed.Succeeded);
        Assert.Equal("Original", Assert.Single(failed.Profiles).DisplayName);
        Assert.Equal(1, store.Generation);
        Assert.Single(failed.Failures);
        File.Delete(Path.Combine(directory.Path, "bad.application.json"));
        var success = store.Reload();
        Assert.True(success.Succeeded);
        Assert.Equal(2, store.Generation);
        Assert.Equal("Changed", Assert.Single(store.List()).DisplayName);
        Assert.Equal(store.Generation, Assert.Single(store.List()).Generation);
        Assert.False(Assert.Single(store.List()).IsStale);
        Assert.NotEqual(initial.Revision, Assert.Single(store.List()).Revision);
    }

    [Fact]
    public void Portable_reload_rejects_duplicate_ids_and_invalid_native_input()
    {
        using var directory = new TestDirectory();
        directory.Write("editor.application.json", Json("editor", "Original"));
        var store = PortableProfileStore.LoadFromDirectory(directory.Path);
        directory.Write("duplicate.application.json", Json("EDITOR", "Duplicate"));
        directory.Write("native.application.json",
            """{"id":"native","displayName":"Native","executablePath":"native.exe","nativeInput":{"allowKeyboard":true,"requireForeground":false}}""");

        var result = store.Reload();

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, failure => Assert.False(string.IsNullOrWhiteSpace(failure.Message)));
        Assert.Equal("Original", Assert.Single(store.List()).DisplayName);
    }

    private static string Json(string id, string displayName)
        => $$"""{"id":"{{id}}","displayName":"{{displayName}}","executablePath":"{{id}}.exe"}""";
}
