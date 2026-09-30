using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ProfileUpdateProposalFactoryTests
{
    [Fact]
    public void Create_returns_null_for_non_resolved_and_exact_results()
    {
        var target = Target();
        Assert.Null(ProfileUpdateProposalFactory.Create(
            "app",
            target,
            Result(ResolutionStatus.Ambiguous, null, "Ambiguous"),
            DateTimeOffset.UnixEpoch));
        Assert.Null(ProfileUpdateProposalFactory.Create(
            "app",
            target,
            Result(ResolutionStatus.Ambiguous, "candidate", "Ambiguous"),
            DateTimeOffset.UnixEpoch));
        Assert.Null(ProfileUpdateProposalFactory.Create(
            "app",
            target,
            Result(
                ResolutionStatus.Resolved,
                "candidate",
                "A configured selector strategy matched exactly and uniquely."),
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Create_builds_strategy_fingerprint_and_evidence()
    {
        var target = Target();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var result = Result(
            ResolutionStatus.Resolved,
            "candidate",
            "Fuzzy candidate met thresholds.");

        var proposal = ProfileUpdateProposalFactory.Create(
            "app",
            target,
            result,
            createdAt);

        Assert.NotNull(proposal);
        Assert.Equal("app", proposal.ProfileId);
        Assert.Equal("field", proposal.SemanticKey);
        Assert.Equal("candidate", proposal.CandidateId);
        Assert.Equal(0.9, proposal.Score);
        Assert.Equal(0.2, proposal.Margin);
        Assert.Equal(createdAt, proposal.CreatedAt);
        Assert.Equal(
            "CustomerNameTextBox",
            proposal.ProposedTarget.Strategies[0].AutomationId);
        Assert.Equal(2, proposal.ProposedTarget.Strategies.Count);
        Assert.NotNull(proposal.ProposedTarget.Fingerprint);
        Assert.Equal(["feature"], proposal.Evidence.Select(item => item.Feature));
    }

    [Fact]
    public void Create_uses_name_or_class_when_automation_id_is_missing()
    {
        var named = ResultWithControl(SnapshotFixtures.Control(
            "named",
            name: "Customer name",
            className: "EditClass"));
        var namedProposal = ProfileUpdateProposalFactory.Create(
            "app",
            Target(),
            named,
            DateTimeOffset.UnixEpoch)!;
        Assert.Equal(
            "Customer name",
            namedProposal.ProposedTarget.Strategies[0].Name);
        Assert.Null(namedProposal.ProposedTarget.Strategies[0].ClassName);

        var classOnly = ResultWithControl(SnapshotFixtures.Control(
            "class",
            className: "EditClass"));
        var classProposal = ProfileUpdateProposalFactory.Create(
            "app",
            Target(),
            classOnly,
            DateTimeOffset.UnixEpoch)!;
        Assert.Null(classProposal.ProposedTarget.Strategies[0].Name);
        Assert.Equal(
            "EditClass",
            classProposal.ProposedTarget.Strategies[0].ClassName);
    }

    private static SemanticTargetDefinition Target()
        => new()
        {
            Intent = "Customer name",
            Strategies =
            [
                new SelectorStrategy
                {
                    Name = "Customer name",
                    ControlType = "Edit",
                    Weight = 0.5
                }
            ]
        };

    private static ControlResolutionResult Result(
        ResolutionStatus status,
        string? selectedCandidateId,
        string reason)
    {
        var control = SnapshotFixtures.Control(
            "candidate",
            name: "Customer name",
            automationId: "CustomerNameTextBox",
            supportedPatterns: ["Value"]);
        return new ControlResolutionResult(
            status,
            "field",
            "Customer name",
            "view-main",
            selectedCandidateId,
            0.9,
            0.2,
            reason,
            [
                new ControlCandidateScore(
                    control.CandidateId,
                    0.9,
                    control,
                    [
                        new ResolutionFeatureScore(
                            "feature",
                            0.9,
                            1,
                            "evidence")
                    ])
            ]);
    }

    private static ControlResolutionResult ResultWithControl(
        DesktopComputerUse.Contracts.Discovery.ControlSnapshot control)
        => new(
            ResolutionStatus.Resolved,
            "field",
            "Field",
            "view-main",
            control.CandidateId,
            0.9,
            0.2,
            "Fuzzy candidate met thresholds.",
            [new ControlCandidateScore(control.CandidateId, 0.9, control, [])]);
}
