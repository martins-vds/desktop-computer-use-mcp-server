using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.ProfileIntelligence;

public static class ProfileIntelligenceValidator
{
    public static IntentResolutionSuggestion Validate(
        IntentResolutionRequest request,
        IntentResolutionSuggestion suggestion)
    {
        if (suggestion.CandidateId is null)
        {
            return suggestion with { ReportedConfidence = null };
        }

        if (!request.Candidates.Any(candidate =>
                string.Equals(
                    candidate.CandidateId,
                    suggestion.CandidateId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"The intelligence provider selected unknown candidate '{suggestion.CandidateId}'.");
        }

        if (suggestion.ReportedConfidence is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "The intelligence provider returned confidence outside the 0 to 1 range.");
        }

        return suggestion;
    }
}
