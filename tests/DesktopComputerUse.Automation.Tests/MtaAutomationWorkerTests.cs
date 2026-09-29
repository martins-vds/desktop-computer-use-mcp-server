using DesktopComputerUse.Automation.Threading;

namespace DesktopComputerUse.Automation.Tests;

public sealed class MtaAutomationWorkerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunAsync_serializes_actions_on_one_worker_thread()
    {
        await using var worker = new MtaAutomationWorker();
        using var releaseFirst = new ManualResetEventSlim();
        var firstStarted = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = 0;

        var first = worker.RunAsync(
            token =>
            {
                firstStarted.SetResult(Environment.CurrentManagedThreadId);
                releaseFirst.Wait(token);
                return Environment.CurrentManagedThreadId;
            },
            TestTimeout,
            CancellationToken.None);

        var firstThread = await firstStarted.Task.WaitAsync(TestTimeout);
        var second = worker.RunAsync(
            _ =>
            {
                Interlocked.Exchange(ref secondStarted, 1);
                return Environment.CurrentManagedThreadId;
            },
            TestTimeout,
            CancellationToken.None);

        Assert.False(second.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref secondStarted));

        releaseFirst.Set();

        Assert.Equal(firstThread, await first.WaitAsync(TestTimeout));
        Assert.Equal(firstThread, await second.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task RunAsync_cancels_queued_work_before_invoking_it()
    {
        await using var worker = new MtaAutomationWorker();
        using var releaseFirst = new ManualResetEventSlim();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        var first = worker.RunAsync(
            token =>
            {
                firstStarted.SetResult();
                releaseFirst.Wait(token);
                return true;
            },
            TestTimeout,
            CancellationToken.None);
        await firstStarted.Task.WaitAsync(TestTimeout);

        using var cancellation = new CancellationTokenSource();
        var queued = worker.RunAsync(
            _ =>
            {
                Interlocked.Increment(ref invocationCount);
                return true;
            },
            TestTimeout,
            cancellation.Token);

        cancellation.Cancel();
        releaseFirst.Set();
        await first.WaitAsync(TestTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queued.WaitAsync(TestTimeout));
        Assert.Equal(0, Volatile.Read(ref invocationCount));
    }

    [Fact]
    public async Task RunAsync_propagates_caller_cancellation_to_running_work()
    {
        await using var worker = new MtaAutomationWorker();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var task = worker.RunAsync(
            token =>
            {
                started.SetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return true;
            },
            TestTimeout,
            cancellation.Token);

        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task RunAsync_converts_cooperative_timeout_cancellation_to_timeout_exception()
    {
        await using var worker = new MtaAutomationWorker();

        var task = worker.RunAsync(
            token =>
            {
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return true;
            },
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => task.WaitAsync(TestTimeout));
        Assert.Contains("100 milliseconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_rejects_non_positive_timeouts()
    {
        await using var worker = new MtaAutomationWorker();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = worker.RunAsync(
                    _ => true,
                    TimeSpan.Zero,
                    CancellationToken.None);
            });
    }
}
