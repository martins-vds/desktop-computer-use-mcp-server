using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Resolution;

public sealed class FuzzyControlResolver
{
    public ControlResolutionResult Resolve(
        ApplicationSnapshot snapshot,
        string semanticKey,
        SemanticTargetDefinition target,
        int maximumCandidates = 10)
    {
        if (!string.IsNullOrWhiteSpace(target.Scope.ViewKey) &&
            !string.Equals(
                target.Scope.ViewKey,
                snapshot.Window.View.Key,
                StringComparison.OrdinalIgnoreCase))
        {
            return Empty(
                ResolutionStatus.InvalidTarget,
                semanticKey,
                target,
                snapshot.Window.View.Key,
                "The current application view does not match the target scope.");
        }

        var controlLookup = snapshot.Window.Controls.ToDictionary(
            control => control.CandidateId,
            StringComparer.Ordinal);

        foreach (var strategy in target.Strategies.OrderByDescending(strategy => strategy.Weight))
        {
            var exactMatches = snapshot.Window.Controls
                .Where(control => PassesHardGates(control, target))
                .Where(control => IsExactMatch(control, strategy, controlLookup))
                .ToArray();
            if (strategy.Index is int index)
            {
                exactMatches = index < exactMatches.Length
                    ? [exactMatches[index]]
                    : [];
            }

            if (exactMatches.Length == 1)
            {
                var candidate = exactMatches[0];
                return new ControlResolutionResult(
                    ResolutionStatus.Resolved,
                    semanticKey,
                    target.Intent,
                    snapshot.Window.View.Key,
                    candidate.CandidateId,
                    1,
                    1,
                    "A configured selector strategy matched exactly and uniquely.",
                    [
                        new ControlCandidateScore(
                            candidate.CandidateId,
                            1,
                            candidate,
                            [
                                new ResolutionFeatureScore(
                                    "exact-strategy",
                                    1,
                                    strategy.Weight,
                                    "Configured selector fields matched exactly.")
                            ])
                    ]);
            }
        }

        var scoredCandidates = snapshot.Window.Controls
            .Where(control => PassesHardGates(control, target))
            .Select(control => Score(control, target, controlLookup))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();

        if (scoredCandidates.Length == 0)
        {
            return Empty(
                ResolutionStatus.NotFound,
                semanticKey,
                target,
                snapshot.Window.View.Key,
                "No controls passed the target's type, pattern, and scope requirements.");
        }

        var best = scoredCandidates[0];
        var secondScore = scoredCandidates.Length > 1 ? scoredCandidates[1].Score : 0;
        var margin = best.Score - secondScore;
        var resolved =
            best.Score >= target.Thresholds.MinimumConfidence &&
            margin >= target.Thresholds.MinimumMargin;

        return new ControlResolutionResult(
            resolved ? ResolutionStatus.Resolved : ResolutionStatus.Ambiguous,
            semanticKey,
            target.Intent,
            snapshot.Window.View.Key,
            resolved ? best.CandidateId : null,
            best.Score,
            margin,
            resolved
                ? "The best candidate met the configured confidence and margin thresholds."
                : "Candidate evidence is insufficient or too close to another candidate.",
            scoredCandidates.Take(maximumCandidates).ToArray());
    }

    private static bool IsExactMatch(
        ControlSnapshot control,
        SelectorStrategy strategy,
        IReadOnlyDictionary<string, ControlSnapshot> controls)
    {
        if (!string.IsNullOrWhiteSpace(strategy.AutomationId) &&
            !string.Equals(
                control.AutomationId,
                strategy.AutomationId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(strategy.Name) &&
            !string.Equals(control.Name, strategy.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(strategy.ControlType) &&
            !string.Equals(
                control.ControlType,
                strategy.ControlType,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(strategy.ClassName) &&
            !string.Equals(
                control.ClassName,
                strategy.ClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        return !strategy.IsEmpty &&
            MatchesAncestor(control, strategy.Ancestor, controls);
    }

    private static bool PassesHardGates(
        ControlSnapshot control,
        SemanticTargetDefinition target)
    {
        if (target.ExpectedControlTypes.Count > 0 &&
            !target.ExpectedControlTypes.Contains(
                control.ControlType,
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (target.RequiredPatterns.Any(required =>
                !control.SupportedPatterns.Contains(
                    required,
                    StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (target.Scope.AncestorLabels.Count > 0 &&
            target.Scope.AncestorLabels.Any(required =>
                !control.TreePath.Any(path =>
                    TextNormalizer.Similarity(path, required) >= 0.8)))
        {
            return false;
        }

        return true;
    }

    private static ControlCandidateScore Score(
        ControlSnapshot control,
        SemanticTargetDefinition target,
        IReadOnlyDictionary<string, ControlSnapshot> controls)
    {
        var features = new List<ResolutionFeatureScore>();

        Add(features, "intent-name", TextNormalizer.Similarity(target.Intent, control.Name), 0.6,
            $"Intent '{target.Intent}' compared with name '{control.Name}'.");
        Add(features, "intent-automation-id",
            TextNormalizer.Similarity(target.Intent, control.AutomationId), 0.55,
            $"Intent compared with automation ID '{control.AutomationId}'.");
        Add(features, "intent-help-text",
            TextNormalizer.Similarity(target.Intent, control.HelpText), 0.35,
            $"Intent compared with help text '{control.HelpText}'.");

        var labelScore = control.NearbyLabels
            .Select(label =>
                TextNormalizer.Similarity(
                    target.Synonyms.Append(target.Intent).SelectMany(TextNormalizer.Tokens),
                    TextNormalizer.Tokens(label.Text)) * label.Confidence)
            .DefaultIfEmpty(0)
            .Max();
        Add(features, "nearby-label", labelScore, 0.75,
            $"Best nearby label evidence from {control.NearbyLabels.Count} labels.");

        var strategyScore = target.Strategies
            .Select(strategy => ScoreStrategy(control, strategy, controls))
            .DefaultIfEmpty(0)
            .Max();
        Add(features, "selector-strategy", strategyScore, 1,
            "Best configured selector strategy similarity.");

        if (target.Fingerprint is not null)
        {
            Add(features, "fingerprint", ScoreFingerprint(control, target.Fingerprint), 0.8,
                "Similarity to the previously approved control fingerprint.");
        }

        var totalWeight = features.Sum(feature => feature.Weight);
        var finalScore = totalWeight == 0
            ? 0
            : features.Sum(feature => feature.Score * feature.Weight) / totalWeight;

        return new ControlCandidateScore(
            control.CandidateId,
            Math.Clamp(finalScore, 0, 1),
            control,
            features);
    }

    private static double ScoreStrategy(
        ControlSnapshot control,
        SelectorStrategy strategy,
        IReadOnlyDictionary<string, ControlSnapshot> controls)
    {
        if (!MatchesAncestor(control, strategy.Ancestor, controls))
        {
            return 0;
        }

        var scores = new List<double>();
        if (!string.IsNullOrWhiteSpace(strategy.AutomationId))
        {
            scores.Add(string.Equals(
                control.AutomationId,
                strategy.AutomationId,
                StringComparison.Ordinal)
                ? 1
                : TextNormalizer.Similarity(control.AutomationId, strategy.AutomationId) * 0.7);
        }

        if (!string.IsNullOrWhiteSpace(strategy.Name))
        {
            scores.Add(string.Equals(control.Name, strategy.Name, StringComparison.Ordinal)
                ? 1
                : TextNormalizer.Similarity(control.Name, strategy.Name) * 0.8);
        }

        if (!string.IsNullOrWhiteSpace(strategy.ControlType))
        {
            scores.Add(string.Equals(
                control.ControlType,
                strategy.ControlType,
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0);
        }

        if (!string.IsNullOrWhiteSpace(strategy.ClassName))
        {
            scores.Add(string.Equals(
                control.ClassName,
                strategy.ClassName,
                StringComparison.Ordinal)
                ? 1
                : 0);
        }

        return scores.Count == 0 ? 0 : scores.Average() * strategy.Weight;
    }

    private static bool MatchesAncestor(
        ControlSnapshot control,
        ControlSelector? selector,
        IReadOnlyDictionary<string, ControlSnapshot> controls)
    {
        if (selector is null)
        {
            return true;
        }

        return EnumerateAncestors(control, controls)
            .Any(parent =>
                MatchesSnapshotSelector(parent, selector) &&
                MatchesAncestor(parent, selector.Ancestor, controls));
    }

    private static IEnumerable<ControlSnapshot> EnumerateAncestors(
        ControlSnapshot control,
        IReadOnlyDictionary<string, ControlSnapshot> controls,
        int remainingDepth = 20)
    {
        var parent = GetParent(control, controls);
        return remainingDepth <= 0 || parent is null
            ? []
            : new[] { parent }.Concat(
                EnumerateAncestors(parent, controls, remainingDepth - 1));
    }

    private static ControlSnapshot? GetParent(
        ControlSnapshot control,
        IReadOnlyDictionary<string, ControlSnapshot> controls)
        => control.ParentCandidateId is null
            ? null
            : controls.GetValueOrDefault(control.ParentCandidateId);

    private static bool MatchesSnapshotSelector(
        ControlSnapshot control,
        ControlSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.AutomationId) &&
            !string.Equals(
                control.AutomationId,
                selector.AutomationId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.Name) &&
            !string.Equals(control.Name, selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ControlType) &&
            !string.Equals(
                control.ControlType,
                selector.ControlType,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ClassName) &&
            !string.Equals(
                control.ClassName,
                selector.ClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        return !selector.IsEmpty;
    }

    private static double ScoreFingerprint(
        ControlSnapshot control,
        ControlFingerprint fingerprint)
    {
        var scores = new List<double>();
        if (!string.IsNullOrWhiteSpace(fingerprint.ControlType))
        {
            scores.Add(string.Equals(
                control.ControlType,
                fingerprint.ControlType,
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0);
        }

        scores.Add(TextNormalizer.Similarity(
            TextNormalizer.Tokens(control.Name),
            fingerprint.NameTokens));
        scores.Add(TextNormalizer.Similarity(
            control.NearbyLabels.SelectMany(label => TextNormalizer.Tokens(label.Text)),
            fingerprint.NearbyLabelTokens));
        scores.Add(TextNormalizer.Similarity(
            control.TreePath.SelectMany(TextNormalizer.Tokens),
            fingerprint.AncestorTokens));

        if (!string.IsNullOrWhiteSpace(fingerprint.ClassName))
        {
            scores.Add(string.Equals(
                control.ClassName,
                fingerprint.ClassName,
                StringComparison.Ordinal)
                ? 1
                : 0);
        }

        return scores.Count == 0 ? 0 : scores.Average();
    }

    private static void Add(
        ICollection<ResolutionFeatureScore> features,
        string name,
        double score,
        double weight,
        string evidence)
        => features.Add(new ResolutionFeatureScore(
            name,
            Math.Clamp(score, 0, 1),
            weight,
            evidence));

    private static ControlResolutionResult Empty(
        ResolutionStatus status,
        string semanticKey,
        SemanticTargetDefinition target,
        string viewKey,
        string reason)
        => new(
            status,
            semanticKey,
            target.Intent,
            viewKey,
            null,
            null,
            null,
            reason,
            []);
}
