using DesktopComputerUse.Automation.Profiles;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ProfileDraftBuilderTests
{
    [Fact]
    public void AddTarget_creates_schema_v2_target_and_fingerprint()
    {
        var profile = TestProfile.Create();
        var candidate = SnapshotFixtures.Control(
            "node-0002",
            name: "Customer name",
            automationId: "CustomerNameTextBox",
            supportedPatterns: ["Value"]);
        var snapshot = SnapshotFixtures.Application([candidate]);

        var updated = new ProfileDraftBuilder().AddTarget(
            profile,
            snapshot,
            "customer-name",
            "Editable customer name field",
            candidate.CandidateId,
            ["Value"]);

        Assert.Equal(2, updated.SchemaVersion);
        Assert.Empty(updated.SemanticSelectors);
        var target = Assert.Single(updated.SemanticTargets).Value;
        Assert.Equal("Editable customer name field", target.Intent);
        Assert.Equal(snapshot.Window.View.Key, target.Scope.ViewKey);
        Assert.Equal(["Value"], target.RequiredPatterns);
        Assert.Equal("CustomerNameTextBox", Assert.Single(target.Strategies).AutomationId);
        Assert.Null(Assert.Single(target.Strategies).Name);
        Assert.Null(Assert.Single(target.Strategies).ClassName);
        Assert.NotNull(target.Fingerprint);
    }

    [Fact]
    public void AddTarget_rejects_unknown_snapshot_candidate()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ProfileDraftBuilder().AddTarget(
                TestProfile.Create(),
                SnapshotFixtures.Application([]),
                "save",
                "Save record",
                "missing"));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public void AddTarget_rejects_snapshot_from_another_profile()
    {
        var candidate = SnapshotFixtures.Control("node-0001");
        var snapshot = SnapshotFixtures.Application([candidate]) with
        {
            ProfileId = "another-app"
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProfileDraftBuilder().AddTarget(
                TestProfile.Create(),
                snapshot,
                "field",
                "Field",
                candidate.CandidateId));

        Assert.Contains("does not match", exception.Message);
    }

    [Fact]
    public void ApplyPatch_requires_matching_profile()
    {
        var target = new SemanticTargetDefinition
        {
            Intent = "Save record",
            Strategies = [new SelectorStrategy { AutomationId = "SaveButton" }]
        };
        var patch = new ProfileUpdatePatch("other", "proposal", "save", target);

        Assert.Throws<ArgumentException>(() =>
            new ProfileDraftBuilder().ApplyPatch(TestProfile.Create(), patch));
    }

    [Theory]
    [InlineData("", "Intent", "candidate")]
    [InlineData("key", "", "candidate")]
    [InlineData("key", "Intent", "")]
    public void AddTarget_rejects_missing_required_arguments(
        string key,
        string intent,
        string candidateId)
        => Assert.Throws<ArgumentException>(() =>
            new ProfileDraftBuilder().AddTarget(
                TestProfile.Create(),
                SnapshotFixtures.Application([]),
                key,
                intent,
                candidateId));

    [Fact]
    public void AddTarget_uses_name_when_automation_id_is_missing()
    {
        var profile = TestProfile.Create();
        var candidate = SnapshotFixtures.Control(
            "node",
            name: "Customer name",
            className: "EditClass");
        var updated = new ProfileDraftBuilder().AddTarget(
            profile,
            SnapshotFixtures.Application([candidate]),
            "name",
            "Name",
            candidate.CandidateId);

        var strategy = Assert.Single(updated.SemanticTargets["name"].Strategies);
        Assert.Null(strategy.AutomationId);
        Assert.Equal("Customer name", strategy.Name);
        Assert.Null(strategy.ClassName);
        Assert.Empty(updated.SemanticTargets["name"].RequiredPatterns);
    }

    [Fact]
    public void AddTarget_uses_class_when_id_and_name_are_missing()
    {
        var profile = TestProfile.Create();
        var candidate = SnapshotFixtures.Control(
            "node",
            className: "EditClass");
        var updated = new ProfileDraftBuilder().AddTarget(
            profile,
            SnapshotFixtures.Application([candidate]),
            "field",
            "Field",
            candidate.CandidateId);

        var strategy = Assert.Single(updated.SemanticTargets["field"].Strategies);
        Assert.Null(strategy.Name);
        Assert.Equal("EditClass", strategy.ClassName);
    }

    [Fact]
    public void ApplyPatch_updates_matching_profile()
    {
        var profile = TestProfile.Create();
        var target = new SemanticTargetDefinition
        {
            Intent = "Save record",
            Strategies = [new SelectorStrategy { AutomationId = "SaveButton" }]
        };

        var updated = new ProfileDraftBuilder().ApplyPatch(
            profile,
            new ProfileUpdatePatch(profile.Id, "proposal", "save", target));

        Assert.Same(target, updated.SemanticTargets["save"]);
        Assert.Equal(2, updated.SchemaVersion);
    }
}
