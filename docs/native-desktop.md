# Native desktop fallback and feedback remediation

Semantic UI Automation remains the preferred interaction method. Native tools are
a fallback for legacy controls whose providers cannot supply usable selectors.
The Windows server must run in the same interactive desktop and integrity level
as the target application. Linux advertises the same tool names but cannot
operate a Windows desktop.

## Coordinate migration

The Windows executable declares Per-Monitor V2 DPI awareness before automation
starts. Screen coordinates are **physical pixels in the Windows virtual
desktop**, not coordinates relative to the primary monitor. Negative X and Y
origins are valid. Existing `ControlSummary.bounds` values on scaled displays
have changed value semantics: callers must not apply a DPI multiplier to them.
UIA provider bounds may still be defective; use `get_window_geometry` for
authoritative HWND geometry and its discrepancy diagnostics.
Main-window roots returned by inspection and snapshots use native window bounds;
descendant control rectangles remain provider-reported physical coordinates.

`get_desktop_layout` returns monitor bounds, work areas, and the virtual desktop
rectangle. `get_window_geometry` distinguishes outer window, visible frame, and
client-area screen rectangles. Window-client input is converted using native
client geometry. Screenshot-relative input must use `click_capture_point` with
the capture ID; never treat screenshot pixels as screen pixels.

## Enabling native input

Native input is disabled unless the attached profile explicitly enables it:

```json
"nativeInput": {
  "enabled": true,
  "allowMouse": true,
  "allowKeyboard": false,
  "allowedMouseButtons": ["left"],
  "constrainTo": "clientArea",
  "requireForeground": true,
  "activateBeforeInput": false,
  "maximumTextLength": 500,
  "allowSystemKeys": false
}
```

Keep the default client-area constraint. Keyboard input requires foreground
verification; a profile combining `allowKeyboard: true` and
`requireForeground: false` is invalid. `type_text` does not use the clipboard.
`key_press` accepts bounded, allowlisted chords rather than scripts.
Text payloads are not audited or echoed in result metadata.

`click_at_point` checks native bounds and hit-tests the point before dispatch.
An unrelated occluding window causes rejection. Keyboard operations verify the
foreground window and focused HWND's process ownership immediately before
dispatch and recheck foreground afterward. Windows `SendInput` is a global,
non-atomic transport: pre/post checks reduce risk but cannot eliminate a
foreground change during dispatch. Do not use unattended native input while a
person or another automation process is interacting with the same desktop.
Clients may disable the native-input tools without affecting semantic tools.

`restore_window` waits for a non-minimized, valid native rectangle rather than
treating a queued restore request as success. `activate_window` reports failure
when Windows foreground-lock policy denies activation. No permanent topmost
state is imposed.

### Optional activation before native input

Set `nativeInput.activateBeforeInput: true` in the attached profile to attempt
foreground activation before each native mouse or keyboard operation when the
target is not already foreground. This permission defaults to false and requires
`requireForeground: true`; it does not enable native input or keyboard permission.
Detach and reattach after changing the profile.

The broker makes one activation request using the existing verified-target
`BringWindowToTop`/`SetForegroundWindow` path and polls the foreground postcondition
within its lifecycle timeout (two seconds by default, ten seconds maximum).
It does not inject fake user input, attach thread input queues, request permanent
topmost state, or bypass Windows foreground-lock restrictions. Activation denial
returns `WindowActivationFailed` without dispatching input. This is a best-effort
request, not a guarantee of focus across a conversational sequence.

Activation does not restore a minimized window implicitly: restore it explicitly
first. After activation, mouse geometry, confinement, and hit ownership are still
checked; keyboard foreground, focus, and process/window ownership remain mandatory
immediately before dispatch and are checked again afterward. Cancellation before
dispatch prevents input. Failed or partial dispatch and postdispatch focus loss
never trigger automatic reactivation or input retries; input may already have
occurred, so inspect the application state before deciding what to do next.

## Captures

`capture_application_window_image` returns an MCP image plus JSON metadata; hosts
can render the image without manually decoding a structured base64 result.
`capture_application_window` retains its original structured result for
compatibility and is deprecated. Its image is in `value.base64Data`, with
`value.mimeType`, `value.width`, and `value.height`.

Captures still require `enableScreenshots: true`. With `privacyMode: true`
(the default), password fields and
`sensitiveAutomationIds` are redacted. If enumeration, sensitivity, or sensitive
bounds cannot be established, capture fails closed rather than returning an
unredacted image. An explicit `privacyMode: false` profile instead allows
unredacted values and images and skips UIA sensitivity discovery; image metadata
identifies this mode. It does not disable screenshot authorization or window
confinement. Reload and detach/reattach to adopt a changed privacy policy.
HWND capture does not silently fall back to desktop pixels.
Provider support varies across legacy/custom/GPU-rendered applications;
a successful native capture call is not proof that the application's provider
rendered every control faithfully. Minimized windows must be restored before
capture.

Capture metadata includes source geometry and a capture ID. Capture-relative
clicks are refused after detach/reattach, profile revision changes, window
movement/resizing, or transform expiry. Take a fresh image before retrying.

## Profiles and launch behavior

`list_application_profiles` identifies the loaded revision, timestamps,
generation, and whether its file has changed. `reload_application_profiles`
validates the whole registry and swaps it atomically. Invalid files leave the
previous valid registry in place. An active session retains its original
profile and permissions; detach and reattach to adopt an edited policy.
`get_application_state` reports the session revision and staleness.

Launching defaults to refusing an already-running matching executable.
`ifAlreadyRunning: "attach"` attaches only when there is one verified match;
`"launchNew"` requires an explicit multiple-instance profile permission.
Launch failure closes only the newly spawned, owned process, then uses bounded
process-tree termination if graceful shutdown does not finish. The error
includes the spawned PID and cleanup outcome. Attached/pre-existing processes
are never terminated by this cleanup path.

## Partial UIA results

Tree/snapshot inspection returns usable nodes alongside bounded diagnostics,
partial/truncation indicators, and property failures. Provider failures do not
silently become empty successful results. Selector matching does not treat a
failed property read as a match. Inspect the diagnostic operation, phase,
exception type, HRESULT, and candidate location before retrying. Diagnostics
omit stack traces and control values.

## Interactive Windows qualification

Pure geometry, policy, reload, diagnostic, and MCP discovery tests can run on
Linux. Native Windows behavior requires an interactive Windows desktop; a
passing non-Windows test run is not qualification of these operations.

Build the solution and test app on Windows, then run the integration category:

```powershell
dotnet build DesktopComputerUse.sln
$env:DESKTOP_COMPUTER_USE_INTERACTIVE_TESTS = "1"
$env:DESKTOP_COMPUTER_USE_TEST_APP = (Resolve-Path "tests/DesktopComputerUse.TestApp/bin/Debug/net10.0-windows/DesktopComputerUse.TestApp.exe").Path
dotnet test tests/DesktopComputerUse.Automation.Tests --filter Category=WindowsIntegration
bash scripts/run-quality-analysis.sh
```

Qualify both privacy policies separately. The `PrivacyMode=Disabled` integration
case explicitly allows screenshots and sets `privacyMode:false` on its owned
fixture profile. It verifies unredacted reads/writes for a configured sensitive
field, capture of sensitive and password controls, and HWND-only window capture
with zero redacted controls. Windows/provider password masking is not removed.
The privacy-enabled cases remain unchanged; passing the disabled case does not
establish that enabled redaction works with the provider.

To reproduce only the privacy-disabled case after building and setting the
environment variables above:

```powershell
dotnet test tests/DesktopComputerUse.Automation.Tests --no-build --no-restore --filter "Category=WindowsIntegration&PrivacyMode=Disabled"
```

Follow diagnostic reruns with the full quality runner above for Microsoft CRAP
and complete-level mutation analysis. Native keyboard-input qualification still
requires foreground activation regardless of privacy mode.

The quality runner requires Bash (Git Bash on Windows, not WSL Bash), Python 3,
.NET 10 SDK, and Stryker.NET. Run
`bash scripts/install-quality-tools.sh` to build the pinned
[Microsoft crap4csharp](https://github.com/microsoft/crap4csharp) analyzer adapter.
On ARM64 Windows, Stryker's bundled VSTest runner uses an x64 test host, so the
x64 .NET 10 Windows Desktop Runtime must also be available. Install it alongside
the ARM64 SDK, or set `DOTNET_ROOT_X64` to a private x64 runtime directory.
This does not change the SDK selected by `global.json`.
It merges real coverage from every test assembly before checking
that every method's CRAP score is strictly below 20, then runs complete-level
mutation testing without excluding native Windows implementation files.
The adapter calls Microsoft's analyzer directly; it does not substitute another
CRAP implementation. Missing coverage retains its `N/A` metric and is gated
using the conservative zero-coverage bound. Missing native coverage remains
visible in the mutation report.

Before a release, repeat these cases in a disposable test application, not a
production/backend-connected application:

| Scenario | Required observation |
| --- | --- |
| Secondary monitor and a monitor left/above primary | HWND bounds and clicks retain physical negative origins; no primary-monitor offset |
| 100% and 150%/200% mixed DPI | Native client transforms and image coordinates match the actual target |
| Minimized window | Restore waits for valid bounds; capture never returns another window |
| Foreign window covering target | Mouse hit-test rejects dispatch; HWND capture excludes foreign pixels |
| Foreground-lock denial | Activation returns failure; keyboard dispatch does not occur |
| Foreign focused HWND | Keyboard dispatch is refused |
| Password/sensitive control with unreadable bounds | Capture fails closed |
| Legacy provider throwing per property/child | Partial inspection reports diagnostics; actions do not target an unreadable candidate |
| Move/resize after capture | `click_capture_point` rejects the old capture ID |
| Repeated launch, ambiguous existing processes | No new process by default; attach refuses ambiguity |
| Launch timeout/cancellation before main window | Spawned PID and owned-process cleanup outcome are visible |
| Invalid profile edit/reload | Last valid registry and active-session policy remain unchanged |

For monitor, DPI, RDP, and foreground-lock cases, record the OS version, monitor
layout/scales, integrity levels, native geometry, capture metadata, and observed
result. A GitHub-hosted single-monitor runner is not a substitute for this matrix.
