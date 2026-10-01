using System.Diagnostics;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Applications;

public interface IProcessLifecycleHandle
{
    int Id { get; }
    bool HasExited { get; }
    bool CloseMainWindow();
    bool WaitForExit(int milliseconds);
    void Kill(bool entireProcessTree);
}

public interface IProcessInspectionHandle : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    string? ExecutablePath { get; }
    string? MainWindowTitle { get; }
}

public sealed record ProcessCleanupResult(
    int ProcessId,
    string Outcome,
    IReadOnlyList<AutomationDiagnostic> Failures)
{
    public bool Succeeded => Outcome is "alreadyExited" or "closed" or "killed" or "notOwned" or "notRequested";
}

public static class ProcessLifecycle
{
    public static IReadOnlyList<MatchingApplicationProcess> FindMatchingProcesses(string executablePath)
        => FindMatchingProcesses(
            executablePath,
            Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath))
                .Select(process => new SystemProcessInspectionHandle(process))
                .ToArray());

    public static IReadOnlyList<MatchingApplicationProcess> FindMatchingProcesses(
        string executablePath,
        IReadOnlyList<IProcessInspectionHandle> processes)
    {
        var matches = new List<MatchingApplicationProcess>();
        try
        {
            foreach (var process in processes)
            {
                var match = InspectProcess(process, executablePath);
                if (match is not null)
                    matches.Add(match);
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return matches.OrderBy(match => match.ProcessId).ToArray();
    }

    private static MatchingApplicationProcess? InspectProcess(IProcessInspectionHandle process, string executablePath)
    {
        try
        {
            return ReadMatchingProcess(process, executablePath);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return null;
        }
        catch (Exception exception)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.AccessDenied,
                $"Cannot safely determine whether process {process.Id} matches the requested executable.",
                diagnostic: AutomationExceptionResultMapper.CreateDiagnostic(exception) with
                {
                    Code = AutomationErrorCode.AccessDenied,
                    Operation = "launch",
                    Phase = "verifyExistingProcess",
                    ProcessId = process.Id
                });
        }
    }

    private static MatchingApplicationProcess? ReadMatchingProcess(IProcessInspectionHandle process, string executablePath)
    {
        if (process.HasExited)
            return null;
        var actualPath = process.ExecutablePath;
        if (string.IsNullOrWhiteSpace(actualPath))
            throw new UnauthorizedAccessException("The process executable path could not be verified.");
        return PathsMatch(actualPath, executablePath)
            ? new(process.Id, actualPath!, process.MainWindowTitle)
            : null;
    }

    public static bool ShouldTerminateProcess(bool terminateRequested, bool ownsProcess, bool hasOwnedHandle)
        => terminateRequested && ownsProcess && hasOwnedHandle;

    public static bool HasCleanupFailures(ProcessCleanupResult cleanup)
        => !cleanup.Succeeded || cleanup.Failures.Count > 0;

    public static AutomationOperationException StartupFailure(
        Exception exception,
        bool callerCancelled,
        string profileId,
        int processId,
        bool ownsProcess,
        ProcessCleanupResult cleanup)
    {
        var original = AutomationExceptionResultMapper.Map<ApplicationState>(exception, callerCancelled).Error!;
        var diagnostic = original.Diagnostic! with
        {
            Operation = original.Diagnostic!.Operation ?? (ownsProcess ? "launch" : "attach"),
            Phase = original.Diagnostic!.Phase ?? "startSession",
            ProfileId = profileId,
            ProcessId = processId,
            CleanupOutcome = cleanup.Outcome
        };
        return new AutomationOperationException(
            original.Code,
            original.Message,
            original.Candidates,
            diagnostic,
            original.Failures.Concat(cleanup.Failures).ToArray());
    }

    public static int? SelectExistingProcess(
        LaunchPolicy policy,
        bool allowMultipleInstances,
        IReadOnlyList<MatchingApplicationProcess> matches)
    {
        if (!Enum.IsDefined(policy))
            throw new AutomationOperationException(AutomationErrorCode.InvalidProfile, "Unknown launch policy.");
        if (policy == LaunchPolicy.LaunchNew)
        {
            if (!allowMultipleInstances)
                throw new AutomationOperationException(
                    AutomationErrorCode.ApplicationNotAllowed,
                    "Launching a new instance requires AllowMultipleInstances in the application profile.");
            return null;
        }

        if (matches.Count == 0)
            return null;
        if (policy == LaunchPolicy.Attach && matches.Count == 1)
            return matches[0].ProcessId;

        throw new AutomationOperationException(
            AutomationErrorCode.ApplicationAlreadyAttached,
            policy == LaunchPolicy.Attach
                ? "Multiple matching processes exist; attach requires one unambiguous verified executable."
                : "The application is already running; select attach or explicitly permitted launchNew.",
            diagnostic: new AutomationDiagnostic
            {
                Code = AutomationErrorCode.ApplicationAlreadyAttached,
                Operation = "launch",
                Phase = "checkExistingProcesses",
                MatchingProcesses = matches
            });
    }

    public static bool PathsMatch(string? actualPath, string expectedPath)
        => !string.IsNullOrWhiteSpace(actualPath) &&
            string.Equals(
                Path.GetFullPath(actualPath),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase);

    public static ProcessCleanupResult CleanupOwnedProcess(
        Process process,
        TimeSpan gracefulTimeout,
        TimeSpan killTimeout)
        => Cleanup(new SystemProcessHandle(process), true, gracefulTimeout, killTimeout);

    public static ProcessCleanupResult Cleanup(
        IProcessLifecycleHandle process,
        bool ownsProcess,
        TimeSpan gracefulTimeout,
        TimeSpan killTimeout)
    {
        var failures = new List<AutomationDiagnostic>();
        var processId = process.Id;
        if (!ownsProcess)
            return new(processId, "notOwned", failures);

        try
        {
            if (process.HasExited)
                return new(processId, "alreadyExited", failures);
            process.CloseMainWindow();
            if (process.WaitForExit(ToMilliseconds(gracefulTimeout)))
                return new(processId, "closed", failures);
        }
        catch (Exception exception)
        {
            failures.Add(CleanupDiagnostic(exception, processId, "gracefulClose"));
        }

        try
        {
            if (process.HasExited)
                return new(processId, "closed", failures);
            process.Kill(entireProcessTree: true);
            if (process.WaitForExit(ToMilliseconds(killTimeout)))
                return new(processId, "killed", failures);
            failures.Add(CleanupDiagnostic(
                new TimeoutException("The owned process did not exit within the bounded kill timeout."),
                processId,
                "waitForKilledProcess"));
        }
        catch (Exception exception)
        {
            failures.Add(CleanupDiagnostic(exception, processId, "killOwnedProcessTree"));
        }

        return new(processId, "failed", failures);
    }

    private static int ToMilliseconds(TimeSpan timeout)
        => (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);

    private static AutomationDiagnostic CleanupDiagnostic(Exception exception, int processId, string phase)
        => AutomationExceptionResultMapper.CreateDiagnostic(exception) with
        {
            Operation = "cleanup",
            Phase = phase,
            ProcessId = processId
        };

    private sealed class SystemProcessHandle(Process process) : IProcessLifecycleHandle
    {
        public int Id => process.Id;
        public bool HasExited => process.HasExited;
        public bool CloseMainWindow() => process.CloseMainWindow();
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);
    }

    private sealed class SystemProcessInspectionHandle(Process process) : IProcessInspectionHandle
    {
        public int Id => process.Id;
        public bool HasExited => process.HasExited;
        public string? ExecutablePath => process.MainModule?.FileName;
        public string? MainWindowTitle => process.MainWindowTitle;
        public void Dispose() => process.Dispose();
    }
}
