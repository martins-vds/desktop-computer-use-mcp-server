using System.Text.Json;
using DesktopComputerUse.Automation.Profiles;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ProfileDiffTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Compare_reports_no_differences_for_equal_profiles()
    {
        var profile = TestProfile.Create();

        Assert.Equal(
            ["No semantic profile differences."],
            ProfileDiff.Compare(profile, profile, Options));
    }

    [Fact]
    public void Compare_reports_added_removed_and_changed_targets()
    {
        var original = TestProfile.Create() with
        {
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["removed"] = Target("Removed", "RemovedButton"),
                ["changed"] = Target("Old", "OldButton")
            }
        };
        var draft = TestProfile.Create() with
        {
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["added"] = Target("Added", "AddedButton"),
                ["changed"] = Target("New", "NewButton")
            }
        };

        var result = ProfileDiff.Compare(original, draft, Options);

        Assert.Contains("+ semanticTargets.added", result);
        Assert.Contains("- semanticTargets.removed", result);
        Assert.Contains("~ semanticTargets.changed", result);
    }

    [Fact]
    public void Compare_reports_profile_metadata_changes()
    {
        var original = TestProfile.Create();
        var draft = original with { DisplayName = "Changed" };

        Assert.Contains(
            "~ profile metadata",
            ProfileDiff.Compare(original, draft, Options));
    }

    private static SemanticTargetDefinition Target(
        string intent,
        string automationId)
        => new()
        {
            Intent = intent,
            Strategies = [new SelectorStrategy { AutomationId = automationId }]
        };
}
