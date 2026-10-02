using System.Drawing;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using FlaUI.Core.AutomationElements;

namespace DesktopComputerUse.Automation;

internal static class CapturePrivacy
{
    internal static PhysicalScreenRect ToPhysicalRegion(RectangleInfo bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw InvalidSensitiveRegion();
        }

        return RoundRegionOutward(bounds);
    }

    private static PhysicalScreenRect RoundRegionOutward(RectangleInfo bounds)
    {
        try
        {
            var left = checked((int)Math.Floor(bounds.X));
            var top = checked((int)Math.Floor(bounds.Y));
            var right = checked((int)Math.Ceiling(bounds.X + bounds.Width));
            var bottom = checked((int)Math.Ceiling(bounds.Y + bounds.Height));
            return new(left, top, checked(right - left), checked(bottom - top));
        }
        catch (OverflowException)
        {
            throw InvalidSensitiveRegion();
        }
    }

    private static NativeOperationException InvalidSensitiveRegion()
        => new(NativeFailureCode.SensitiveGeometryUnavailable, "captureWindow", "mapRedactions",
            "A sensitive region has no finite, valid physical bounds.");

    public static IReadOnlyList<RectangleInfo> ReadSensitiveBounds(
        AutomationElement root,
        ApplicationProfile profile,
        CancellationToken cancellationToken)
    {
        return ReadWithPolicy(profile, () => ReadSensitiveBounds(
            root, profile.MaxResults,
            element => IsSensitive(element, profile),
            element => RequireSensitiveBounds(element.BoundingRectangle),
            element => element.FindAllChildren(),
            cancellationToken), cancellationToken);
    }

    internal static IReadOnlyList<RectangleInfo> ReadWithPolicy(
        ApplicationProfile profile,
        Func<IReadOnlyList<RectangleInfo>> readSensitiveBounds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!profile.EnableScreenshots)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                "Image capture is disabled by the application profile.");
        }

        return profile.PrivacyMode ? readSensitiveBounds() : [];
    }

    internal static IReadOnlyList<RectangleInfo> ReadSensitiveBounds<T>(
        T root,
        int maximumElements,
        Func<T, bool> isSensitive,
        Func<T, RectangleInfo> readBounds,
        Func<T, IReadOnlyList<T>> readChildren,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<T>();
        pending.Push(root);
        var bounds = new List<RectangleInfo>();
        var inspected = 0;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++inspected > maximumElements)
            {
                throw new AutomationOperationException(
                    AutomationErrorCode.ApplicationNotAllowed,
                    "Capture refused because sensitive-control discovery exceeded the profile element limit.");
            }

            ReadElement(pending.Pop(), isSensitive, readBounds, readChildren, pending, bounds);
        }

        return bounds;
    }

    private static void ReadElement<T>(
        T element,
        Func<T, bool> isSensitive,
        Func<T, RectangleInfo> readBounds,
        Func<T, IReadOnlyList<T>> readChildren,
        Stack<T> pending,
        List<RectangleInfo> bounds)
    {
        try
        {
            AddSensitiveBounds(element, isSensitive, readBounds, bounds);
            EnqueueChildren(readChildren(element), pending);
        }

        catch (Exception exception) when (RequiresRedactionFailure(exception))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                $"Capture refused because complete redaction could not be established ({exception.GetType().Name}).");
        }
    }

    private static bool RequiresRedactionFailure(Exception exception)
        => exception is not AutomationOperationException and not OperationCanceledException;

    private static void AddSensitiveBounds<T>(
        T element, Func<T, bool> isSensitive, Func<T, RectangleInfo> readBounds,
        List<RectangleInfo> bounds)
    {
        if (isSensitive(element))
        {
            bounds.Add(readBounds(element));
        }
    }

    private static void EnqueueChildren<T>(IReadOnlyList<T> children, Stack<T> pending)
    {
        foreach (var child in children)
        {
            pending.Push(child);
        }
    }

    private static bool IsSensitive(AutomationElement element, ApplicationProfile profile)
        => element.Properties.IsPassword.Value ||
            profile.SensitiveAutomationIds.Contains(element.AutomationId ?? string.Empty);

    private static RectangleInfo RequireSensitiveBounds(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                "Capture refused because a sensitive control has no reliable physical bounds.");
        }

        return new RectangleInfo(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
