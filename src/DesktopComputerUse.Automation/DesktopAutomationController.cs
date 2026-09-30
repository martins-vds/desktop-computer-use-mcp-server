using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;
using FlaApplication = FlaUI.Core.Application;

namespace DesktopComputerUse.Automation;

public sealed class DesktopAutomationController : IAsyncDisposable
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
                EnsureExecutableExists(profile);

                var startInfo = new ProcessStartInfo
                {
                    FileName = profile.ExecutablePath,
                    Arguments = profile.Arguments ?? string.Empty,
                    WorkingDirectory = profile.WorkingDirectory
                        ?? Path.GetDirectoryName(profile.ExecutablePath)
                        ?? Environment.CurrentDirectory,
                    UseShellExecute = false
                };

                var application = FlaApplication.Launch(startInfo);
                var automation = _automationFactory.Create(profile.Backend);
                return StartSession(
                    profile,
                    application,
                    automation,
                    ownsProcess: true,
                    token);
            });
    }

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

                var application = FlaApplication.Attach(processId);
                var automation = _automationFactory.Create(profile.Backend);
                return StartSession(
                    profile,
                    application,
                    automation,
                    ownsProcess: false,
                    token);
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

                CloseOwnedProcess(session, terminateOwnedProcess);

                session.Dispose();
                _session = null;
                _profileUpdateProposals.Clear();
                LogAudit("detach", profileId, processId, succeeded: true);
                return AutomationResult.Success();
            });
    }

    private ApplicationState StartSession(
        ApplicationProfile profile,
        FlaApplication application,
        FlaUI.Core.AutomationBase automation,
        bool ownsProcess,
        CancellationToken cancellationToken)
    {
        try
        {
            var window = WaitForMainWindow(
                application,
                automation,
                profile,
                cancellationToken);
            _session = new AutomationSession(
                profile,
                application,
                automation,
                window,
                ownsProcess);
            LogAudit(
                ownsProcess ? "launch" : "attach",
                profile.Id,
                application.ProcessId,
                succeeded: true);
            return BuildApplicationState(_session);
        }
        catch
        {
            CleanupFailedSession(application, automation, ownsProcess);
            throw;
        }
    }

    private static void CleanupFailedSession(
        FlaApplication application,
        FlaUI.Core.AutomationBase automation,
        bool ownsProcess)
    {
        automation.Dispose();
        if (ownsProcess && !application.HasExited)
        {
            application.Close(killIfCloseFails: false);
        }

        application.Dispose();
    }

    private static void CloseOwnedProcess(
        AutomationSession session,
        bool terminateOwnedProcess)
    {
        if (new[]
            {
                terminateOwnedProcess,
                session.OwnsProcess,
                !session.Application.HasExited
            }.All(value => value))
        {
            session.Application.Close(killIfCloseFails: false);
        }
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
            (session, _) =>
            {
                var element = ResolveSingle(session, selector);
                return _observer.Observe(element, session.Profile);
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
                var root = rootSelector is null
                    ? session.MainWindow
                    : ResolveSingle(session, rootSelector);
                var depth = Math.Clamp(
                    maxDepth ?? session.Profile.MaxTreeDepth,
                    1,
                    session.Profile.MaxTreeDepth);
                var remaining = session.Profile.MaxResults;
                return BuildTree(root, session.Profile, depth, ref remaining, token);
            });

    public Task<AutomationResult<ApplicationSnapshot>> SnapshotApplicationAsync(
        int? maxDepth,
        int? maxResults,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, token) => _snapshotBuilder.Build(
                session,
                Math.Clamp(
                    maxDepth ?? session.Profile.MaxTreeDepth,
                    1,
                    session.Profile.MaxTreeDepth),
                Math.Clamp(
                    maxResults ?? session.Profile.MaxResults,
                    1,
                    session.Profile.MaxResults),
                token));

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
                FindMatches(session, selector));
            var summary = ObserveControl(element, session.Profile);
            if (WaitConditionEvaluator.Matches(summary, condition))
            {
                return summary ?? MissingControlSummary();
            }

            cancellationToken.WaitHandle.WaitOne(session.Profile.PollIntervalMs);
        }

        throw new AutomationOperationException(
            AutomationErrorCode.Timeout,
            $"The requested control state was not observed within {requestedTimeout.TotalMilliseconds:0} milliseconds.");
    }

    private ControlSummary? ObserveControl(
        AutomationElement? element,
        ApplicationProfile profile)
        => element is null ? null : _observer.Observe(element, profile);

    public Task<AutomationResult<WindowCapture>> CaptureApplicationWindowAsync(
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, _) =>
            {
                return CaptureRedactedElement(
                    session.MainWindow,
                    session.Profile);
            });

    public Task<AutomationResult<WindowCapture>> CaptureControlImageAsync(
        ControlSelector selector,
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, _) =>
            {
                var element = ResolveSingle(session, selector);
                if (IsSensitiveElement(element, session.Profile))
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.ApplicationNotAllowed,
                        "Capturing a password or sensitive control is not permitted.");
                }

                return CaptureRedactedElement(element, session.Profile);
            });

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _worker.RunAsync(
                _ =>
                {
                    _session?.Dispose();
                    _session = null;
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
                var element = ResolveSingle(session, selector);
                var before = _observer.Observe(element, session.Profile);
                action(element);
                token.ThrowIfCancellationRequested();

                ControlSummary observed;
                try
                {
                    observed = _observer.Observe(
                        ResolveSingle(session, selector),
                        session.Profile);
                }
                catch (AutomationOperationException)
                {
                    observed = before;
                }

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
        Func<AutomationSession, CancellationToken, T> action)
        => ExecuteAsync(
            GetSessionTimeout(),
            cancellationToken,
            token =>
            {
                var session = GetSession();
                EnsureProcessAlive(session);
                return action(session, token);
            });

    private Task<AutomationResult<T>> ExecuteAsync<T>(
        ApplicationProfile profile,
        CancellationToken cancellationToken,
        Func<CancellationToken, T> action)
        => ExecuteAsync(
            TimeSpan.FromMilliseconds(profile.OperationTimeoutMs),
            cancellationToken,
            action);

    private async Task<AutomationResult<T>> ExecuteAsync<T>(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, T> action)
    {
        try
        {
            var value = await _worker.RunAsync(action, timeout, cancellationToken);
            return AutomationResult<T>.Success(value);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected automation failure.");
            return AutomationExceptionResultMapper.Map<T>(
                exception,
                cancellationToken.IsCancellationRequested);
        }
    }

    private async Task<AutomationResult> ExecuteAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, AutomationResult> action)
    {
        var result = await ExecuteAsync<AutomationResult>(
            timeout,
            cancellationToken,
            action);
        return result.Succeeded
            ? result.Value!
            : AutomationResult.Failure(
                result.Error!.Code,
                result.Error.Message);
    }

    private AutomationElement ResolveSingle(
        AutomationSession session,
        ControlSelector selector)
    {
        var matches = FindMatches(session, selector);

        return matches.Count switch
        {
            0 => throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                "No control matched the selector within the attached application window."),
            1 => matches[0],
            _ => throw Ambiguous(session, matches)
        };
    }

    private IReadOnlyList<AutomationElement> FindMatches(
        AutomationSession session,
        ControlSelector selector)
    {
        var matchSets = _selectorResolver
            .ExpandSemanticSelectors(session.Profile, selector)
            .Select(expanded => _selectorResolver.FindMatches(
                session.MainWindow,
                expanded,
                session.Profile.MaxResults,
                session.Profile.MaxTreeDepth))
            .ToArray();
        return matchSets.FirstOrDefault(matches => matches.Count == 1)
            ?? matchSets.FirstOrDefault(matches => matches.Count > 1)
            ?? [];
    }

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

    private ControlTreeNode BuildTree(
        AutomationElement element,
        ApplicationProfile profile,
        int depth,
        ref int remaining,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (remaining <= 0)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.AutomationFailure,
                $"Control inspection exceeded the profile limit of {profile.MaxResults} elements.");
        }

        remaining--;
        if (depth <= 1)
        {
            return new ControlTreeNode(_observer.Observe(element, profile), []);
        }

        var children = BuildChildren(
            element,
            profile,
            depth,
            ref remaining,
            cancellationToken);

        return new ControlTreeNode(_observer.Observe(element, profile), children);
    }

    private IReadOnlyList<ControlTreeNode> BuildChildren(
        AutomationElement element,
        ApplicationProfile profile,
        int depth,
        ref int remaining,
        CancellationToken cancellationToken)
    {
        var children = new List<ControlTreeNode>();
        foreach (var child in element.FindAllChildren().Take(remaining))
        {
            children.Add(BuildTree(
                child,
                profile,
                depth - 1,
                ref remaining,
                cancellationToken));
        }

        return children;
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

    private static ApplicationState BuildApplicationState(AutomationSession session)
        => new(
            session.Profile.Id,
            session.Application.ProcessId,
            session.Profile.ExecutablePath,
            session.OwnsProcess,
            session.Application.HasExited,
            session.Application.HasExited ? null : session.MainWindow.Title,
            session.Profile.Backend);

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
            if (matchingWindow is not null)
            {
                return matchingWindow;
            }

            cancellationToken.WaitHandle.WaitOne(profile.PollIntervalMs);
        }

        throw new AutomationOperationException(
            AutomationErrorCode.WindowNotFound,
            $"No top-level window matched profile '{profile.Id}'.");
    }

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

    private static string DescribeSelector(ControlSelector selector)
        => selector.SemanticKey
            ?? selector.AutomationId
            ?? selector.Name
            ?? selector.ControlType
            ?? "unspecified";

    private static WindowCapture CaptureRedactedElement(
        AutomationElement element,
        ApplicationProfile profile)
    {
        if (!profile.EnableScreenshots)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ApplicationNotAllowed,
                "Image capture is disabled by the application profile.");
        }

        using var capture = FlaUI.Core.Capturing.Capture.Element(element);
        RedactSensitiveDescendants(
            capture.Bitmap,
            element,
            profile);
        using var stream = new MemoryStream();
        capture.Bitmap.Save(stream, ImageFormat.Png);
        return new WindowCapture(
            "image/png",
            Convert.ToBase64String(stream.ToArray()),
            capture.Bitmap.Width,
            capture.Bitmap.Height);
    }

    private static void RedactSensitiveDescendants(
        Bitmap bitmap,
        AutomationElement root,
        ApplicationProfile profile)
    {
        var rootBounds = root.BoundingRectangle;
        using var graphics = Graphics.FromImage(bitmap);
        foreach (var descendant in root.FindAllDescendants())
        {
            RedactSensitiveElement(
                graphics,
                bitmap.Size,
                rootBounds,
                descendant,
                profile);
        }
    }

    private static void RedactSensitiveElement(
        Graphics graphics,
        Size bitmapSize,
        Rectangle rootBounds,
        AutomationElement element,
        ApplicationProfile profile)
    {
        if (!IsSensitiveElement(element, profile))
        {
            return;
        }

        var bounds = element.BoundingRectangle;
        var rectangle = Rectangle.Intersect(
            new Rectangle(
                bounds.X - rootBounds.X,
                bounds.Y - rootBounds.Y,
                bounds.Width,
                bounds.Height),
            new Rectangle(Point.Empty, bitmapSize));
        if (!rectangle.IsEmpty)
        {
            graphics.FillRectangle(Brushes.Black, rectangle);
        }
    }

    private static bool IsSensitiveElement(
        AutomationElement element,
        ApplicationProfile profile)
        => SafeIsPassword(element) ||
            SafeHasSensitiveAutomationId(element, profile);

    private static bool SafeIsPassword(AutomationElement element)
    {
        try
        {
            return element.Properties.IsPassword.ValueOrDefault;
        }
        catch
        {
            return false;
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
        catch
        {
            return false;
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
