using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;

namespace DesktopComputerUse.Automation.Discovery;

public static class NearbyLabelGeometry
{
    public static NearbyLabel? Create(
        string candidateId,
        string text,
        RectangleInfo label,
        RectangleInfo target)
        => CreateLeft(candidateId, text, label, target)
            ?? CreateAbove(candidateId, text, label, target);

    private static NearbyLabel? CreateLeft(
        string candidateId,
        string text,
        RectangleInfo label,
        RectangleInfo target)
    {
        var verticalOverlap = OverlapRatio(
            label.Y,
            label.Y + label.Height,
            target.Y,
            target.Y + target.Height);
        if (label.X + label.Width > target.X || verticalOverlap < 0.35)
        {
            return null;
        }

        var distance = target.X - label.X - label.Width;
        return distance <= Math.Max(300, target.Width * 3)
            ? new NearbyLabel(
                candidateId,
                text,
                "left",
                distance,
                Math.Clamp(0.9 - distance / 600d, 0.4, 0.9))
            : null;
    }

    private static NearbyLabel? CreateAbove(
        string candidateId,
        string text,
        RectangleInfo label,
        RectangleInfo target)
    {
        var horizontalOverlap = OverlapRatio(
            label.X,
            label.X + label.Width,
            target.X,
            target.X + target.Width);
        if (label.Y + label.Height > target.Y || horizontalOverlap < 0.35)
        {
            return null;
        }

        var distance = target.Y - label.Y - label.Height;
        return distance <= Math.Max(160, target.Height * 4)
            ? new NearbyLabel(
                candidateId,
                text,
                "above",
                distance,
                Math.Clamp(0.8 - distance / 400d, 0.35, 0.8))
            : null;
    }

    private static double OverlapRatio(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd)
    {
        var overlap = Math.Max(
            0,
            Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
        var shortest = Math.Min(
            firstEnd - firstStart,
            secondEnd - secondStart);
        return shortest <= 0 ? 0 : overlap / shortest;
    }
}
