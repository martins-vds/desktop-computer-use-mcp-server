namespace DesktopComputerUse.Automation.Threading;

public interface IAutomationWorker : IAsyncDisposable
{
    Task<T> RunAsync<T>(
        Func<CancellationToken, T> action,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
