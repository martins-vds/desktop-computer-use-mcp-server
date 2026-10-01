# Feedback: `desktop-computer-use` MCP Server

**Context:** Driving a real, unmodified legacy WinForms app (`Vista.exe`, a nomination/scheduling
system run from a network share) via the `test-winforms-app` profile, in a dual-monitor
environment (primary 1280x800, secondary 1280x720). This is exactly the "messy legacy app"
case the tool should shine at, so the friction below is worth treating as representative,
not an edge case.

## TL;DR

The core attach/screenshot loop works, but almost everything past that required me to drop
out of the MCP server entirely and hand-roll Win32 P/Invoke in PowerShell: restoring a
minimized window, forcing z-order, reading the *real* window rect, and sending mouse clicks.
None of that should have been my job. The two highest-impact fixes would be:

1. **Structured, non-opaque error payloads** (see below) instead of 3 generic error codes.
2. **A raw coordinate click tool** + **a trustworthy, screen-space bounds contract** so callers
   don't need Win32 interop at all.

---

## What worked well

- **`list_application_profiles` / `attach_application` / `get_application_state`** — clean,
  predictable, good diagnostic fields (`hasExited`, `activeWindowTitle`, `ownsProcess`,
  `backend`). `ownsProcess: false` on attach was a nice touch — shows real intent around
  process-lifecycle ownership tracking.
- **Root-level `inspect_controls` (maxDepth=1)** — accurate `name`, `automationId`,
  `className`, `isEnabled`, `isOffscreen`. When it worked, it worked well.
- **`capture_application_window`** — when the window was actually in the foreground and on
  top, this reliably returned a real, high-quality screenshot of the live app state. The
  underlying capture mechanism itself is fine; the problems are all in the surrounding
  contract (see below).

## What didn't work well

### 1. Profile changes require a full MCP server restart, silently
I edited the profile JSON (`mainWindow.title`, `operationTimeoutMs`) four separate times to
chase a `WindowNotFound`/`Timeout` flip-flop, and **none of the edits took effect** until I
asked the human to manually restart the server from VS Code's MCP panel. There's no
`reload_profiles` tool, no file-watcher, and no signal anywhere (not in
`list_application_profiles`, not in the error payload) that the loaded profile is stale.
This alone cost ~8 wasted tool calls and a round trip to the user.

**Ask:** either hot-reload the profiles directory, or expose a `reload_profiles` tool, or at
minimum put a config fingerprint/timestamp in `list_application_profiles` output so an agent
can tell "the thing I just edited is not what's loaded."

### 2. `launch_application` has no "already running?" check, and failures still spawn processes
First call timed out (`Timeout`, 10s). Second call returned `WindowNotFound`. Both actually
**started a new `Vista.exe` process** even though neither reported success — I only discovered
this by shelling out to `Get-Process`. By the time I checked, **5 duplicate instances** of a
real (if Qual/test) backend session were running. A tool whose failure modes silently leave
orphaned processes behind is dangerous for anything with session/license/backend cost.

**Ask:** `launch_application` should either (a) detect an existing matching process and offer
to attach instead of relaunching, or (b) report the PID of whatever it spawned even on
failure, so the caller isn't left guessing and shelling out to `Get-Process` to clean up.

### 3. Control-tree enumeration is a hard, opaque dead end for real apps
`inspect_controls`, `snapshot_application_schema`, and `find_control` **all** failed with the
exact same generic error the moment they tried to walk past the root window — even at
`maxDepth=2, maxResults=1` (i.e. just the *first* child):

```json
{"succeeded": false, "error": {"code": "AutomationFailure", "message": "The automation operation failed unexpectedly. See the server log for details."}}
```

This is almost certainly a child control whose UIA provider throws on a property read (very
common with legacy custom grids/MDI containers — exactly the kind of control this app has).
That's an expected, well-known category of problem in UIA automation, and it shouldn't be
fatal to the whole call:

- **No partial/best-effort results.** One bad child kills the entire tree walk instead of
  being skipped with a placeholder node.
- **No exception detail surfaced to the caller.** "See the server log for details" is not
  actionable for an agent — I have no access to that log. At minimum, include the inner
  exception type/message (`ElementNotAvailableException`, `COMException` HRESULT, etc.) and
  *which* control/index/depth it failed at.
- **Same generic code for structurally different problems.** `Timeout` and `WindowNotFound`
  and `AutomationFailure` are all I ever saw, for what were clearly different underlying
  issues (stale profile cache, real timeout, exception-throwing child, offscreen window).

**Ask:** make tree-walking resilient (catch-and-skip per node, return what you could get plus
a `partial: true` + list of failed nodes), and return structured diagnostic detail inline in
the response, not just in a log I can't read.

### 4. `capture_application_window`'s contract doesn't hold for minimized/occluded windows
The tool description says it "Captures only the attached application's main window." In
practice:
- When the window was minimized (`isOffscreen: true`, `bounds: 0,0,0,0` per `inspect_controls`),
  capture sometimes failed outright, and sometimes **silently returned a screenshot of a
  completely different window** (VS Code) that happened to occupy the same screen region.
  That strongly suggests the capture is a screen-region grab at last-known bounds, not a true
  isolated per-window capture (e.g. `PrintWindow` with `PW_RENDERFULLCONTENT`, or the Windows
  Graphics Capture API) — both of which can render minimized/occluded windows faithfully.
- There is **no tool to restore/focus/raise the window**. I had to hand-roll
  `ShowWindow(SW_RESTORE)`, `SetForegroundWindow` (which silently no-ops due to Windows'
  foreground-lock timer — a very common gotcha), and finally
  `SetWindowPos(HWND_TOPMOST → HWND_NOTOPMOST)` as a workaround, entirely outside the MCP
  server, in PowerShell.

**Ask:** add `restore_window` / `bring_to_front` (or just make `capture_application_window`
do this internally before capturing), and switch the capture implementation to something that
doesn't depend on z-order/visibility at all.

### 5. Reported window bounds don't match real screen coordinates — this was the big one
`inspect_controls` reported the main window's `bounds` as `{x: 26, y: 26, width: 1200, height: 700}`.
The **real** `GetWindowRect` for the same window, at essentially the same moment, was
`Left=1272, Top=-8, Right=2568, Bottom=680` (on a secondary 1280x720 monitor starting at
virtual-desktop X=1280). These are wildly different coordinate spaces, with no documentation
of which one `bounds` is actually in (client-relative? primary-monitor-relative? DPI-scaled?).

Because I had no raw-click tool (see next point) and had to compute click coordinates myself,
I trusted the MCP-reported `bounds`, computed a click target from it, and **the click landed
on the wrong monitor entirely**, hitting VS Code instead of Vista. This is the single costliest
bug in the whole session — it doesn't just fail safely, it silently clicks the wrong thing.

**Ask:** document (and fix) the coordinate space of every `bounds`/position field. It should
be virtual-desktop screen coordinates (matching `GetWindowRect`), full stop, so it composes
safely with any click tool. If DPI virtualization is involved, say so explicitly and expose
the scale factor.

### 6. There is no raw coordinate click tool at all
Every interaction tool (`invoke_control`, `select_control_item`, `scroll_control`,
`set_expanded_state`) requires resolving a control via selector first — exactly the operation
that reliably crashes for this app (#3). That's a hard dead end: I can *see* the app via
screenshots but have **no supported way to act** on anything the selector-based tools can't
resolve. I ended up hand-writing `SetCursorPos` + `mouse_event` P/Invoke in PowerShell, with
all the coordinate-space risk described in #5.

**Ask:** add a `click_at_point(x, y, button)` (and ideally `type_text`, `key_press`) tool that
operates in the same documented coordinate space as the bounds fields, scoped/validated against
the attached window's own rect so it can't accidentally click outside the app. This single
addition would have let me skip essentially all of the manual Win32 interop in this session.

### 7. Large tool outputs (screenshots) force an awkward manual decode pipeline
`capture_application_window`'s result isn't inlined — it's written to a `content.json` /
`schema.json` pair on disk, and I had to reverse-engineer the JSON shape
(`$json.value.base64Data`), write a one-off PowerShell decode step, save a PNG to `%TEMP%`,
and only then call `view_image`. Every single screenshot in this session needed this 3-step
manual pipeline.

**Ask:** either return a ready-to-view image resource directly (so the host can render it
without a round-trip through the caller's shell), or at least document the output schema so
agents don't have to discover `value.base64Data` by trial and error.

---

## Prioritized fix list

1. Fix the `bounds`/position coordinate-space mismatch (#5) — this caused an actual wrong-target
   click, not just an error message.
2. Add a raw `click_at_point` tool (#6) so callers never need Win32 interop for input.
3. Make tree-walking resilient + return structured error detail instead of opaque
   `AutomationFailure` (#3).
4. Add `restore_window`/`bring_to_front`, and make capture correct regardless of z-order/minimized
   state (#4).
5. Hot-reload profiles, or expose a `reload_profiles` tool / staleness signal (#1).
6. Make `launch_application` idempotent (detect-and-attach) and never leave orphaned,
   unreported processes behind on failure (#2).
7. Simplify the screenshot output contract (#7).

## Tools I reached for that don't exist (and had to fake with PowerShell)

- Restore/un-minimize a window
- Bring a window to front / manage z-order
- Get the *true* screen-space window rect
- List monitors / virtual-desktop layout
- Click at raw (x, y) coordinates
- Reload/refresh loaded profiles without a full server restart
