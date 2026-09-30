using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Resolution;

public static class ProfileUpdateProposalFactory
{
    public static ProfileUpdateProposal? Create(
        string profileId,
        SemanticTargetDefinition target,
        ControlResolutionResult result,
        DateTimeOffset createdAt)
    {
        if (!CanCreate(result))
        {
            return null;
        }

        var selected = result.Candidates.First(candidate =>
            candidate.CandidateId == result.SelectedCandidateId);
        var strategy = CreateStrategy(selected);
        var proposedTarget = target with
        {
            Strategies = new[] { strategy }
                .Concat(target.Strategies)
                .Distinct()
                .ToArray(),
            Fingerprint = ControlFingerprintFactory.Create(selected.Candidate)
        };

        return new ProfileUpdateProposal(
            Guid.NewGuid().ToString("N"),
            profileId,
            result.SemanticKey,
            result.ViewKey,
            selected.CandidateId,
            result.Score ?? 0,
            result.Margin ?? 0,
            proposedTarget,
            selected.Features,
            createdAt);
    }

    private static bool CanCreate(ControlResolutionResult result)
        => result.Status == ResolutionStatus.Resolved &&
            result.SelectedCandidateId is not null &&
            !result.Reason.StartsWith(
                "A configured selector strategy matched exactly",
                StringComparison.Ordinal);

    private static SelectorStrategy CreateStrategy(ControlCandidateScore selected)
        => new()
        {
            AutomationId = selected.Candidate.AutomationId,
            Name = string.IsNullOrWhiteSpace(selected.Candidate.AutomationId)
                ? selected.Candidate.Name
                : null,
            ControlType = selected.Candidate.ControlType,
            ClassName = string.IsNullOrWhiteSpace(selected.Candidate.AutomationId) &&
                string.IsNullOrWhiteSpace(selected.Candidate.Name)
                ? selected.Candidate.ClassName
                : null,
            Weight = 1
        };
}
