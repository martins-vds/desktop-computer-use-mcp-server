using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Automation;
using System.Diagnostics;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ProcessLifecycleTests
{
    private static readonly MatchingApplicationProcess Match = new(42, @"C:\app.exe", "Application");

    [Theory]
    [InlineData(LaunchPolicy.Fail)]
    [InlineData(LaunchPolicy.Attach)]
    public void No_existing_process_launches_normally(LaunchPolicy policy)
        => Assert.Null(ProcessLifecycle.SelectExistingProcess(policy, false, []));

    [Fact]
    public void Default_policy_refuses_existing_process_and_reports_candidates()
    {
        var exception = Assert.Throws<AutomationOperationException>(
            () => ProcessLifecycle.SelectExistingProcess(LaunchPolicy.Fail, false, [Match]));

        Assert.Equal(AutomationErrorCode.ApplicationAlreadyAttached, exception.Code);
        Assert.Equal(Match, Assert.Single(exception.Diagnostic!.MatchingProcesses));
        Assert.Equal("launch", exception.Diagnostic.Operation);
        Assert.Equal("checkExistingProcesses", exception.Diagnostic.Phase);
        Assert.Contains("already running", exception.Message);
    }

    [Fact]
    public void Attach_requires_one_verified_match()
    {
        Assert.Equal(42, ProcessLifecycle.SelectExistingProcess(LaunchPolicy.Attach, false, [Match]));
        var exception = Assert.Throws<AutomationOperationException>(
            () => ProcessLifecycle.SelectExistingProcess(LaunchPolicy.Attach, false, [Match, Match with { ProcessId = 43 }]));
        Assert.Equal(2, exception.Diagnostic!.MatchingProcesses.Count);
        Assert.Contains("one unambiguous", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Launch_new_requires_permission_even_without_existing_processes(bool alreadyRunning)
    {
        var exception = Assert.Throws<AutomationOperationException>(
            () => ProcessLifecycle.SelectExistingProcess(
                LaunchPolicy.LaunchNew, false, alreadyRunning ? [Match] : []));
        Assert.Equal(AutomationErrorCode.ApplicationNotAllowed, exception.Code);
        Assert.Contains("AllowMultipleInstances", exception.Message);
    }

    [Fact]
    public void Explicit_permission_allows_launch_new()
    {
        Assert.Null(ProcessLifecycle.SelectExistingProcess(LaunchPolicy.LaunchNew, true, [Match]));
        Assert.Null(ProcessLifecycle.SelectExistingProcess(LaunchPolicy.LaunchNew, true, []));
    }

    [Fact]
    public void Undefined_policy_is_rejected()
    {
        var exception = Assert.Throws<AutomationOperationException>(
            () => ProcessLifecycle.SelectExistingProcess((LaunchPolicy)999, true, []));
        Assert.Equal(AutomationErrorCode.InvalidProfile, exception.Code);
        Assert.Contains("Unknown launch policy", exception.Message);
    }

    [Fact]
    public void Paths_require_a_known_verified_executable()
    {
        Assert.False(ProcessLifecycle.PathsMatch(null, "application.exe"));
        Assert.False(ProcessLifecycle.PathsMatch("", "application.exe"));
        Assert.False(ProcessLifecycle.PathsMatch("  ", "application.exe"));
        Assert.False(ProcessLifecycle.PathsMatch("other.exe", "application.exe"));
        Assert.True(ProcessLifecycle.PathsMatch(Path.GetFullPath("application.exe"), "application.exe"));
        Assert.True(ProcessLifecycle.PathsMatch("APPLICATION.exe", "application.exe"));
        Assert.True(ProcessLifecycle.PathsMatch("./application.exe", "./child/../application.exe"));
        Assert.False(ProcessLifecycle.PathsMatch("application.exe.other", "application.exe"));
    }

    [Fact]
    public void Existing_processes_require_exact_executable_paths_and_are_sorted_and_disposed()
    {
        var second = new FakeInspection(43, "application.exe", "Second");
        var other = new FakeInspection(44, "other.exe", "Not matching");
        var first = new FakeInspection(42, "./application.exe", "First");
        var exited = new FakeInspection(45, "application.exe", "Exited") { Exited = true };
        var matches = ProcessLifecycle.FindMatchingProcesses("application.exe", [second, other, first, exited]);

        Assert.Equal([42, 43], matches.Select(match => match.ProcessId));
        Assert.Equal(["First", "Second"], matches.Select(match => match.MainWindowTitle));
        Assert.Equal("./application.exe", matches[0].ExecutablePath);
        Assert.Equal("application.exe", matches[1].ExecutablePath);
        Assert.All(new[] { second, other, first, exited }, process => Assert.True(process.Disposed));
        Assert.False(exited.PathRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Unknown_existing_executable_fails_closed_and_disposes_all_handles(string? path)
    {
        var inaccessible = new FakeInspection(42, path, "Unknown");
        var remaining = new FakeInspection(43, "application.exe", "Remaining");
        var exception = Assert.Throws<AutomationOperationException>(
            () => ProcessLifecycle.FindMatchingProcesses("application.exe", [inaccessible, remaining]));

        Assert.Equal(AutomationErrorCode.AccessDenied, exception.Code);
        Assert.Equal(AutomationErrorCode.AccessDenied, exception.Diagnostic!.Code);
        Assert.Equal("launch", exception.Diagnostic.Operation);
        Assert.Equal("verifyExistingProcess", exception.Diagnostic.Phase);
        Assert.Equal(42, exception.Diagnostic.ProcessId);
        Assert.Equal(nameof(UnauthorizedAccessException), exception.Diagnostic.ExceptionType);
        Assert.True(inaccessible.Disposed);
        Assert.True(remaining.Disposed);
        Assert.False(remaining.PathRead);
    }

    [Fact]
    public void Access_denied_during_executable_verification_is_not_a_match()
    {
        var denied = new FakeInspection(42, "application.exe", "Denied")
        {
            PathException = new UnauthorizedAccessException("denied")
        };
        var exception = Assert.Throws<AutomationOperationException>(
            () => ProcessLifecycle.FindMatchingProcesses("application.exe", [denied]));
        Assert.Equal(AutomationErrorCode.AccessDenied, exception.Code);
        Assert.Equal(nameof(UnauthorizedAccessException), exception.Diagnostic!.ExceptionType);
        Assert.Contains("process 42", exception.Message);
        Assert.True(denied.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exit_during_executable_verification_is_ignored_only_if_process_really_exited(bool exits)
    {
        var process = new FakeInspection(42, "application.exe", "Exiting")
        {
            PathException = new InvalidOperationException("cannot read"),
            ExitOnPathRead = exits
        };
        if (exits)
            Assert.Empty(ProcessLifecycle.FindMatchingProcesses("application.exe", [process]));
        else
            Assert.Throws<AutomationOperationException>(
                () => ProcessLifecycle.FindMatchingProcesses("application.exe", [process]));
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void Termination_requires_request_ownership_and_retained_process_handle(
        bool requested, bool owned, bool retained, bool expected)
        => Assert.Equal(expected, ProcessLifecycle.ShouldTerminateProcess(requested, owned, retained));

    [Theory]
    [InlineData("alreadyExited", true)]
    [InlineData("closed", true)]
    [InlineData("killed", true)]
    [InlineData("notOwned", true)]
    [InlineData("notRequested", true)]
    [InlineData("failed", false)]
    [InlineData("unknown", false)]
    public void Cleanup_outcomes_are_classified_exactly(string outcome, bool succeeded)
    {
        var cleanup = new ProcessCleanupResult(42, outcome, []);
        Assert.Equal(succeeded, cleanup.Succeeded);
        Assert.Equal(!succeeded, ProcessLifecycle.HasCleanupFailures(cleanup));
        Assert.True(ProcessLifecycle.HasCleanupFailures(
            cleanup with { Failures = [new AutomationDiagnostic { ProcessId = 42 }] }));
    }

    [Fact]
    public void Attached_process_is_never_inspected_closed_or_killed()
    {
        var process = new FakeProcess { ThrowOnReadExited = true };
        var result = Cleanup(process, ownsProcess: false);

        Assert.Equal("notOwned", result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Empty(process.Calls);
    }

    [Fact]
    public void Exited_owned_process_needs_no_termination()
    {
        var process = new FakeProcess { Exited = true };
        var result = Cleanup(process);

        Assert.Equal("alreadyExited", result.Outcome);
        Assert.Equal(["hasExited"], process.Calls);
    }

    [Fact]
    public void Real_owned_process_adapter_keeps_ownership_even_after_process_exits()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        try
        {
            Assert.True(process.WaitForExit(10_000));
            var cleanup = ProcessLifecycle.CleanupOwnedProcess(
                process, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200));
            Assert.Equal(process.Id, cleanup.ProcessId);
            Assert.Equal("alreadyExited", cleanup.Outcome);
            Assert.True(cleanup.Succeeded);
            Assert.Empty(cleanup.Failures);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                Assert.True(process.WaitForExit(2_000));
            }
        }
    }

    [Fact]
    public void Graceful_close_waits_with_bound_and_does_not_kill()
    {
        var process = new FakeProcess { GracefulExit = true };
        var result = Cleanup(process);

        Assert.Equal("closed", result.Outcome);
        Assert.Equal(["hasExited", "close", "wait:100"], process.Calls);
    }

    [Fact]
    public void Hung_owned_process_is_killed_as_tree_with_bounded_wait()
    {
        var process = new FakeProcess();
        var result = Cleanup(process);

        Assert.Equal("killed", result.Outcome);
        Assert.Equal(42, result.ProcessId);
        Assert.Equal(["hasExited", "close", "wait:100", "hasExited", "killTree", "wait:200"], process.Calls);
    }

    [Fact]
    public void Graceful_close_error_is_reported_and_kill_is_still_attempted()
    {
        var process = new FakeProcess { CloseException = new UnauthorizedAccessException("denied") };
        var result = Cleanup(process);

        Assert.True(result.Succeeded);
        Assert.Equal("killed", result.Outcome);
        var diagnostic = Assert.Single(result.Failures);
        Assert.Equal("gracefulClose", diagnostic.Phase);
        Assert.Equal(AutomationErrorCode.AccessDenied, diagnostic.Code);
        Assert.Equal("cleanup", diagnostic.Operation);
        Assert.Equal(nameof(UnauthorizedAccessException), diagnostic.ExceptionType);
        Assert.NotNull(diagnostic.HResult);
        Assert.Contains("killTree", process.Calls);
    }

    [Fact]
    public void Kill_failure_is_explicit_with_all_cleanup_failures_preserved()
    {
        var process = new FakeProcess
        {
            CloseException = new InvalidOperationException("cannot close"),
            KillException = new UnauthorizedAccessException("cannot kill")
        };
        var result = Cleanup(process);

        Assert.False(result.Succeeded);
        Assert.Equal("failed", result.Outcome);
        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, diagnostic => Assert.Equal(42, diagnostic.ProcessId));
        Assert.Equal("killOwnedProcessTree", result.Failures[1].Phase);
    }

    [Fact]
    public void Kill_wait_timeout_is_reported()
    {
        var process = new FakeProcess { KilledExit = false };
        var result = Cleanup(process);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Failures);
        Assert.Equal(AutomationErrorCode.Timeout, diagnostic.Code);
        Assert.Equal("waitForKilledProcess", diagnostic.Phase);
        Assert.Equal(nameof(TimeoutException), diagnostic.ExceptionType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wait_failure_is_reported_without_losing_the_cleanup_stage(bool killedWait)
    {
        var process = new FakeProcess
        {
            WaitException = new UnauthorizedAccessException("wait denied"),
            ThrowDuringKilledWait = killedWait
        };
        var result = Cleanup(process);
        Assert.Equal(!killedWait, result.Succeeded);
        Assert.Equal(killedWait ? "failed" : "killed", result.Outcome);
        var diagnostic = Assert.Single(result.Failures);
        Assert.Equal(killedWait ? "killOwnedProcessTree" : "gracefulClose", diagnostic.Phase);
        Assert.Equal(AutomationErrorCode.AccessDenied, diagnostic.Code);
    }

    [Fact]
    public void Unreadable_exit_state_reports_both_cleanup_attempts()
    {
        var process = new FakeProcess { ThrowOnReadExited = true };
        var result = Cleanup(process);
        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Failures.Count);
        Assert.Equal(["hasExited", "hasExited"], process.Calls);
        Assert.DoesNotContain("killTree", process.Calls);
    }

    [Fact]
    public void Wait_bounds_are_clamped_without_overflow()
    {
        var process = new FakeProcess();
        var result = ProcessLifecycle.Cleanup(process, true, TimeSpan.FromMilliseconds(-1), TimeSpan.MaxValue);
        Assert.True(result.Succeeded);
        Assert.Contains("wait:0", process.Calls);
        Assert.Contains($"wait:{int.MaxValue}", process.Calls);
    }

    [Fact]
    public void Exit_during_graceful_failure_does_not_kill_a_reused_pid()
    {
        var process = new FakeProcess
        {
            CloseException = new InvalidOperationException("already exited"),
            ExitOnClose = true
        };
        var result = Cleanup(process);

        Assert.Equal("closed", result.Outcome);
        Assert.DoesNotContain("killTree", process.Calls);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Startup_error_preserves_original_failure_with_spawned_pid_and_cleanup_details()
    {
        var original = new AutomationOperationException(AutomationErrorCode.WindowNotFound, "No matching window");
        var cleanupFailure = new AutomationDiagnostic { Operation = "cleanup", Phase = "kill", ProcessId = 42 };
        var exception = ProcessLifecycle.StartupFailure(
            original, false, "profile", 42, true, new(42, "failed", [cleanupFailure]));
        var mapped = AutomationExceptionResultMapper.Map<ApplicationState>(exception, false);

        Assert.Equal(AutomationErrorCode.WindowNotFound, mapped.Error!.Code);
        Assert.Equal("No matching window", mapped.Error.Message);
        Assert.Equal(42, mapped.Error.Diagnostic!.ProcessId);
        Assert.Equal("profile", mapped.Error.Diagnostic.ProfileId);
        Assert.Equal("startSession", mapped.Error.Diagnostic.Phase);
        Assert.Equal("failed", mapped.Error.Diagnostic.CleanupOutcome);
        Assert.Equal(nameof(AutomationOperationException), mapped.Error.Diagnostic.ExceptionType);
        Assert.Equal(cleanupFailure, Assert.Single(mapped.Error.Failures));
    }

    [Theory]
    [InlineData(false, AutomationErrorCode.Timeout)]
    [InlineData(true, AutomationErrorCode.OperationCancelled)]
    public void Cancelled_startup_retains_timeout_versus_caller_cancellation(
        bool callerCancelled, AutomationErrorCode expected)
    {
        var exception = ProcessLifecycle.StartupFailure(
            new OperationCanceledException(), callerCancelled, "profile", 42, true, new(42, "killed", []));
        Assert.Equal(expected, exception.Code);
        Assert.Equal("killed", exception.Diagnostic!.CleanupOutcome);
    }

    [Fact]
    public void Factory_failure_retains_original_exception_metadata_after_cleanup()
    {
        var original = new InvalidOperationException("factory failed");
        var exception = ProcessLifecycle.StartupFailure(original, false, "profile", 42, true, new(42, "killed", []));
        Assert.Equal(nameof(InvalidOperationException), exception.Diagnostic!.ExceptionType);
        Assert.Equal($"0x{unchecked((uint)original.HResult):X8}", exception.Diagnostic.HResult);
        Assert.Equal(42, exception.Diagnostic.ProcessId);
        Assert.Equal("launch", exception.Diagnostic.Operation);
    }

    [Fact]
    public void Attach_failure_preserves_candidates_and_prior_diagnostics_without_claiming_ownership()
    {
        var prior = new AutomationDiagnostic { Phase = "original" };
        var cleanupFailure = new AutomationDiagnostic { Phase = "disposeApplication" };
        var original = new AutomationOperationException(
            AutomationErrorCode.AmbiguousControl,
            "ambiguous",
            [],
            new AutomationDiagnostic
            {
                Operation = "readWindow",
                Phase = "readProperty",
                HResult = "0x12345678",
                Property = "OriginalProperty"
            },
            [prior]);
        var exception = ProcessLifecycle.StartupFailure(
            original, false, "attached-profile", 77, false, new(77, "notOwned", [cleanupFailure]));
        Assert.Equal(AutomationErrorCode.AmbiguousControl, exception.Code);
        Assert.Equal("ambiguous", exception.Message);
        Assert.Same(original.Candidates, exception.Candidates);
        Assert.Equal([prior, cleanupFailure], exception.Failures);
        Assert.Equal("readWindow", exception.Diagnostic!.Operation);
        Assert.Equal("readProperty", exception.Diagnostic.Phase);
        Assert.Equal("notOwned", exception.Diagnostic.CleanupOutcome);
        Assert.Equal("attached-profile", exception.Diagnostic.ProfileId);
        Assert.Equal(77, exception.Diagnostic.ProcessId);
        Assert.Equal("0x12345678", exception.Diagnostic.HResult);
        Assert.Equal("OriginalProperty", exception.Diagnostic.Property);
    }

    [Fact]
    public void Attach_failure_without_original_context_gets_attach_startup_context()
    {
        var exception = ProcessLifecycle.StartupFailure(
            new InvalidOperationException(), false, "profile", 42, false, new(42, "notOwned", []));
        Assert.Equal("attach", exception.Diagnostic!.Operation);
        Assert.Equal("startSession", exception.Diagnostic.Phase);
        Assert.Equal("notOwned", exception.Diagnostic.CleanupOutcome);
    }

    private static ProcessCleanupResult Cleanup(FakeProcess process, bool ownsProcess = true)
        => ProcessLifecycle.Cleanup(process, ownsProcess, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200));

    private sealed class FakeProcess : IProcessLifecycleHandle
    {
        public List<string> Calls { get; } = [];
        public int Id => 42;
        public bool Exited { get; set; }
        public bool ThrowOnReadExited { get; init; }
        public bool GracefulExit { get; init; }
        public bool KilledExit { get; init; } = true;
        public bool ExitOnClose { get; init; }
        public Exception? CloseException { get; init; }
        public Exception? KillException { get; init; }
        public Exception? WaitException { get; init; }
        public bool ThrowDuringKilledWait { get; init; }
        private bool _killed;

        public bool HasExited
        {
            get
            {
                Calls.Add("hasExited");
                if (ThrowOnReadExited)
                    throw new InvalidOperationException("attached process must not be touched");
                return Exited;
            }
        }

        public bool CloseMainWindow()
        {
            Calls.Add("close");
            Exited |= ExitOnClose;
            if (CloseException is not null)
                throw CloseException;
            return true;
        }

        public bool WaitForExit(int milliseconds)
        {
            Calls.Add($"wait:{milliseconds}");
            if (WaitException is not null && _killed == ThrowDuringKilledWait)
                throw WaitException;
            return _killed ? KilledExit : GracefulExit;
        }

        public void Kill(bool entireProcessTree)
        {
            Calls.Add(entireProcessTree ? "killTree" : "kill");
            if (KillException is not null)
                throw KillException;
            _killed = true;
        }
    }

    private sealed class FakeInspection(int id, string? path, string? title) : IProcessInspectionHandle
    {
        public int Id => id;
        public bool HasExited => Exited;
        public bool Exited { get; set; }
        public bool Disposed { get; private set; }
        public bool PathRead { get; private set; }
        public bool ExitOnPathRead { get; init; }
        public Exception? PathException { get; init; }
        public string? MainWindowTitle => title;

        public string? ExecutablePath
        {
            get
            {
                PathRead = true;
                Exited |= ExitOnPathRead;
                if (PathException is not null)
                    throw PathException;
                return path;
            }
        }

        public void Dispose() => Disposed = true;
    }
}
