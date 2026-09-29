using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Automation.FlaUi;
using DesktopComputerUse.Automation.Selectors;
using DesktopComputerUse.Automation.Threading;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
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
    private readonly ILogger<DesktopAutomationController> _logger;

    private AutomationSession? _session;

    public DesktopAutomationController(
        IApplicationProfileStore profiles,
        IAutomationWorker worker,
        FlaUiAutomationFactory automationFactory,
        ControlSelectorResolver selectorResolver,
        ControlObserver observer,
        ILogger<DesktopAutomationController> logger)
    {
        _profiles = profiles;
        _worker = worker;
        _automationFactory = automationFactory;
        _selectorResolver = selectorResolver;
        _observer = observer;
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

                try
                {
                    var window = WaitForMainWindow(
                        application,
                        automation,
                        profile,
                        token);
                    _session = new AutomationSession(
                        profile,
                        application,
                        automation,
                        window,
                        ownsProcess: true);
                    LogAudit("launch", profile.Id, application.ProcessId, succeeded: true);
                    return BuildApplicationState(_session);
                }
                catch
                {
                    automation.Dispose();
                    if (!application.HasExited)
                    {
                        application.Close(killIfCloseFails: false);
                    }

                    application.Dispose();
                    throw;
                }
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

                try
                {
                    var window = WaitForMainWindow(
                        application,
                        automation,
                        profile,
                        token);
                    _session = new AutomationSession(
                        profile,
                        application,
                        automation,
                        window,
                        ownsProcess: false);
                    LogAudit("attach", profile.Id, processId, succeeded: true);
                    return BuildApplicationState(_session);
                }
                catch
                {
                    automation.Dispose();
                    application.Dispose();
                    throw;
                }
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

                if (terminateOwnedProcess &&
                    session.OwnsProcess &&
                    !session.Application.HasExited)
                {
                    session.Application.Close(killIfCloseFails: false);
                }

                session.Dispose();
                _session = null;
                LogAudit("detach", profileId, processId, succeeded: true);
                return AutomationResult.Success();
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
            expanded ? "expand" : "collapse",
            selector,
            cancellationToken,
            element =>
            {
                if (!element.Patterns.ExpandCollapse.TryGetPattern(out var pattern))
                {
                    throw Unsupported("ExpandCollapse", element);
                }

                if (expanded)
                {
                    pattern.Expand();
                }
                else
                {
                    pattern.Collapse();
                }
            });

    public Task<AutomationResult<ActionResult>> ScrollAsync(
        ControlSelector selector,
        int horizontalSteps,
        int verticalSteps,
        CancellationToken cancellationToken)
        => ExecuteActionAsync(
            "scroll",
            selector,
            cancellationToken,
            element =>
            {
                if (Math.Abs(horizontalSteps) > 20 || Math.Abs(verticalSteps) > 20)
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.AutomationFailure,
                        "Scroll steps must be between -20 and 20.");
                }

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
                        GetScrollAmount(horizontalSteps, index),
                        GetScrollAmount(verticalSteps, index));
                }
            });

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
            token =>
            {
                var session = GetSession();
                var expanded = _selectorResolver.ExpandSemanticSelector(
                    session.Profile,
                    selector);
                var deadline = DateTime.UtcNow + requestedTimeout;

                while (DateTime.UtcNow <= deadline)
                {
                    token.ThrowIfCancellationRequested();
                    var matches = _selectorResolver.FindMatches(
                        session.MainWindow,
                        expanded,
                        session.Profile.MaxResults,
                        session.Profile.MaxTreeDepth);

                    if (matches.Count > 1)
                    {
                        throw Ambiguous(session, matches);
                    }

                    var element = matches.SingleOrDefault();
                    if (MatchesCondition(element, session.Profile, condition))
                    {
                        return element is null
                            ? new ControlSummary(
                                null,
                                null,
                                "Missing",
                                null,
                                false,
                                true,
                                new RectangleInfo(0, 0, 0, 0),
                                null,
                                false,
                                [])
                            : _observer.Observe(element, session.Profile);
                    }

                    token.WaitHandle.WaitOne(session.Profile.PollIntervalMs);
                }

                throw new AutomationOperationException(
                    AutomationErrorCode.Timeout,
                    $"The requested control state was not observed within {requestedTimeout.TotalMilliseconds:0} milliseconds.");
            });
    }

    public Task<AutomationResult<WindowCapture>> CaptureApplicationWindowAsync(
        CancellationToken cancellationToken)
        => ExecuteWithSessionAsync(
            cancellationToken,
            (session, _) =>
            {
                if (!session.Profile.EnableScreenshots)
                {
                    throw new AutomationOperationException(
                        AutomationErrorCode.ApplicationNotAllowed,
                        "Window capture is disabled by the application profile.");
                }

                using var capture = FlaUI.Core.Capturing.Capture.Element(session.MainWindow);
                using var stream = new MemoryStream();
                capture.Bitmap.Save(stream, ImageFormat.Png);
                return new WindowCapture(
                    "image/png",
                    Convert.ToBase64String(stream.ToArray()),
                    capture.Bitmap.Width,
                    capture.Bitmap.Height);
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
        catch (AutomationOperationException exception)
        {
            _logger.LogInformation(
                "Automation operation failed with {Code}: {Message}",
                exception.Code,
                exception.Message);
            return AutomationResult<T>.Failure(
                exception.Code,
                exception.Message,
                exception.Candidates);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Automation operation timed out.");
            return AutomationResult<T>.Failure(
                AutomationErrorCode.Timeout,
                exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AutomationResult<T>.Failure(
                AutomationErrorCode.OperationCancelled,
                "The automation operation was cancelled.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Automation access was denied.");
            return AutomationResult<T>.Failure(
                AutomationErrorCode.AccessDenied,
                "Windows denied access to the target process or control.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected automation failure.");
            return AutomationResult<T>.Failure(
                AutomationErrorCode.AutomationFailure,
                "The automation operation failed unexpectedly. See the server log for details.");
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
        var expanded = _selectorResolver.ExpandSemanticSelector(
            session.Profile,
            selector);
        var matches = _selectorResolver.FindMatches(
            session.MainWindow,
            expanded,
            session.Profile.MaxResults,
            session.Profile.MaxTreeDepth);

        return matches.Count switch
        {
            0 => throw new AutomationOperationException(
                AutomationErrorCode.ControlNotFound,
                "No control matched the selector within the attached application window."),
            1 => matches[0],
            _ => throw Ambiguous(session, matches)
        };
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

        var children = new List<ControlTreeNode>();
        foreach (var child in element.FindAllChildren())
        {
            if (remaining <= 0)
            {
                break;
            }

            children.Add(BuildTree(
                child,
                profile,
                depth - 1,
                ref remaining,
                cancellationToken));
        }

        return new ControlTreeNode(_observer.Observe(element, profile), children);
    }

    private static bool MatchesCondition(
        AutomationElement? element,
        ApplicationProfile profile,
        WaitCondition condition)
    {
        if (condition.Property == WaitProperty.Exists)
        {
            return condition.Comparison switch
            {
                WaitComparison.True => element is not null,
                WaitComparison.False => element is null,
                _ => throw new AutomationOperationException(
                    AutomationErrorCode.AutomationFailure,
                    "Exists waits require the True or False comparison.")
            };
        }

        if (element is null)
        {
            return false;
        }

        var observer = new ControlObserver();
        var summary = observer.Observe(element, profile);
        var actual = condition.Property switch
        {
            WaitProperty.Name => summary.Name,
            WaitProperty.Value => summary.Value,
            WaitProperty.IsEnabled => summary.IsEnabled.ToString(),
            WaitProperty.IsOffscreen => summary.IsOffscreen.ToString(),
            _ => null
        };

        return condition.Comparison switch
        {
            WaitComparison.Equals => string.Equals(
                actual,
                condition.ExpectedValue,
                StringComparison.Ordinal),
            WaitComparison.NotEquals => !string.Equals(
                actual,
                condition.ExpectedValue,
                StringComparison.Ordinal),
            WaitComparison.Contains => actual?.Contains(
                condition.ExpectedValue ?? string.Empty,
                StringComparison.Ordinal) is true,
            WaitComparison.True => bool.TryParse(actual, out var parsed) && parsed,
            WaitComparison.False => bool.TryParse(actual, out var parsed) && !parsed,
            _ => false
        };
    }

    private static ScrollAmount GetScrollAmount(int steps, int iteration)
    {
        if (iteration >= Math.Abs(steps) || steps == 0)
        {
            return ScrollAmount.NoAmount;
        }

        return steps > 0
            ? ScrollAmount.SmallIncrement
            : ScrollAmount.SmallDecrement;
    }

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
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            throw new AutomationOperationException(
                AutomationErrorCode.ProcessNotFound,
                $"Process {processId} was not found.",
                null);
        }

        using (process)
        {
            if (process.HasExited)
            {
                throw new AutomationOperationException(
                    AutomationErrorCode.ProcessExited,
                    $"Process {processId} has exited.");
            }

            var actualPath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(actualPath) ||
                !string.Equals(
                    Path.GetFullPath(actualPath),
                    profile.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AutomationOperationException(
                    AutomationErrorCode.ApplicationNotAllowed,
                    $"Process {processId} does not match application profile '{profile.Id}'.");
            }
        }
    }

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
            if (application.HasExited)
            {
                throw new AutomationOperationException(
                    AutomationErrorCode.ProcessExited,
                    "The application exited before its main window was available.");
            }

            var matchingWindow = application
                .GetAllTopLevelWindows(automation)
                .FirstOrDefault(window => MatchesWindow(window, profile.MainWindow));
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

    private static bool MatchesWindow(
        Window window,
        WindowSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Title) &&
            !string.Equals(window.Title, selector.Title, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ClassName) &&
            !string.Equals(
                window.ClassName,
                selector.ClassName,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.TitleRegex) &&
            !Regex.IsMatch(
                window.Title,
                selector.TitleRegex,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250)))
        {
            return false;
        }

        return true;
    }

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
}
