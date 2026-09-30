using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.ProfileIntelligence;

public sealed class NoOpProfileIntelligenceProvider : IProfileIntelligenceProvider
{
    public Task<IntentResolutionSuggestion> RankCandidatesAsync(
        IntentResolutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new IntentResolutionSuggestion(
            null,
            null,
            ["No profile intelligence provider is configured."]));
    }
}
