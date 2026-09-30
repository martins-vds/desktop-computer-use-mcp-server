using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Resolution;

public static class ControlFingerprintFactory
{
    public static ControlFingerprint Create(
        ControlSnapshot control,
        string? applicationVersion = null)
        => new()
        {
            ControlType = control.ControlType,
            SupportedPatterns = control.SupportedPatterns
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            ClassName = control.ClassName,
            FrameworkId = control.FrameworkId,
            NameTokens = TextNormalizer.Tokens(control.Name),
            NearbyLabelTokens = control.NearbyLabels
                .SelectMany(label => TextNormalizer.Tokens(label.Text))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            AncestorTokens = control.TreePath
                .Take(Math.Max(0, control.TreePath.Count - 1))
                .SelectMany(TextNormalizer.Tokens)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            RelativeRegion = Region(control.RelativeBounds.X, control.RelativeBounds.Y),
            RelativeWidth = control.RelativeBounds.Width,
            RelativeHeight = control.RelativeBounds.Height,
            ApplicationVersion = applicationVersion
        };

    private static string Region(double x, double y)
    {
        var horizontal = x < 0.33 ? "left" : x < 0.66 ? "center" : "right";
        var vertical = y < 0.33 ? "top" : y < 0.66 ? "middle" : "bottom";
        return $"{vertical}-{horizontal}";
    }
}
