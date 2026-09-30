using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Profiles;

public sealed class ProfileDraftBuilder
{
    public ApplicationProfile AddTarget(
        ApplicationProfile profile,
        ApplicationSnapshot snapshot,
        string semanticKey,
        string intent,
        string candidateId,
        IReadOnlyList<string>? requiredPatterns = null)
    {
        ValidateTargetArguments(semanticKey, intent, candidateId);
        ValidateSnapshot(profile, snapshot);

        var candidate = snapshot.Window.Controls.SingleOrDefault(
            control => control.CandidateId == candidateId)
            ?? throw new ArgumentException(
                $"Candidate '{candidateId}' does not exist in the supplied snapshot.",
                nameof(candidateId));
        var target = new SemanticTargetDefinition
        {
            Intent = intent,
            ExpectedControlTypes = [candidate.ControlType],
            RequiredPatterns = requiredPatterns ?? [],
            Scope = new SelectorScope { ViewKey = snapshot.Window.View.Key },
            Strategies = [BuildStrategy(candidate)],
            Fingerprint = ControlFingerprintFactory.Create(candidate)
        };
        var targets = new Dictionary<string, SemanticTargetDefinition>(
            profile.EffectiveSemanticTargets,
            StringComparer.OrdinalIgnoreCase)
        {
            [semanticKey] = target
        };

        return profile with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, ControlSelector>(
                StringComparer.OrdinalIgnoreCase),
            SemanticTargets = targets
        };
    }

    private static void ValidateTargetArguments(
        string semanticKey,
        string intent,
        string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
    }

    private static void ValidateSnapshot(
        ApplicationProfile profile,
        ApplicationSnapshot snapshot)
    {
        var matches = new[]
        {
            string.Equals(
                profile.Id,
                snapshot.ProfileId,
                StringComparison.OrdinalIgnoreCase),
            string.Equals(
                Path.GetFullPath(profile.ExecutablePath),
                Path.GetFullPath(snapshot.ExecutablePath),
                StringComparison.OrdinalIgnoreCase),
            profile.Backend == snapshot.Backend
        };
        if (!matches.All(value => value))
        {
            throw new ArgumentException(
                "The snapshot profile, executable, or automation backend does not match the destination profile.",
                nameof(snapshot));
        }
    }

    public ApplicationProfile ApplyPatch(
        ApplicationProfile profile,
        ProfileUpdatePatch patch)
    {
        if (!string.Equals(profile.Id, patch.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Patch profile '{patch.ProfileId}' does not match '{profile.Id}'.",
                nameof(patch));
        }

        var targets = new Dictionary<string, SemanticTargetDefinition>(
            profile.EffectiveSemanticTargets,
            StringComparer.OrdinalIgnoreCase)
        {
            [patch.SemanticKey] = patch.ProposedTarget
        };
        return profile with
        {
            SchemaVersion = 2,
            SemanticSelectors = new Dictionary<string, ControlSelector>(
                StringComparer.OrdinalIgnoreCase),
            SemanticTargets = targets
        };
    }

    private static SelectorStrategy BuildStrategy(ControlSnapshot candidate)
        => new()
        {
            AutomationId = candidate.AutomationId,
            Name = string.IsNullOrWhiteSpace(candidate.AutomationId)
                ? candidate.Name
                : null,
            ControlType = candidate.ControlType,
            ClassName = string.IsNullOrWhiteSpace(candidate.AutomationId) &&
                string.IsNullOrWhiteSpace(candidate.Name)
                ? candidate.ClassName
                : null,
            Weight = 1
        };
}
