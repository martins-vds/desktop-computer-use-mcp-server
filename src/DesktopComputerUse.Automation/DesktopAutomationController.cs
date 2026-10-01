using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.Discovery;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Resolution;
using DesktopComputerUse.Contracts.Profiles;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;
using FlaApplication = FlaUI.Core.Application;

namespace DesktopComputerUse.Automation;

public sealed partial class DesktopAutomationController : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IApplicationProfileStore _profiles;
    private readonly IAutomationWorker _worker;
    private readonly FlaUiAutomationFactory _automationFactory;
    private readonly ControlSelectorResolver _selectorResolver;
    private readonly ControlObserver _observer;
    private readonly ApplicationSnapshotBuilder _snapshotBuilder;
    private readonly FuzzyControlResolver _fuzzyResolver;
    private readonly ILogger<DesktopAutomationController> _logger;

    private AutomationSession? _session;
    private Process? _ownedProcess;
    private readonly Dictionary<string, ProfileUpdateProposal> _profileUpdateProposals =
        new(StringComparer.Ordinal);

    public DesktopAutomationController(
        IApplicationProfileStore profiles,
        IAutomationWorker worker,
        FlaUiAutomationFactory automationFactory,
        ControlSelectorResolver selectorResolver,
        ControlObserver observer,
        ApplicationSnapshotBuilder snapshotBuilder,
        FuzzyControlResolver fuzzyResolver,
        ILogger<DesktopAutomationController> logger)
    {
        _profiles = profiles;
        _worker = worker;
        _automationFactory = automationFactory;
        _selectorResolver = selectorResolver;
        _observer = observer;
        _snapshotBuilder = snapshotBuilder;
        _fuzzyResolver = fuzzyResolver;
        _logger = logger;
    }

    public IReadOnlyList<ApplicationProfileSummary> ListProfiles() => _profiles.List();

    public Task<AutomationResult<ApplicationState>> LaunchAsync(
        string profileId,
        CancellationToken cancellationToken)
        => LaunchAsync(profileId, LaunchPolicy.Fail, cancellationToken);

    public Task<AutomationResult<ApplicationState>> LaunchAsync(
        string profileId,
        LaunchPolicy launchPolicy,
        CancellationToken cancellationToken)
    {
        if (!_profiles.TryGet(profileId, out var profile))
        {
            return Task.FromResult(AutomationResult<ApplicationState>.Failure(
                AutomationErrorCode.ProfileNotFound,
                $"Application profile '{profileId}' was not found."));
        }

        return ExecuteAsync(
            profile,
            cancellationToken,
            token => LaunchOrAttachSession(profile, launchPolicy, token, cancellationToken));
    }

    private ApplicationState LaunchOrAttachSession(
        ApplicationProfile profile,
        LaunchPolicy launchPolicy,
        CancellationToken token,
        CancellationToken callerToken)
    {
        EnsureWindows();
        EnsureNoLaunchSession();
        EnsureExecutableExists(profile);
        token.ThrowIfCancellationRequested();
        var matches = ProcessLifecycle.FindMatchingProcesses(profile.ExecutablePath);
        var existingProcessId = ProcessLifecycle.SelectExistingProcess(
            launchPolicy, profile.AllowMultipleInstances, matches);
        if (existingProcessId is int existingId)
        {
            VerifyProcess(profile, existingId);
            return StartSession(profile, existingId, null, token, callerToken);
        }
        token.ThrowIfCancellationRequested();
        var process = SpawnOwnedProcess(profile, callerToken);
        return StartSession(profile, process.Id, process, token, callerToken);
    }

    private Process SpawnOwnedProcess(ApplicationProfile profile, CancellationToken callerToken)
    {
        try
        {
            return Process.Start(CreateProcessStartInfo(profile))
                ?? throw new AutomationOperationException(
                    AutomationErrorCode.AutomationFailure,
                    "The application process could not be started.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to spawn application profile {ProfileId}.", profile.Id);
            var original = AutomationExceptionResultMapper.Map<ApplicationState>(
                exception, callerToken.IsCancellationRequested).Error!;
            throw new AutomationOperationException(
                original.Code,
                original.Message,
                original.Candidates,
                original.Diagnostic! with
                {
                    Operation = "launch",
                    Phase = "spawn",
                    ProfileId = profile.Id,
                    CleanupOutcome = "notSpawned"
                },
                original.Failures);
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(ApplicationProfile profile)
        => new()
        {
            FileName = profile.ExecutablePath,
            Arguments = profile.Arguments ?? string.Empty,
            WorkingDirectory = GetWorkingDirectory(profile),
            UseShellExecute = false
        };

    private static string GetWorkingDirectory(ApplicationProfile profile)
        => profile.WorkingDirectory
            ?? Path.GetDirectoryName(profile.ExecutablePath)
            ?? Environment.CurrentDirectory;

    public Task<AutomationResult<ApplicationState>> AttachAsync(
        string profileId,
        int processId,
        CancellationToken cancellationToken)
    {
        if (!_profiles.TryGet(profileId, out var profile))
        {
            return Task.FromResult(AutomationResult<ApplicationState>.Failure(
                AutomationErrorCode.ProfileNotFound,
                $"Application profile '{profileId}' was not found."));
        }

        return ExecuteAsync(
            profile,
            cancellationToken,
            token =>
            {
                EnsureWindows();
                EnsureNoSession();
                VerifyProcess(profile, processId);

                return StartSession(profile, processId, null, token, cancellationToken);
            });
    }

    public Task<AutomationResult> DetachAsync(
        bool terminateOwnedProcess,
        CancellationToken cancellationToken)
    {
        var timeout = GetSessionTimeout();
        return ExecuteAsync(
            timeout,
            cancellationToken,
            token =>
            {
                token.ThrowIfCancellationRequested();
                var session = GetSession();
                var profileId = session.Profile.Id;
                var processId = session.Application.ProcessId;

                InvalidateCaptures(session);
                var cleanup = CloseOwnedProcess(session, terminateOwnedProcess);
                var failures = new List<AutomationDiagnostic>(cleanup.Failures);
                DisposeSessionResource(session.Automation, processId, "disposeAutomation", failures);
                DisposeSessionResource(session.Application, processId, "disposeApplication", failures);
                DisposeSessionResource(_ownedProcess, processId, "disposeProcessHandle", failures);
                _ownedProcess = null;
                _session = null;
                _profileUpdateProposals.Clear();
                if (ProcessLifecycle.HasCleanupFailures(cleanup with { Failures = failures }))
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.AutomationFailure,
                        "The session was detached, but process cleanup or resource disposal failed.",
                        diagnostic: new AutomationDiagnostic
                        {
                            Code = AutomationErrorCode.AutomationFailure,
                            Operation = "detach",
                            ProcessId = processId,
                            CleanupOutcome = cleanup.Outcome
                        },
                        failures: failures);
                }
                LogAudit("detach", profileId, processId, succeeded: true);
                return AutomationResult.Success();
            });
    }

    private ApplicationState StartSession(
        ApplicationProfile profile,
        int processId,
        Process? ownedProcess,
        CancellationToken cancellationToken,
        CancellationToken callerCancellationToken)
    {
        FlaApplication? application = null;
        FlaUI.Core.AutomationBase? automation = null;
        var ownsProcess = ownedProcess is not null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            application = FlaApplication.Attach(processId);
            automation = _automationFactory.Create(profile.Backend);
            var window = WaitForMainWindow(
                application,
                automation,
                profile,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var session = new AutomationSession(
                profile,
                application,
                automation,
                window,
                ownsProcess: ownsProcess);
            var state = BuildApplicationState(session);
            LogAudit(
                OwnershipAction(ownsProcess),
                profile.Id,
                application.ProcessId,
                succeeded: true);
            _session = session;
            _ownedProcess = ownedProcess;
            return state;
        }
        catch (Exception exception)
        {
            var cleanup = CleanupFailedSession(application, automation, ownedProcess, processId);
            _logger.LogError(
                exception,
                "Failed to start profile {ProfileId}; spawned/attached PID {ProcessId}; cleanup {CleanupOutcome}.",
                profile.Id,
                processId,
                cleanup.Outcome);
            throw ProcessLifecycle.StartupFailure(
                exception,
                callerCancellationToken.IsCancellationRequested,
                profile.Id,
                processId,
                ownsProcess,
                cleanup);
        }
    }

    private static string OwnershipAction(bool ownsProcess) => ownsProcess ? "launch" : "attach";

    private ProcessCleanupResult CleanupFailedSession(
        FlaApplication? application,
        FlaUI.Core.AutomationBase? automation,
        Process? ownedProcess,
        int processId)
    {
        var cleanup = ownedProcess is null
            ? new ProcessCleanupResult(processId, "notOwned", [])
            : ProcessLifecycle.CleanupOwnedProcess(
                ownedProcess,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));
        var failures = cleanup.Failures.ToList();
        ReportCleanup(cleanup);
        DisposeSessionResource(automation, processId, "disposeAutomation", failures);
        DisposeSessionResource(application, processId, "disposeApplication", failures);
        DisposeSessionResource(ownedProcess, processId, "disposeProcessHandle", failures);
        return cleanup with { Failures = failures };
    }

    private void DisposeSessionResource(
        IDisposable? resource,
        int processId,
        string phase,
        List<AutomationDiagnostic> failures)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed session resource cleanup {Phase} for PID {ProcessId}.", phase, processId);
            failures.Add(AutomationExceptionResultMapper.CreateDiagnostic(exception) with
            {
                Operation = "cleanup",
                Phase = phase,
                ProcessId = processId
            });
        }
    }

    private void ReportCleanup(ProcessCleanupResult cleanup)
    {
        _logger.LogInformation(
            "Process cleanup for PID {ProcessId}: {CleanupOutcome}.",
            cleanup.ProcessId,
            cleanup.Outcome);
        foreach (var failure in cleanup.Failures)
            _logger.LogWarning(
                "Process cleanup failure for PID {ProcessId}: phase {Phase}, exception {ExceptionType}, HRESULT {HResult}.",
                cleanup.ProcessId,
                failure.Phase,
                failure.ExceptionType,
                failure.HResult);
    }

    private ProcessCleanupResult CloseOwnedProcess(
        AutomationSession session,
        bool terminateOwnedProcess)
    {
        if (!ProcessLifecycle.ShouldTerminateProcess(
                terminateOwnedProcess, session.OwnsProcess, _ownedProcess is not null))
            return new(session.Application.ProcessId, "notRequested", []);
        var cleanup = ProcessLifecycle.CleanupOwnedProcess(
            _ownedProcess!,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
        ReportCleanup(cleanup);
        return cleanup;
    }

    private void EnsureNoLaunchSession()
    {
        if (_session is null)
            return;
        throw new AutomationOperationException(
            AutomationErrorCode.ApplicationAlreadyAttached,
            "An application session is already active. Detach it before launching or attaching another application.",
            diagnostic: new AutomationDiagnostic
            {
                Code = AutomationErrorCode.ApplicationAlreadyAttached,
                Operation = "launch",
                Phase = "checkActiveSession",
                ProcessId = _session.Application.ProcessId,
                MatchingProcesses =
                [
                    new(_session.Application.ProcessId, _session.Profile.ExecutablePath, null)
                ]
            });
    }

    public Task<AutomationResult<ApplicationState>> GetApplicationStateAsync(
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, _) => BuildApplicationState(session));

    public Task<AutomationResult<ControlSummary>> FindControlAsync(
        ControlSelector selector,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                var matches = FindMatches(session, selector, token);
                return ObserveResolvedControl(session, matches, UniqueElement(session, matches));
            });

    public Task<AutomationResult<ControlSummary>> GetControlPropertiesAsync(
        ControlSelector selector,
        CancellationToken cancellationToken)
        => FindControlAsync(selector, cancellationToken);

    public Task<AutomationResult<ControlTreeNode>> InspectControlsAsync(
        ControlSelector? rootSelector,
        int? maxDepth,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                var root = ResolveInspectionRoot(session, rootSelector, token);
                var depth = Math.Clamp(
                    maxDepth ?? session.Profile.MaxTreeDepth,
                    1,
                    session.Profile.MaxTreeDepth);
                var tree = _observer.Inspect(root, session.Profile, depth - 1, session.Profile.MaxResults, token);
                return ApplyNativeInspectionRoot(tree, root, session);
            });

    private AutomationElement ResolveInspectionRoot(
        AutomationSession session, ControlSelector? selector, CancellationToken token)
        => selector is null ? session.MainWindow : ResolveSingle(session, selector, token);

    private ControlTreeNode ApplyNativeInspectionRoot(
        ControlTreeNode tree, AutomationElement root, AutomationSession session)
        => ReferenceEquals(root, session.MainWindow)
            ? tree with { Control = tree.Control with { Bounds = NativeRootBounds(session) } }
            : tree;

    public Task<AutomationResult<ApplicationSnapshot>> SnapshotApplicationAsync(
        int? maxDepth,
        int? maxResults,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                var snapshot = _snapshotBuilder.Build(
                    session,
                    Math.Clamp(maxDepth ?? session.Profile.MaxTreeDepth, 1, session.Profile.MaxTreeDepth),
                    Math.Clamp(maxResults ?? session.Profile.MaxResults, 1, session.Profile.MaxResults),
                    token);
                var bounds = NativeRootBounds(session);
                return snapshot with
                {
                    Window = snapshot.Window with
                    {
                        Bounds = bounds,
                        Controls = snapshot.Window.Controls.Select(control =>
                            control.CandidateId == snapshot.Window.CandidateId
                                ? control with { Bounds = bounds }
                                : control).ToArray()
                    }
                };
            });

    public Task<AutomationResult<ControlResolutionResult>> ResolveControlIntentAsync(
        string semanticKey,
        int maximumCandidates,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                if (!session.Profile.EffectiveSemanticTargets.TryGetValue(
                        semanticKey,
                        out var target))
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.ControlNotFound,
                        $"Semantic target '{semanticKey}' is not defined by profile '{session.Profile.Id}'.");
                }

                var snapshot = _snapshotBuilder.Build(
                    session,
                    session.Profile.MaxTreeDepth,
                    session.Profile.MaxResults,
                    token);
                var result = _fuzzyResolver.Resolve(
                    snapshot,
                    semanticKey,
                    target,
                    Math.Clamp(maximumCandidates, 1, 50));
                RecordShadowProposal(session, target, result);
                return result;
            });

    public Task<AutomationResult<IReadOnlyList<ProfileUpdateProposal>>> GetProfileUpdateProposalsAsync(
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                token.ThrowIfCancellationRequested();
                return (IReadOnlyList<ProfileUpdateProposal>)_profileUpdateProposals.Values
                    .Where(proposal => proposal.ProfileId == session.Profile.Id)
                    .OrderByDescending(proposal => proposal.CreatedAt)
                    .ToArray();
            });

    public Task<AutomationResult<ProfileUpdatePatch>> ExportProfileUpdatePatchAsync(
        string proposalId,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (!_profileUpdateProposals.TryGetValue(proposalId, out var proposal) ||
                    proposal.ProfileId != session.Profile.Id)
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.ControlNotFound,
                        $"Profile update proposal '{proposalId}' was not found.");
                }

                return new ProfileUpdatePatch(
                    proposal.ProfileId,
                    proposal.ProposalId,
                    proposal.SemanticKey,
                    proposal.ProposedTarget);
            });

    public Task<AutomationResult<ActionResult>> InvokeAsync(
        ControlSelector selector,
        CancellationToken cancellationToken)
        => ExecuteActionAsync(
            "invoke",
            selector,
            cancellationToken,
            element =>
            {
                if (!element.Patterns.Invoke.TryGetPattern(out var pattern))
                {
                    throw Unsupported("Invoke", element);
                }

                pattern.Invoke();
            });

    public Task<AutomationResult<ActionResult>> SetValueAsync(
        ControlSelector selector,
        string value,
        CancellationToken cancellationToken)
        => ExecuteActionAsync(
            "set_value",
            selector,
            cancellationToken,
            element =>
            {
                if (!element.Patterns.Value.TryGetPattern(out var pattern))
                {
                    throw Unsupported("Value", element);
                }

                if (pattern.IsReadOnly.ValueOrDefault)
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.ReadOnlyControl,
                        "The selected control exposes a read-only Value pattern.");
                }

                pattern.SetValue(value);
            });

    public Task<AutomationResult<ActionResult>> SelectAsync(
        ControlSelector selector,
        CancellationToken cancellationToken)
        => ExecuteActionAsync(
            "select",
            selector,
            cancellationToken,
            element =>
            {
                if (!element.Patterns.SelectionItem.TryGetPattern(out var pattern))
                {
                    throw Unsupported("SelectionItem", element);
                }

                pattern.Select();
            });

    public Task<AutomationResult<ActionResult>> SetExpandedStateAsync(
        ControlSelector selector,
        bool expanded,
        CancellationToken cancellationToken)
        => ExecuteActionAsync(
            ExpandedActionName(expanded),
            selector,
            cancellationToken,
            element => SetExpandedState(element, expanded));

    public Task<AutomationResult<ActionResult>> ScrollAsync(
        ControlSelector selector,
        int horizontalSteps,
        int verticalSteps,
        CancellationToken cancellationToken)
        => ExecuteActionAsync(
            "scroll",
            selector,
            cancellationToken,
            element => ScrollElement(element, horizontalSteps, verticalSteps));

    private static string ExpandedActionName(bool expanded)
        => expanded ? "expand" : "collapse";

    private static void SetExpandedState(
        AutomationElement element,
        bool expanded)
    {
        if (!element.Patterns.ExpandCollapse.TryGetPattern(out var pattern))
        {
            throw Unsupported("ExpandCollapse", element);
        }

        if (expanded)
        {
            pattern.Expand();
            return;
        }

        pattern.Collapse();
    }

    private static void ScrollElement(
        AutomationElement element,
        int horizontalSteps,
        int verticalSteps)
    {
        ValidateScrollSteps(horizontalSteps, verticalSteps);
        if (!element.Patterns.Scroll.TryGetPattern(out var pattern))
        {
            throw Unsupported("Scroll", element);
        }

        var iterations = Math.Max(
            Math.Abs(horizontalSteps),
            Math.Abs(verticalSteps));
        for (var index = 0; index < iterations; index++)
        {
            pattern.Scroll(
                ScrollAmountResolver.Get(horizontalSteps, index),
                ScrollAmountResolver.Get(verticalSteps, index));
        }
    }

    private static void ValidateScrollSteps(int horizontalSteps, int verticalSteps)
    {
        if (Math.Abs(horizontalSteps) > 20 || Math.Abs(verticalSteps) > 20)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.AutomationFailure,
                "Scroll steps must be between -20 and 20.");
        }
    }

    public Task<AutomationResult<ControlSummary>> WaitForStateAsync(
        ControlSelector selector,
        WaitCondition condition,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        var sessionTimeout = GetSessionTimeout();
        var requestedTimeout = timeoutMs is null
            ? sessionTimeout
            : TimeSpan.FromMilliseconds(
                Math.Clamp(timeoutMs.Value, 100, (int)sessionTimeout.TotalMilliseconds));

        return ExecuteAsync(
            requestedTimeout + TimeSpan.FromSeconds(1),
            cancellationToken,
            token => WaitForState(
                GetSession(),
                selector,
                condition,
                requestedTimeout,
                token));
    }

    private ControlSummary WaitForState(
        AutomationSession session,
        ControlSelector selector,
        WaitCondition condition,
        TimeSpan requestedTimeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + requestedTimeout;
        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = GetSingleMatchOrThrow(
                session,
                FindMatches(session, selector, cancellationToken));
            var summary = ObserveControl(element, session.Profile);
            if (WaitConditionEvaluator.Matches(summary, condition))
            {
                return WaitResult(summary);
            }

            cancellationToken.WaitHandle.WaitOne(session.Profile.PollIntervalMs);
        }

        throw new AutomationOperationException(
            AutomationErrorCode.Timeout,
            $"The requested control state was not observed within {requestedTimeout.TotalMilliseconds:0} milliseconds.");
    }

    private static ControlSummary WaitResult(ControlSummary? summary) => summary ?? MissingControlSummary();

    private ControlSummary? ObserveControl(
        AutomationElement? element,
        ApplicationProfile profile)
        => element is null ? null : _observer.Observe(element, profile);

    public Task<AutomationResult<WindowCapture>> CaptureApplicationWindowAsync(
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) => CaptureWindowResult(session, token));

    public Task<AutomationResult<WindowCapture>> CaptureControlImageAsync(
        ControlSelector selector,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) => CaptureControlResult(session, selector, token));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _worker.RunAsync(
                _ =>
                {
                    if (_session is not null)
                    {
                        InvalidateCaptures(_session);
                    }
                    _session?.Dispose();
                    _session = null;
                    _ownedProcess?.Dispose();
                    _ownedProcess = null;
                    return true;
                },
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose the active automation session cleanly.");
        }

        await _worker.DisposeAsync();
    }

    private Task<AutomationResult<ActionResult>> ExecuteActionAsync(
        string actionName,
        ControlSelector selector,
        CancellationToken cancellationToken,
        Action<AutomationElement> action)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) =>
            {
                token.ThrowIfCancellationRequested();
                var element = ResolveSingle(session, selector, token);
                action(element);
                token.ThrowIfCancellationRequested();

                var observed = _observer.Observe(
                    ResolveSingle(session, selector, token),
                    session.Profile);

                LogAudit(
                    actionName,
                    session.Profile.Id,
                    session.Application.ProcessId,
                    succeeded: true,
                    DescribeSelector(selector));
                return new ActionResult(
                    actionName,
                    observed,
                    observed.Value,
                    DateTimeOffset.UtcNow);
            });

    private Task<AutomationResult<T>> ExecuteWithSessionAsync<T>(
        CancellationToken cancellationToken,
        Func<AutomationSession, CancellationToken, T> action,
        [CallerMemberName] string operation = "")
        => ExecuteAsync(
            GetSessionTimeout(),
            cancellationToken,
            token =>
            {
                var session = GetSession();
                EnsureProcessAlive(session);
                return action(session, token);
            }, operation);

    private Task<AutomationResult<T>> ExecuteAsync<T>(
        ApplicationProfile profile,
        CancellationToken cancellationToken,
        Func<CancellationToken, T> action,
        [CallerMemberName] string operation = "")
        => ExecuteAsync(
            TimeSpan.FromMilliseconds(profile.OperationTimeoutMs),
            cancellationToken,
            action, operation);

    private async Task<AutomationResult<T>> ExecuteAsync<T>(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, T> action,
        [CallerMemberName] string operation = "")
    {
        try
        {
            var value = await _worker.RunAsync(action, timeout, cancellationToken);
            return AutomationResult<T>.Success(value);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected automation failure.");
            var result = AutomationExceptionResultMapper.Map<T>(
                exception,
                cancellationToken.IsCancellationRequested);
            return AddFailureContext(result, operation);
        }
    }

    private async Task<AutomationResult> ExecuteAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, AutomationResult> action,
        [CallerMemberName] string operation = "")
    {
        var result = await ExecuteAsync<AutomationResult>(
            timeout,
            cancellationToken,
            action, operation);
        return result.Succeeded
            ? result.Value!
            : new AutomationResult(false, result.Error);
    }

    private AutomationResult<T> AddFailureContext<T>(AutomationResult<T> result, string operation)
    {
        var error = result.Error!;
        var diagnostic = error.Diagnostic ?? new AutomationDiagnostic { Code = error.Code };
        return result with
        {
            Error = error with
            {
                Diagnostic = diagnostic with
                {
                    Operation = diagnostic.Operation ?? operation,
                    Phase = diagnostic.Phase ?? "execute",
                    ProfileId = diagnostic.ProfileId ?? _session?.Profile.Id,
                    ProfileRevision = diagnostic.ProfileRevision ?? _session?.Profile.Metadata?.Revision,
                    ProcessId = diagnostic.ProcessId ?? _session?.Application.ProcessId,
                    Hwnd = diagnostic.Hwnd ?? _session?.NativeWindowHandle
                }
            }
        };
    }

    private AutomationElement ResolveSingle(
        AutomationSession session,
        ControlSelector selector,
        CancellationToken cancellationToken = default)
        => UniqueElement(session, FindMatches(session, selector, cancellationToken));

    private AutomationElement UniqueElement(AutomationSession session, IReadOnlyList<AutomationElement> matches)
    {
        if (matches.Count == 0)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                "No control matched the selector within the attached application window.");
        }

        if (matches.Count > 1)
        {
            throw Ambiguous(session, matches);
        }

        return matches[0];
    }

    private ControlSummary ObserveResolvedControl(
        AutomationSession session, IReadOnlyList<AutomationElement> matches, AutomationElement element)
    {
        var summary = _observer.Observe(element, session.Profile);
        var searchFailures = (matches as ControlSelectorMatchResult)?.Failures ?? [];
        summary = summary with { Failures = summary.Failures.Concat(searchFailures).Take(100).ToArray() };
        return ReferenceEquals(element, session.MainWindow)
            ? summary with { Bounds = NativeRootBounds(session) }
            : summary;
    }

    private IReadOnlyList<AutomationElement> FindMatches(
        AutomationSession session,
        ControlSelector selector,
        CancellationToken cancellationToken = default)
    {
        var matchSets = _selectorResolver
            .ExpandSemanticSelectors(session.Profile, selector)
            .Select(expanded => _selectorResolver.FindMatchesWithDiagnostics(
                session.MainWindow,
                expanded,
                session.Profile.MaxResults,
                session.Profile.MaxTreeDepth,
                cancellationToken: cancellationToken))
            .ToArray();
        var selected = SelectMatchSet(matchSets);
        if (selected is not null)
        {
            selected.EnsureCompleteForAction();
            return selected;
        }

        ThrowIfSearchIncomplete(matchSets);
        return [];
    }

    private static ControlSelectorMatchResult? SelectMatchSet(IReadOnlyList<ControlSelectorMatchResult> matchSets)
        => matchSets.FirstOrDefault(matches => CompleteUniqueMatch(matches.Count, matches.Truncated))
            ?? matchSets.FirstOrDefault(matches => matches.Count > 1);

    internal static bool CompleteUniqueMatch(int count, bool truncated) => count == 1 && !truncated;

    private static void ThrowIfSearchIncomplete(IReadOnlyList<ControlSelectorMatchResult> matchSets)
    {
        var failures = matchSets.SelectMany(matches => matches.Failures).Take(100).ToArray();
        if (SearchIncomplete(failures.Length, matchSets.Any(matches => matches.Truncated)))
        {
            throw new AutomationOperationException(AutomationErrorCode.ProviderFailure,
                "Selector resolution was incomplete. Inspect the partial diagnostics or use qualified native fallback.",
                failures: failures);
        }
    }

    internal static bool SearchIncomplete(int failedCount, bool truncated) => failedCount != 0 || truncated;

    private AutomationOperationException Ambiguous(
        AutomationSession session,
        IReadOnlyList<AutomationElement> matches)
        => new(
            AutomationErrorCode.AmbiguousControl,
            $"The selector matched {matches.Count} controls. Add a stable ancestor or index.",
            matches
                .Take(10)
                .Select(element => _observer.Observe(element, session.Profile))
                .ToArray());

    private AutomationElement? GetSingleMatchOrThrow(
        AutomationSession session,
        IReadOnlyList<AutomationElement> matches)
    {
        if (matches.Count > 1)
        {
            throw Ambiguous(session, matches);
        }

        return matches.SingleOrDefault();
    }

    private static ControlSummary MissingControlSummary()
        => new(
            null,
            null,
            "Missing",
            null,
            false,
            true,
            new RectangleInfo(0, 0, 0, 0),
            null,
            false,
            []);

    private static AutomationOperationException Unsupported(
        string pattern,
        AutomationElement element)
        => new(
            AutomationErrorCode.UnsupportedPattern,
            $"Control '{element.AutomationId ?? element.Name}' does not support the {pattern} pattern.");

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new AutomationOperationException(
                AutomationErrorCode.PlatformNotSupported,
                "Windows UI Automation requires the MCP server to run on Windows.");
        }
    }

    private static void EnsureExecutableExists(ApplicationProfile profile)
    {
        if (!File.Exists(profile.ExecutablePath))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ExecutableNotFound,
                $"The executable configured by profile '{profile.Id}' does not exist.");
        }
    }

    private void EnsureNoSession()
    {
        if (_session is not null)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationAlreadyAttached,
                "Detach the current application before launching or attaching another one.");
        }
    }

    private AutomationSession GetSession()
        => _session
            ?? throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAttached,
                "No application is currently attached.");

    private TimeSpan GetSessionTimeout()
        => _session is null
            ? DefaultTimeout
            : TimeSpan.FromMilliseconds(_session.Profile.OperationTimeoutMs);

    private static void EnsureProcessAlive(AutomationSession session)
    {
        if (session.Application.HasExited)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ProcessExited,
                "The attached application process has exited.");
        }
    }

    private ApplicationState BuildApplicationState(AutomationSession session)
        => new(
            session.Profile.Id,
            session.Application.ProcessId,
            session.Profile.ExecutablePath,
            session.OwnsProcess,
            session.Application.HasExited,
            session.Application.HasExited ? null : _observer.Observe(session.MainWindow, session.Profile).Name,
            session.Profile.Backend)
        {
            ProfileRevision = session.Profile.Metadata?.Revision,
            ProfileGeneration = LoadedGeneration(session.Profile),
            ProfileIsStale = SessionProfileIsStale(session.Profile),
            SessionId = session.SessionId
        };

    private static long LoadedGeneration(ApplicationProfile profile) => profile.Metadata?.Generation ?? 0;

    private bool SessionProfileIsStale(ApplicationProfile profile)
        => (_profiles as IReloadableApplicationProfileStore)?.IsStale(profile) ?? false;

    private static void VerifyProcess(ApplicationProfile profile, int processId)
    {
        using var process = GetProcess(processId);
        ValidateProcess(profile, processId, process);
    }

    private static Process GetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ProcessNotFound,
                $"Process {processId} was not found.",
                null);
        }
    }

    private static void ValidateProcess(
        ApplicationProfile profile,
        int processId,
        Process process)
    {
        if (process.HasExited)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ProcessExited,
                $"Process {processId} has exited.");
        }

        var actualPath = process.MainModule?.FileName;
        if (!PathsMatch(actualPath, profile.ExecutablePath))
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                $"Process {processId} does not match application profile '{profile.Id}'.");
        }
    }

    private static bool PathsMatch(string? actualPath, string expectedPath)
        => !string.IsNullOrWhiteSpace(actualPath) &&
            string.Equals(
                Path.GetFullPath(actualPath),
                expectedPath,
                StringComparison.OrdinalIgnoreCase);

    private static Window WaitForMainWindow(
        FlaApplication application,
        FlaUI.Core.AutomationBase automation,
        ApplicationProfile profile,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(profile.OperationTimeoutMs);

        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfExitedBeforeWindow(application);
            var matchingWindow = FindMainWindow(application, automation, profile);
            if (WindowAvailable(matchingWindow))
            {
                return matchingWindow;
            }

            cancellationToken.WaitHandle.WaitOne(profile.PollIntervalMs);
        }

        throw new AutomationOperationException(
            AutomationErrorCode.WindowNotFound,
            $"No top-level window matched profile '{profile.Id}'.");
    }

    private static bool WindowAvailable(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Window? window) => window is not null;

    private static void ThrowIfExitedBeforeWindow(FlaApplication application)
    {
        if (application.HasExited)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ProcessExited,
                "The application exited before its main window was available.");
        }
    }

    private static Window? FindMainWindow(
        FlaApplication application,
        FlaUI.Core.AutomationBase automation,
        ApplicationProfile profile)
        => application
            .GetAllTopLevelWindows(automation)
            .FirstOrDefault(window => WindowSelectorMatcher.Matches(
                window.Title,
                window.ClassName,
                profile.MainWindow));

    private void LogAudit(
        string action,
        string profileId,
        int processId,
        bool succeeded,
        string? target = null)
        => _logger.LogInformation(
            "AUDIT action={Action} profile={ProfileId} processId={ProcessId} succeeded={Succeeded} target={Target}",
            action,
            profileId,
            processId,
            succeeded,
            target ?? "-");

    internal static string DescribeSelector(ControlSelector selector)
        => selector.SemanticKey
            ?? selector.AutomationId
            ?? selector.Name
            ?? selector.ControlType
            ?? "unspecified";

    private static bool IsSensitiveElement(
        AutomationElement element,
        ApplicationProfile profile)
        => SafeIsPassword(element) ||
            SafeHasSensitiveAutomationId(element, profile);

    private static bool SafeIsPassword(AutomationElement element)
    {
        try
        {
            return element.Properties.IsPassword.Value;
        }
        catch (Exception exception)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                $"Capture refused because password sensitivity could not be determined ({exception.GetType().Name}).");
        }
    }

    private static bool SafeHasSensitiveAutomationId(
        AutomationElement element,
        ApplicationProfile profile)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(element.AutomationId) &&
                profile.SensitiveAutomationIds.Contains(element.AutomationId);
        }
        catch (Exception exception)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                $"Capture refused because sensitive control identity could not be determined ({exception.GetType().Name}).");
        }
    }

    private void RecordShadowProposal(
        AutomationSession session,
        SemanticTargetDefinition target,
        ControlResolutionResult result)
    {
        var proposal = ProfileUpdateProposalFactory.Create(
            session.Profile.Id,
            target,
            result,
            DateTimeOffset.UtcNow);
        if (proposal is null)
        {
            return;
        }

        RemoveDuplicateProposal(session.Profile.Id, result, proposal.CandidateId);
        _profileUpdateProposals[proposal.ProposalId] = proposal;
        TrimProposals();
    }

    private void RemoveDuplicateProposal(
        string profileId,
        ControlResolutionResult result,
        string candidateId)
    {
        var identity = (
            ProfileId: profileId,
            result.SemanticKey,
            result.ViewKey,
            CandidateId: candidateId);
        var duplicate = _profileUpdateProposals.Values.FirstOrDefault(existing =>
            (
                existing.ProfileId,
                existing.SemanticKey,
                existing.ViewKey,
                existing.CandidateId
            ).Equals(identity));
        if (duplicate is not null)
        {
            _profileUpdateProposals.Remove(duplicate.ProposalId);
        }
    }

    private void TrimProposals()
    {
        var overflow = _profileUpdateProposals.Count - 100;
        foreach (var proposal in _profileUpdateProposals.Values
                     .OrderBy(item => item.CreatedAt)
                     .Take(Math.Max(0, overflow)))
        {
            _profileUpdateProposals.Remove(proposal.ProposalId);
        }
    }
}
