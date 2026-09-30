using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.ProfileIntelligence;

public interface IProfileIntelligenceProvider
{
    Task<IntentResolutionSuggestion> RankCandidatesAsync(
        IntentResolutionRequest request,
        CancellationToken cancellationToken);
}
