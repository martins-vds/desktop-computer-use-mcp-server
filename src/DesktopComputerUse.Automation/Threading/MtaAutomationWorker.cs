using System.Collections.Concurrent;

namespace DesktopComputerUse.Automation.Threading;

public sealed class MtaAutomationWorker : IAutomationWorker
{
    private readonly BlockingCollection<IWorkItem> _queue = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _thread;
    private bool _disposed;

    public MtaAutomationWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DesktopComputerUse.UIAutomation"
        };

        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.MTA);
        }

        _thread.Start();
    }

    public Task<T> RunAsync<T>(
        Func<CancellationToken, T> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(action);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var item = new WorkItem<T>(action, timeout, cancellationToken, _shutdown.Token);
        _queue.Add(item, cancellationToken);
        return item.Task;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _shutdown.Cancel();
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }

    private void Run()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable(_shutdown.Token))
            {
                item.Execute();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<CancellationToken, T> _action;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _callerToken;
        private readonly CancellationToken _shutdownToken;
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkItem(
            Func<CancellationToken, T> action,
            TimeSpan timeout,
            CancellationToken callerToken,
            CancellationToken shutdownToken)
        {
            _action = action;
            _timeout = timeout;
            _callerToken = callerToken;
            _shutdownToken = shutdownToken;
        }

        public Task<T> Task => _completion.Task;

        public void Execute()
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _callerToken,
                _shutdownToken);
            linked.CancelAfter(_timeout);

            try
            {
                linked.Token.ThrowIfCancellationRequested();
                _completion.TrySetResult(_action(linked.Token));
            }
            catch (OperationCanceledException) when (_callerToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(_callerToken);
            }
            catch (OperationCanceledException) when (_shutdownToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(_shutdownToken);
            }
            catch (OperationCanceledException exception)
            {
                _completion.TrySetException(new TimeoutException(
                    $"The automation operation exceeded {_timeout.TotalMilliseconds:0} milliseconds.",
                    exception));
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }
    }
}
