using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DesktopComputerUse.Automation.Selectors;

public sealed class ControlSelectorResolver
{
    public ControlSelector ExpandSemanticSelector(
        ApplicationProfile profile,
        ControlSelector selector)
    {
        if (string.IsNullOrWhiteSpace(selector.SemanticKey))
        {
            return selector;
        }

        if (!profile.SemanticSelectors.TryGetValue(selector.SemanticKey, out var configured))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                $"Semantic selector '{selector.SemanticKey}' is not defined by profile '{profile.Id}'.");
        }

        return configured with
        {
            SemanticKey = null,
            AutomationId = selector.AutomationId ?? configured.AutomationId,
            Name = selector.Name ?? configured.Name,
            ControlType = selector.ControlType ?? configured.ControlType,
            ClassName = selector.ClassName ?? configured.ClassName,
            Ancestor = selector.Ancestor ?? configured.Ancestor,
            Index = selector.Index ?? configured.Index
        };
    }

    public IReadOnlyList<AutomationElement> FindMatches(
        AutomationElement root,
        ControlSelector selector,
        int maxResults,
        int maxAncestorDepth)
    {
        if (selector.IsEmpty)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                "At least one selector field is required.");
        }

        var candidates = FindInitialCandidates(root, selector)
            .Where(element => Matches(element, selector, maxAncestorDepth))
            .Take(maxResults + 1)
            .ToArray();

        if (selector.Index is int index)
        {
            return index < candidates.Length
                ? [candidates[index]]
                : [];
        }

        return candidates;
    }

    private static IEnumerable<AutomationElement> FindInitialCandidates(
        AutomationElement root,
        ControlSelector selector)
    {
        AutomationElement[] descendants;
        if (!string.IsNullOrWhiteSpace(selector.AutomationId))
        {
            descendants = root.FindAllDescendants(
                factory => factory.ByAutomationId(selector.AutomationId));
        }
        else if (!string.IsNullOrWhiteSpace(selector.Name))
        {
            descendants = root.FindAllDescendants(
                factory => factory.ByName(selector.Name));
        }
        else if (TryParseControlType(selector.ControlType, out var controlType))
        {
            descendants = root.FindAllDescendants(
                factory => factory.ByControlType(controlType));
        }
        else if (!string.IsNullOrWhiteSpace(selector.ClassName))
        {
            descendants = root.FindAllDescendants(
                factory => factory.ByClassName(selector.ClassName));
        }
        else
        {
            descendants = root.FindAllDescendants();
        }

        if (MatchesWithoutAncestor(root, selector))
        {
            return descendants.Prepend(root);
        }

        return descendants;
    }

    private static bool Matches(
        AutomationElement element,
        ControlSelector selector,
        int maxAncestorDepth)
    {
        if (!MatchesWithoutAncestor(element, selector))
        {
            return false;
        }

        if (selector.Ancestor is null)
        {
            return true;
        }

        var ancestor = element.Parent;
        for (var depth = 0; ancestor is not null && depth < maxAncestorDepth; depth++)
        {
            if (MatchesWithoutAncestor(ancestor, selector.Ancestor))
            {
                return true;
            }

            ancestor = ancestor.Parent;
        }

        return false;
    }

    private static bool MatchesWithoutAncestor(
        AutomationElement element,
        ControlSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.AutomationId) &&
            !string.Equals(
                element.AutomationId,
                selector.AutomationId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.Name) &&
            !string.Equals(element.Name, selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ClassName) &&
            !string.Equals(
                element.ClassName,
                selector.ClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (TryParseControlType(selector.ControlType, out var controlType) &&
            element.ControlType != controlType)
        {
            return false;
        }

        return true;
    }

    private static bool TryParseControlType(string? value, out ControlType controlType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            controlType = ControlType.Unknown;
            return false;
        }

        if (!Enum.TryParse(value, true, out controlType))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.InvalidProfile,
                $"Unknown control type '{value}'.");
        }

        return true;
    }
}
