# Plan: remediate desktop-computer-use feedback

This plan addresses the real-world findings in [`desktop-computer-use-feedback.md`](desktop-computer-use-feedback.md).

## Outcome

The MCP server should let an agent operate an attached legacy Windows application without writing external PowerShell or Win32 P/Invoke.

It will retain semantic UI Automation as the preferred path and add a bounded, profile-controlled native fallback:

```text
semantic UIA tools
  → preferred

attached-window native tools
  → fallback when UIA is incomplete
```

Native input must never become unrestricted desktop access. It remains default-off, profile-gated, and constrained to the verified attached process/window. Keyboard input always requires foreground verification and cannot opt out.

## Priorities

| Priority | Workstream |
|---|---|
| P0 | Physical virtual-screen coordinate contract and native window geometry |
| P0 | Profile-gated raw click with attached-window validation |
| P0 | Structured diagnostics and partial UIA failures |
| P0 | Idempotent launch and owned-process cleanup |
| P1 | Restore and activate window tools |
| P1 | Reliable HWND-based capture and MCP image output |
| P1 | Reloadable profiles and visible configuration revisions |
| P2 | Type, key, drag, pointer, and capture-relative input |
| P2 | Multi-monitor and mixed-DPI qualification automation |

---

## Phase 1: explicit geometry and diagnostic contracts

Add contracts for:

- Physical virtual-screen points and rectangles.
- Window-client points and rectangles.
- Capture-image points.
- Monitor layout and virtual desktop bounds.
- Window geometry, DPI, state, and HWND.
- Structured operation diagnostics.

Canonical coordinates:

```text
physical pixels in Windows virtual-desktop screen space
```

Negative X/Y values are valid when monitors are left of or above the primary monitor.

Extend errors with:

- Operation and phase.
- Exception type.
- HRESULT/Win32 error.
- Profile ID and revision.
- Process ID and HWND.
- Candidate ID/depth when relevant.
- Retryable flag.
- Diagnostic correlation ID.

Do not expose stack traces or sensitive values.

### Acceptance

- Every bounds field identifies its coordinate space and units.
- Secondary-monitor coordinates match `GetWindowRect`.
- Known UIA/COM/native failures receive specific error codes.
- Release notes identify the change of existing `ControlSummary.Bounds` values to physical virtual-screen pixels as a breaking value-semantics change on DPI-scaled systems.

---

## Phase 2: Windows desktop/window broker

Create:

```text
DesktopComputerUse.Automation/Windows/
```

Implement:

- Per-Monitor V2 DPI awareness.
- `GetWindowRect`.
- DWM extended frame bounds.
- Client-to-screen conversion.
- Monitor enumeration and work areas.
- Virtual desktop bounds.
- Per-window DPI.
- Window visibility/minimized/cloaked/foreground state.

Declare Per-Monitor V2 in the Windows server application manifest or set it at the beginning of `Program.cs` before dependency construction. DPI awareness must be active before `MtaAutomationWorker`, FlaUI, window creation, or any geometry query.

Add tools:

- `get_desktop_layout`
- `get_window_geometry`

Return both UIA and native bounds with discrepancy warnings.

### Acceptance

- Geometry is correct on primary and secondary monitors.
- Mixed-DPI transformations are deterministic.
- No operation assumes a virtual-desktop origin of `(0, 0)`.

---

## Phase 3: bounded native input

Extend application profiles:

```json
"nativeInput": {
  "enabled": false,
  "allowMouse": true,
  "allowKeyboard": false,
  "allowedMouseButtons": ["left"],
  "constrainTo": "clientArea",
  "requireForeground": true,
  "maximumTextLength": 500,
  "allowSystemKeys": false
}
```

The default is disabled. Profile validation rejects `allowKeyboard: true` together with `requireForeground: false`.

Add:

- `click_at_point`
- `type_text`
- `key_press`

Use `SendInput`, not `mouse_event`.

Before dispatch:

1. Verify native input is profile-enabled.
2. Verify attached process/HWND.
3. Convert the declared coordinate space.
4. Verify the point lies inside the configured client/window area.
5. Use `WindowFromPoint` to ensure the target is the attached window or its child.
6. Restore/activate if permitted.
7. Verify foreground when required.

Return requested and actual coordinates, target HWND, foreground state, and dispatch result.

For `type_text` and `key_press`, foreground verification is mandatory:

1. Verify `GetForegroundWindow` immediately before dispatch.
2. Use `GetGUIThreadInfo` to verify the focused HWND belongs to the attached process.
3. Dispatch the bounded input.
4. Verify the attached HWND remains foreground after dispatch.
5. Do not dispatch when either precondition fails.

### Acceptance

- Points outside the attached application are rejected.
- Occluding foreign windows are detected.
- Secondary-monitor clicks cannot land on VS Code.
- Raw-input tools remain discoverable for MCP tool-list stability but return `RawInputDisabled` unless the attached profile enables the requested capability.
- Typed text is never written to logs.
- Keystrokes are never dispatched when the attached window is not foreground.
- Keystrokes are never dispatched when keyboard focus belongs to another process.

---

## Phase 4: window lifecycle tools

Add:

- `restore_window`
- `activate_window`

Restore using `ShowWindowAsync(SW_RESTORE)`.

Activation should attempt a bounded sequence and verify `GetForegroundWindow`. If Windows foreground-lock rules prevent activation, return `WindowActivationFailed` rather than success.

`ShowWindowAsync` only queues the restore request. Poll `IsIconic`, window placement, and non-degenerate native bounds with a bounded timeout before returning success. Return `WindowRestoreFailed` if the postcondition is not reached.

Do not leave the target window permanently topmost.

### Acceptance

- Minimized windows restore.
- Restore success is returned only after the native postcondition is observed.
- Foreground activation is verified.
- Failure reports current and target HWND/process details.

---

## Phase 5: resilient UIA traversal

Introduce:

```text
SafeAutomationElementReader
```

Read each property independently. A bad provider property should produce a node diagnostic, not abort the tree.

For each node:

1. Capture minimal identity.
2. Read properties with individual guards.
3. Attempt child enumeration in a node-local guard.
4. Preserve the node when children fail.
5. Continue with successful siblings.

Return:

```json
{
  "partial": true,
  "truncated": false,
  "root": {},
  "failures": [
    {
      "candidateId": "node-0013",
      "depth": 2,
      "phase": "enumerateChildren",
      "exceptionType": "COMException",
      "hresult": "0x..."
    }
  ]
}
```

Use the same reader for:

- `inspect_controls`
- `snapshot_application_schema`
- `ControlSelectorResolver`
- `FuzzyControlResolver`
- `find_control`
- Every selector-backed action tool

Catch and skip provider failures per candidate. Return skipped-candidate diagnostics so one throwing custom control cannot prevent a valid sibling from resolving.

### Acceptance

- One bad child does not remove successful siblings.
- One bad property does not remove the node.
- Partial and truncated results are distinct.
- One bad selector candidate does not prevent `find_control` or an action tool from resolving another candidate.

---

## Phase 6: reliable capture and image results

Create:

```text
IWindowCaptureProvider
```

Evaluate:

1. Windows Graphics Capture.
2. `PrintWindow(PW_RENDERFULLCONTENT)`.
3. Screen-region capture only as an explicit validated fallback.

Every provider must preserve the existing security boundary:

- Enforce `profile.EnableScreenshots`.
- Redact password controls and `sensitiveAutomationIds`.
- Convert sensitive rectangles through the selected provider's source rectangle and DPI transform.
- Fail closed when a known sensitive descendant cannot be mapped into capture coordinates.
- Never return an unredacted fallback image.

Add:

- `capture_application_window_image`

Return an MCP `ImageContentBlock` plus metadata:

- Capture method.
- Source screen rectangle.
- Image dimensions.
- DPI.
- Restore/activation behavior.
- Occlusion-safety.
- Capture ID and timestamp.
- Redacted-control count and redaction failures.

Keep the existing structured-base64 tool for one compatibility release and mark it deprecated.

Never silently return pixels from another process.

### Acceptance

- Occluded/minimized behavior is explicit and repeatable.
- VS Code renders the image directly.
- Screen fallback refuses unsafe states.
- Capture is refused when sensitive redaction cannot be proven complete.

---

## Phase 7: reloadable profiles

Replace the immutable registry with:

```text
IReloadableApplicationProfileStore
```

Track:

- Source file.
- Loaded time.
- Last-write time.
- SHA-256 revision.
- Store generation.
- Validation status.

Add:

- `reload_application_profiles`

Reload all profiles atomically. Invalid edits must not replace the last valid registry.

Extend `list_application_profiles` with:

- Revision.
- Loaded time.
- Last-write time.
- `isStale`.
- Generation.

Active sessions retain their original revision. `get_application_state` reports when the session profile is stale and requires detach/reattach.

### Acceptance

- Profile edits can be reloaded without restarting MCP.
- Agents can prove which revision is active.
- Invalid profile files do not break the loaded registry.

---

## Phase 8: idempotent launch and cleanup

Extend launch:

```json
{
  "profileId": "vista",
  "ifAlreadyRunning": "fail",
  "onLaunchFailure": "terminateSpawned"
}
```

`ifAlreadyRunning`:

- `fail` — default; return matching PIDs/windows.
- `attach` — attach when one match is unambiguous.
- `launchNew` — allowed only by explicit profile policy.

On launch failure:

1. Return spawned PID.
2. Attempt graceful close.
3. Kill the owned process tree after a bounded timeout.
4. Report cleanup status.

Never kill pre-existing or attached processes.

### Acceptance

- Default repeated launch does not create duplicates.
- Failed launches do not leave silent owned processes.
- Every failure reports spawned PID and cleanup status.

---

## Phase 9: screenshot-relative input

Capture responses should include:

- `captureId`.
- Source screen rectangle.
- Image dimensions.
- Pixel-to-screen transform.
- Session/profile/window generation.

Add:

- `click_capture_point`

This maps screenshot coordinates to physical virtual-screen coordinates and rejects stale captures.

### Acceptance

- The model can click the exact screenshot it observed.
- Window move/resize invalidates stale captures.

---

## Phase 10: protocol, documentation, and Linux parity

When native input ships, update:

- The README statement that currently says coordinate clicking/global typing are intentionally absent.
- The Security boundaries section.
- MCP tool descriptions.
- Profile examples and migration notes.

The replacement security contract states that native input is:

- Default-off.
- Profile-gated.
- Attached-process/window constrained.
- Client-area bounded by default.
- Foreground-verified for keyboard input.
- Audited without text payloads.

MCP tool discovery remains stable. Profile policy controls whether a native-input invocation is accepted; clients may additionally disable the `desktop-native-input` tool set.

For every new Windows MCP tool, add a matching Linux tool definition that returns `PlatformNotSupported`, preserving the common MCP discovery surface. Update `PortableProfileStore` for reload contracts and add a parity test that compares Windows and Linux tool names.

### Acceptance

- README security claims match the implemented tool surface.
- Windows and Linux servers expose the same tool names.
- Linux tools return explicit platform diagnostics.

---

## Testing

Add pure tests for:

- Virtual desktop transforms.
- Client/screen conversions.
- DPI scaling and rounding.
- Point-in-window validation.
- Capture transforms.
- Error serialization.
- Profile revisions.
- Launch policy.

Add Windows integration coverage for:

- Secondary monitor.
- Negative virtual-screen origin.
- Mixed DPI.
- Minimized and occluded windows.
- Foreground-lock failure.
- Throwing UIA providers.
- Safe raw click and text input.
- Failed launch cleanup.

GitHub-hosted runners do not model multi-monitor/RDP behavior well. Use a self-hosted interactive Windows qualification runner or a repeatable manual qualification script.

---

## Pull request sequence

1. Diagnostic and coordinate-space contracts.
2. Native geometry/monitor tools and Per-Monitor V2.
3. Partial UIA inspection and safe selector resolution.
4. Reloadable profile registry and revisions.
5. Idempotent launch and owned-process cleanup.
6. Restore/activate window tools with verified postconditions.
7. Profile-gated click/type/key tools.
8. HWND capture and MCP-native images.
9. Capture-relative input.
10. Linux companion parity, documentation, and tool-set migration.
11. Interactive Windows qualification suite.

Do not release raw input before coordinate correctness and attached-window validation are complete.

The PR sequence is the binding delivery order. Phase sections group related contracts; native input must not merge before geometry and restore/activation dependencies are complete.

## Definition of done

A VS Code Copilot agent can:

1. Reload an edited profile without restarting MCP.
2. Identify the exact loaded profile revision.
3. Launch without duplicate/orphaned instances.
4. Receive partial UIA results with actionable diagnostics.
5. Restore and activate the attached window.
6. Retrieve correct multi-monitor physical geometry.
7. Capture the target without unrelated pixels.
8. Receive directly renderable MCP images.
9. Click and type inside the attached application without external P/Invoke.
10. Be prevented from sending input to a different window or monitor.
