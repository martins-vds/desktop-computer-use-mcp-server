using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Applications;

public interface IReloadableApplicationProfileStore : IApplicationProfileStore
{
    long Generation { get; }

    ApplicationProfileReloadResult? LastReloadResult { get; }

    ApplicationProfileReloadResult Reload();

    bool IsStale(ApplicationProfile profile);
}
