# Resilient UI Automation diagnostics

`SafeAutomationElementReader` guards individual provider property reads and
node-local child enumeration. Expected UIA/COM provider failures produce sanitized
diagnostics; cancellation and programming errors still propagate. Failed nodes and
successful siblings remain in the result.

Inspection and application snapshots distinguish `partial` (provider failures)
from `truncated` (depth or node limits). Both may be true. Diagnostics identify
the operation, phase, property, candidate ID, depth, exception type, HRESULT,
retryability, and correlation ID without exposing provider messages or stack
traces. Additional profile/process/window context is supported.
Native postdispatch failures additionally retain dispatched-input counts and
`inputMayHaveOccurred`; these failures warn against automatic retry. The
`afterDispatch` phase is conservative even when the exact dispatch count is unknown.

With privacy mode enabled, failed password or sensitive-ID reads suppress value access. Snapshot labels,
paths, geometry, and view signatures reuse captured properties rather than
performing unguarded live reads. Fuzzy resolution consumes those snapshots and
skips candidates with failed identity/safety properties or required patterns,
while preserving candidates with optional-property or child-enumeration failures.

`ControlSelectorResolver.FindMatchesWithDiagnostics` returns matches plus skipped
candidate/provider diagnostics and truncation metadata. The existing
`FindMatches` signature is retained and returns the same diagnostic-bearing list.
Controller integrations must carry these diagnostics into returned summaries or
errors rather than discarding them. `IsComplete` distinguishes complete traversal
with skipped property-failed candidates from an unreadable subtree or any traversal
limit. `EnsureCompleteForAction()` throws structured `IncompleteControlSearch`
when uniqueness cannot be established. The legacy `FindMatches` wrapper invokes
this guard automatically; discovery callers wanting partial results must use
`FindMatchesWithDiagnostics` directly. An unreadable subtree therefore cannot
silently turn into `ControlNotFound`, nor can a visible match authorize an action
when an unseen duplicate may exist.
Fuzzy discovery similarly retains visible candidates but marks
`traversalComplete: false`; an incomplete snapshot cannot return a selected
candidate or claim `Resolved`/`NotFound`.

For partial inspection, delegate the controller's tree construction to
`ControlObserver.Inspect(root, profile, depth - 1, profile.MaxResults, token)`.
The observer's `maxDepth` counts edges from the root (zero includes only the
root), matching snapshot depth semantics; the existing controller depth counts
levels. The returned `ControlTreeNode` has aggregate root failures and distinct
partial/truncated metadata.

## Coordinate compatibility

`ControlSummary.Bounds` and snapshot absolute bounds explicitly declare
`coordinateSpace: "physicalVirtualScreen"` and `units: "pixels"`. Negative
coordinates are valid. UIA screen bounds must be captured in a Per-Monitor V2
DPI-aware process; compared with a DPI-unaware process, these are a **breaking
value-semantics change on DPI-scaled systems**. Snapshot `relativeBounds` instead
declare `coordinateSpace: "windowRelative"` and `units: "normalized"`.

## Legacy IE/MSHTML providers

UIA2 and UIA3 can expose the same defective underlying MSHTML accessibility
provider. Switching backends is not expected to repair a
`PropertyNotSupportedException` for `Name`, `AutomationId`, or `ClassName`.

Selector property failures exclude only the affected candidate and remain in
returned diagnostics. Valid siblings later in document order can still match.
If no readable candidate matches, the result is `ControlNotFound` with skipped
candidate diagnostics, not a blanket provider failure. Unreadable child subtrees
and traversal limits still block actions because unseen duplicates cannot be
ruled out. Avoid arbitrary indices in changing content when stable IDs or
ancestor scopes are available.

Nested ancestor selectors resolve from the outermost scope inward. An ancestor's
`index` is applied to matches inside its own preceding ancestor scope; the leaf
selector then searches only descendants of the selected ancestor, excluding that
ancestor itself. Thus a button under the third combo box does not resolve to a
button under the first combo box. Accessibility hierarchy, not screen overlap,
defines containment: legitimate popups may extend outside their parent bounds.

An advertised pattern is not proof that its provider can execute it.
`set_control_value` reports `UnsupportedPattern` when `SetValue` rejects execution
with `InvalidOperationException`; it does not silently switch to keystrokes.
`invoke_control` provider failures identify `phase: "executePattern"` and
`property: "Invoke"`, distinct from `resolveSelector` failures.
Once an action reaches the provider, `inputMayHaveOccurred: true` and
`retryable: false` prevent unsafe assumptions about replay. No action is
automatically retried: a failed invocation can already have changed application
state. Read values or take a fresh capture before selecting a supported alternate
interaction.

### Explicit privacy opt-out

Profiles default to `"privacyMode": true`. This preserves sensitive-value
redaction and fail-closed screenshot privacy. For trusted automation against a
provider that cannot report sensitivity, explicitly configure:

```json
{
  "privacyMode": false,
  "enableScreenshots": true
}
```

These are profile fields, not a per-tool override. Reload, detach, and reattach;
active sessions retain their original profile and cannot silently downgrade
privacy during a reload. Listings, application state, value results, snapshots,
and image metadata expose the privacy mode.

When disabled, UIA values (including passwords/configured sensitive fields)
can be returned, and HWND captures skip sensitivity traversal and redaction.
This avoids the MSHTML privacy-discovery failure without introducing best-effort
redaction that could misrepresent an image as protected. The image is explicitly
unredacted. `enableScreenshots`, HWND/geometry checks, and no-screen-fallback
rules still apply. Optional Copilot profile-ranking inputs retain their own
password/value sanitization.

`get_control_value` returns `value`, `isValueRedacted`, `privacyMode`, and
diagnostics. Empty text is `""`, protected text is `null` with
`isValueRedacted: true`, and unsupported or failed Value-pattern reads are errors.
Metadata tools already expose readable values, but this explicit tool avoids
confusing unavailable data with an empty field.
The mode does not repair a provider that rejects reads/writes and does not
authorize global mouse or keyboard access. Audit logs never include field values.

## Validation boundaries

Linux tests exercise the same observation, traversal, matching, snapshot-enrichment,
and diagnostic logic through provider-independent delegates and synthetic provider
exceptions. They do not qualify live FlaUI providers or Windows desktop behavior.
CRAP analysis retains unexecuted Windows adapters, and mutation reports retain their
`NoCoverage` mutants. Interactive Windows integration coverage is still required.
