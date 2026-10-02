# Desktop Computer Use MCP — Issues found while exploring VISTA Nominations (Qual_BK)

- **Tool version**: `desktop-computer-use-windows-x64-v0.5.0`
- **Target app**: `Vista.exe` (legacy WinForms shell hosting an embedded IE browser / HTML UI), `Qual_BK` environment
- **Profile**: `test-winforms-app` (`profiles/example.application.json`), tried with both `backend: "uia2"` and `backend: "uia3"`
- **Session context**: read-only exploration + attempted form fill-in via MCP tools (`find_control`, `invoke_control`, `set_control_value`, `inspect_controls`, `snapshot_application_schema`, native input fallback)

## Summary

The target application is a legacy WinForms host embedding an IE browser control (`MainForm → IEBrowser → Pane → ...`), which exposes a fragile, inconsistent accessibility tree over UI Automation. Several MCP tool operations either fail outright, resolve to the wrong element, or can't be used at all against this kind of app. Below are the concrete issues encountered, with repro notes, in case they're useful for the next release.

## 1. Selector resolution crashes when any sibling node fails a property read

**Symptom:** `find_control` / `invoke_control` / `inspect_controls` fail with `ProviderFailure` / `"Selector resolution was incomplete"` whenever a selector includes a `name` filter and the tree-walk has to pass over sibling nodes that throw `PropertyNotSupportedException` while reading `Name` (common in this IE-hosted content — many nodes don't support `Name`, `AutomationId`, or `ClassName` at all).

- A selector like `{"controlType": "Hyperlink", "name": "Schedule"}` reliably failed, citing a **fixed, repeatable set of candidate IDs** (e.g. `node-0012`, `node-0086`, `node-0097`, `node-0098`, `node-0101`, `node-0102`, `node-0105`, `node-0106`, `node-0110`, `node-0114`, `node-0118`, `node-0120`, `node-0121`, `node-0141`) that threw on `Name` reads, aborting the whole match instead of skipping them.
- Only selectors that matched **very early** in document order (e.g. "Search", which happened to be first) resolved successfully with a `name` filter; anything positioned after the broken nodes in traversal order failed, **regardless of which name was searched for**.
- **Workaround found:** dropping `name` entirely and using pure `{"controlType": "Hyperlink", "index": N}` selectors worked reliably, since apparently comparison-by-index doesn't force a `Name` read on every candidate. This let us brute-force enumerate the full nav list by index and read back each control's `name` from the *result* (which works fine once a node is already resolved).
- **Suggestion:** when a selector filter requires reading a property that a candidate doesn't support, skip/exclude that candidate from consideration instead of aborting the whole resolution with a hard failure. This would make `name`-based selectors usable against mixed-fidelity trees like this one.

## 2. Switching backend (Uia2 → Uia3) did not fix the above

We suspected the Uia2 (MSAA bridge) backend was the cause, so the profile's `backend` was changed from `"uia2"` to `"uia3"` and profiles were reloaded + re-attached (`reload_application_profiles`, then detach/re-launch-attach since an active session doesn't pick up a new backend automatically).

- Result: **identical failure**, same exact candidate IDs thrown, same exact error shape, under Uia3.
- Conclusion: this is inherent to how the legacy IE-hosted HTML content surfaces through UI Automation (both bridges struggle with the same underlying control), not a Uia2-specific bug.
- **Suggestion:** document that backend switching is not expected to resolve `PropertyNotSupportedException` issues on IE/MSHTML-hosted legacy content, to save users time re-testing this.

## 3. Nested `ancestor` selectors resolve to the wrong element

**Symptom:** A selector like `{"ancestor": {"ancestor": {"name": "Nominations"}, "controlType": "ComboBox", "index": 2}, "controlType": "Button", "index": 0}` — intended to find the "Open" picker button inside the 3rd combo box (Shipper) — instead resolved to (and `invoke_control` actually clicked) a **different** combo box's native dropdown toggle (Month's), not the intended Shipper combo's search/open button.

- This was confirmed by capturing a full `snapshot_application_schema` afterwards and finding a `List` control (dropdown popup, 120×422px) had become a child of the Month combo box (`parentCandidateId` matching Month, not Shipper) — i.e. the wrong control was invoked.
- Single-level selectors (`{"ancestor": {"name": "Nominations"}, "controlType": "ComboBox", "index": N}`) reliably picked the correct Nth combo box when tested directly. It's specifically the **second level of ancestor nesting** (`ancestor.ancestor`) combined with an inner `index` that misresolves.
- There are also **multiple controls with the exact same accessible name** ("Open"), since this app uses the same toggle-button pattern for a native `<select>` arrow and for two autocomplete/search pickers — with no `automationId` or other distinguishing property exposed, index-based disambiguation among same-named buttons is inherently unreliable in a dynamic UI (the toggle's `name` literally flips between "Open" and "Close" depending on expand/collapse state, so index counts shift live).
- **Suggestion:** consider flagging when a nested-ancestor + index selector's resolved bounds don't fall within the expected ancestor's bounds (a sanity check), or support filtering by positional containment (e.g. return matches sorted/scoped strictly within the immediate ancestor's screen bounds) to make this class of selector safer to use blind.

## 4. No way to read current control values ("write-only" automation)

**Symptom:** There is no "get value" tool for UI Automation `Value`-pattern controls. `get_control_properties` returns only metadata (bounds, control type, supported patterns, enabled/offscreen flags) — never the actual text/selected value. `set_control_value` / `type_text` are explicitly excluded from the audit log and don't have a read counterpart.

- This is presumably an intentional security/redaction design choice (avoid leaking sensitive business data through the audit trail), but it makes it impossible to verify that a value was actually set correctly, or to discover what an existing field currently contains before changing it.
- **Suggestion:** if this is deliberate, consider documenting it explicitly in the profile schema / tool descriptions so users don't spend time looking for a "read value" tool that doesn't exist by design. If not deliberate, a scoped read capability (e.g. gated by a separate profile permission) would make form-filling workflows verifiable.

## 5. `set_control_value` fails on custom combo boxes that advertise the `Value` pattern

**Symptom:** Three ComboBox controls (Month/Pipeline/Shipper on the Nomination Create/Edit form) all report `supportedPatterns: ["Value"]`, but calling `set_control_value` against any of them fails with:

```
AutomationFailure / ExecuteActionAsync / InvalidOperationException (0x80131509)
```

- This suggests the control advertises `Value` support without actually implementing `SetValue` (common for custom/legacy combo implementations backed by `<select>`/autocomplete widgets rendered in MSHTML). The tool has no fallback (e.g. falling back to simulated keystrokes) when the UIA `SetValue` call itself throws.
- **Suggestion:** catch `InvalidOperationException` from `SetValue` specifically and report a clearer error (e.g. "control advertises Value pattern but does not support SetValue") rather than the generic `AutomationFailure`, so the caller knows to try an alternate interaction (click + keyboard) instead of assuming their selector was wrong.

## 6. Native input (mouse/keyboard) requires foreground, which fights with a conversational/agent workflow

**Symptom:** `click_at_point` / native keyboard require the target window to be the OS foreground window at the exact instant of the call (`requireForeground: true` in the profile). In practice, during an interactive chat session, focus constantly bounces back to the chat/editor application between tool calls (the user has to type into the chat to respond), so the target window is foreground only momentarily and unpredictably.

- `activate_window` also failed consistently with `WindowActivationFailed`, because Windows' focus-stealing prevention blocks background processes from calling `SetForegroundWindow` on another app without a qualifying user input event immediately prior.
- This makes any **multi-step** native-input sequence (click field → type → click next field → type...) impractical to drive from an agent/chat loop, even with `nativeInput.enabled: true`, because there's no way to guarantee foreground state across multiple back-to-back tool calls without the human manually re-focusing the target window before every single step.
- **Suggestion:** consider an opt-in profile setting to request foreground activation via a legitimate focus-stealing exemption (e.g. `AllowSetForegroundWindow` from the target process, or attaching input queues via `AttachThreadInput`) so a single legitimately-authorized automation session can perform a sequence of native inputs without needing the user to manually alt-tab before every single action.

## 7. `invoke_control` fails on `ExecuteActionAsync` for a control that `find_control` confirms is resolvable and enabled

**Symptom:** Invoking a left-nav "Upstream" hyperlink (`{"controlType": "Hyperlink", "index": 8}`) failed 3 consecutive times with:

```
ProviderFailure / ExecuteActionAsync / phase: execute
```

...even though `find_control` with the **exact same selector**, run immediately before and between each attempt, consistently resolved the control successfully and reported `isEnabled: true`, `supportedPatterns: ["Invoke"]`.

- This indicates the failure is in the invoke-pattern execution itself (post-resolution), not selector ambiguity — and it was not a transient/flaky issue, since it reproduced identically across 3 separate calls with different internal candidate IDs showing up in the diagnostic each time.
- By contrast, other links on the same page/nav list (e.g. "Search", earlier in the session) invoked successfully without issue, so this isn't a universal invoke failure — just specific to certain controls/pages.
- **Suggestion:** add retry-with-backoff or a documented distinction between "selector couldn't be resolved" vs "resolved but the underlying UIA `Invoke()` call itself threw," since right now both surface as the same generic `ProviderFailure` shape and are hard to tell apart without close reading of the `operation`/`phase` fields.

## 8. Screenshots are unavailable for this app class

**Symptom:** `capture_application_window_image` and the deprecated `capture_application_window` both fail with:

```
Capture refused because complete redaction could not be established (PropertyNotSupportedException)
```

- This happens even though the profile has `enableScreenshots: true`. Since this WinForms+IE host exposes many controls that don't support the properties needed to verify redaction (see issue #1), the capture path apparently requires those same property reads to succeed before it will render an image, so it refuses outright rather than capturing with partial redaction.
- This removes any visual verification option for this class of app, compounding issue #4 (no value read) — there is effectively **no way to observe actual on-screen state** short of inferring it from the (partial, sometimes-wrong) accessibility tree.
- **Suggestion:** consider an explicit opt-in "best-effort capture" mode that proceeds with known-safe redaction (e.g. blacking out regions whose redaction status couldn't be confirmed) rather than refusing the whole capture.

## Net impact

For this specific class of target (legacy WinForms app hosting MSHTML/IE content with partial UIA support), the combination of (1) selector-resolution aborting on any unsupported property read, (4) no value-read capability, (5) `SetValue` not actually working despite being advertised, (6) foreground-dependent native input being impractical in a conversational workflow, and (8) no screenshot fallback meant we could reliably do **read-only navigation** (once selectors avoided `name` filters) but could not reliably **fill in or verify form data** without risking clicking the wrong control.
