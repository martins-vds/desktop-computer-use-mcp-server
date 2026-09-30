namespace DesktopComputerUse.Contracts.Profiles;

public sealed record ViewSignature(
    string Key,
    string Hash,
    IReadOnlyList<string> TitleTokens,
    IReadOnlyList<string> VisibleAutomationIds,
    IReadOnlyList<string> VisibleNames);
