using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;
using DesktopComputerUse.ProfileIntelligence.Copilot;

namespace DesktopComputerUse.ProfileBuilder.Copilot;

internal static class CopilotProfileBuilderCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await ExecuteAsync(Parse(args));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static CopilotCommand Parse(string[] args)
    {
        if (args.Length < 3 || args[0] is "-h" or "--help" or "help")
        {
            throw new ArgumentException(
                "Usage: profile-builder-copilot <profile> <snapshot.json> <semantic-key> [model]");
        }

        return new CopilotCommand(
            args[0],
            args[1],
            args[2],
            args.ElementAtOrDefault(3) ?? "auto");
    }

    private static async Task<int> ExecuteAsync(CopilotCommand command)
    {
        var profile = ApplicationProfileStore.LoadFile(command.ProfilePath);
        var snapshot = ReadSnapshot(command.SnapshotPath);
        var target = GetTarget(profile.EffectiveSemanticTargets, command.SemanticKey);
        EnsureAiEnabled(target, command.SemanticKey);
        var local = ResolveCandidates(snapshot, command.SemanticKey, target);
        var provider = new CopilotProfileIntelligenceProvider(
            new CopilotProfileIntelligenceOptions { Model = command.Model });
        var suggestion = await provider.RankCandidatesAsync(
            new IntentResolutionRequest(
                command.SemanticKey,
                target,
                snapshot.Window.View.Key,
                local.Candidates),
            CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(suggestion, JsonOptions));
        return suggestion.CandidateId is null ? 2 : 0;
    }

    private static ApplicationSnapshot ReadSnapshot(string path)
        => JsonSerializer.Deserialize<ApplicationSnapshot>(
            File.ReadAllText(path),
            JsonOptions)
            ?? throw new InvalidOperationException("Snapshot JSON is empty or invalid.");

    private static SemanticTargetDefinition GetTarget(
        IReadOnlyDictionary<string, SemanticTargetDefinition> targets,
        string semanticKey)
        => targets.TryGetValue(semanticKey, out var target)
            ? target
            : throw new InvalidOperationException(
                $"Semantic target '{semanticKey}' is not defined.");

    private static void EnsureAiEnabled(
        SemanticTargetDefinition target,
        string semanticKey)
    {
        if (!target.AllowAiAssistance)
        {
            throw new InvalidOperationException(
                $"Semantic target '{semanticKey}' does not set allowAiAssistance to true.");
        }
    }

    private static ControlResolutionResult ResolveCandidates(
        ApplicationSnapshot snapshot,
        string semanticKey,
        SemanticTargetDefinition target)
    {
        var result = new FuzzyControlResolver().Resolve(
            snapshot,
            semanticKey,
            target,
            15);
        return result.Candidates.Count > 0
            ? result
            : throw new InvalidOperationException(
                "No deterministic candidates are available for Copilot ranking.");
    }

    private sealed record CopilotCommand(
        string ProfilePath,
        string SnapshotPath,
        string SemanticKey,
        string Model);
}
