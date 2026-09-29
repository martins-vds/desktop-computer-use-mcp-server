using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Applications;

public interface IApplicationProfileStore
{
    IReadOnlyList<ApplicationProfileSummary> List();

    bool TryGet(string profileId, out ApplicationProfile profile);
}
