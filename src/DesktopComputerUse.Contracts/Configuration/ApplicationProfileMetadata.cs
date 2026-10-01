namespace DesktopComputerUse.Contracts.Configuration;

public enum ProfileValidationStatus
{
    Valid,
    Invalid
}

public sealed record ApplicationProfileMetadata(
    string? SourceFile,
    DateTimeOffset LoadedAtUtc,
    DateTimeOffset? SourceLastWriteTimeUtc,
    string Revision,
    long Generation,
    ProfileValidationStatus ValidationStatus = ProfileValidationStatus.Valid);
