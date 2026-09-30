namespace DesktopComputerUse.Contracts.Profiles;

public sealed record ProfileUpdatePatch(
    string ProfileId,
    string ProposalId,
    string SemanticKey,
    SemanticTargetDefinition ProposedTarget);
