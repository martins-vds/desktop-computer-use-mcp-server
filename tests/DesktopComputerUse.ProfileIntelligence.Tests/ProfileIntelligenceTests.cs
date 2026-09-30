using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;
using DesktopComputerUse.ProfileIntelligence.Copilot;

namespace DesktopComputerUse.ProfileIntelligence.Tests;

public sealed class ProfileIntelligenceTests
{
    [Fact]
    public async Task NoOp_provider_returns_no_candidate()
    {
        var provider = new NoOpProfileIntelligenceProvider();

        var result = await provider.RankCandidatesAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Null(result.CandidateId);
        Assert.Null(result.ReportedConfidence);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public void Validator_rejects_candidate_outside_bounded_request()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProfileIntelligenceValidator.Validate(
                CreateRequest(),
                new IntentResolutionSuggestion("node-9999", 0.9, [])));

        Assert.Contains("unknown candidate", exception.Message);
    }

    [Fact]
    public void Validator_accepts_known_candidate_but_does_not_calibrate_confidence()
    {
        var suggestion = new IntentResolutionSuggestion(
            "node-0001",
            0.74,
            ["Nearby label matches."]);

        var result = ProfileIntelligenceValidator.Validate(CreateRequest(), suggestion);

        Assert.Same(suggestion, result);
    }

    [Fact]
    public void Validator_rejects_invalid_confidence()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProfileIntelligenceValidator.Validate(
                CreateRequest(),
                new IntentResolutionSuggestion("node-0001", 1.1, [])));
    }

    [Fact]
    public async Task Copilot_provider_requires_explicit_target_opt_in_before_starting_runtime()
    {
        var provider = new CopilotProfileIntelligenceProvider(
            new CopilotProfileIntelligenceOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.RankCandidatesAsync(CreateRequest(), CancellationToken.None));

        Assert.Contains("opted into AI assistance", exception.Message);
    }

    private static IntentResolutionRequest CreateRequest()
    {
        var control = new ControlSnapshot
        {
            CandidateId = "node-0001",
            Name = "Customer name",
            AutomationId = "CustomerNameTextBox",
            ControlType = "Edit",
            IsEnabled = true,
            Bounds = new RectangleInfo(0, 0, 100, 30),
            RelativeBounds = new RectangleInfo(0, 0, 0.1, 0.1),
            SupportedPatterns = ["Value"]
        };
        return new IntentResolutionRequest(
            "customer-name",
            new SemanticTargetDefinition
            {
                Intent = "Customer name",
                Strategies = [new SelectorStrategy { AutomationId = "missing" }]
            },
            "view-main",
            [
                new ControlCandidateScore(
                    control.CandidateId,
                    0.8,
                    control,
                    [])
            ]);
    }
}
