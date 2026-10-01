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

Failed password or sensitive-ID reads suppress value access. Snapshot labels,
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

## Validation boundaries

Linux tests exercise the same observation, traversal, matching, snapshot-enrichment,
and diagnostic logic through provider-independent delegates and synthetic provider
exceptions. They do not qualify live FlaUI providers or Windows desktop behavior.
CRAP analysis retains unexecuted Windows adapters, and mutation reports retain their
`NoCoverage` mutants. Interactive Windows integration coverage is still required.
