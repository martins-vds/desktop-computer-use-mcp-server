namespace DesktopComputerUse.Contracts.Configuration;

public sealed record NativeInputPolicy
{
    public bool Enabled { get; init; }

    public bool AllowMouse { get; init; } = true;

    public bool AllowKeyboard { get; init; }

    public IReadOnlyList<string> AllowedMouseButtons { get; init; } = ["left"];

    public string ConstrainTo { get; init; } = "clientArea";

    public bool RequireForeground { get; init; } = true;

    public bool ActivateBeforeInput { get; init; }

    public int MaximumTextLength { get; init; } = 500;

    public bool AllowSystemKeys { get; init; }

    public void Validate()
    {
        if (AllowKeyboard && !RequireForeground)
        {
            throw new ProfileValidationException(
                "Native keyboard input requires foreground verification.");
        }

        if (ActivateBeforeInput && !RequireForeground)
        {
            throw new ProfileValidationException(
                "Native activation before input requires foreground verification.");
        }

        if (MaximumTextLength is < 1 or > 10_000)
        {
            throw new ProfileValidationException(
                "Native input maximum text length must be between 1 and 10000.");
        }

        if (ConstrainTo is not ("clientArea" or "window"))
        {
            throw new ProfileValidationException(
                "Native input constrainTo must be clientArea or window.");
        }

        ValidateMouseButtons();
    }

    private void ValidateMouseButtons()
    {
        if (AllowedMouseButtons is null ||
            AllowedMouseButtons.Count is < 1 or > 3 ||
            AllowedMouseButtons.Any(button => button is not ("left" or "right" or "middle")) ||
            AllowedMouseButtons.Distinct(StringComparer.Ordinal).Count() != AllowedMouseButtons.Count)
        {
            throw new ProfileValidationException(
                "Native input allowed mouse buttons must be unique left, right, or middle values.");
        }
    }
}
