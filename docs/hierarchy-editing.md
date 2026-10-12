# Designer hierarchy workflows and performance review

The native Component menu and canvas context menu now offer **Wrap In Container → Coordinate Container** and **Modify Source View**. These are independently implemented workflows based on the public Perspective component documentation, not an Ignition project-format adapter or a claim of full designer parity.

## Wrap In Container

Select one or more contiguous siblings in a pixel-coordinate container and choose **Coordinate Container**. The new container occupies their combined bounds and is inserted at the same stacking position. Original child IDs, event configurations, style settings and offsets are retained; child coordinates are rebased inside a zero-padding wrapper so the components do not move on screen. The wrapper receives a unique view-local name and becomes the selection. A single Undo restores the original hierarchy and source text; Redo recreates the same stored identities.

Only selected roots are moved when selection includes both an ancestor and a descendant. Mixed-parent, noncontiguous, locked, percentage-coordinate and layout-managed selections, and view roots, are explicitly rejected. Noncontiguous siblings cannot be grouped without potentially changing their stacking relation with intervening components. This iteration therefore does not silently alter those designs or advertise unimplemented flex/column/breakpoint wrapping.

Bindings on each moved child that refer to its old `parent` are converted to the old parent's stable ID (or `root`). This includes direct and bidirectional property bindings, parser-recognized expression references, expression transforms, expression-structure leaves and expression-valued history query options. Descendant bindings are unchanged because their parents do not change. Indirect tag-path parameters and quoted reference-like text are not rewritten. A parent ID ambiguous in the existing reference grammar, or an affected expression that cannot be parsed, stops the operation before the hierarchy is mutated.

`ExpressionEngine.RewriteReferences` uses the existing parser's reference occurrences. It is not a text-replacement or regular-expression heuristic: function names, operator words, quoted strings and escapes are distinguished from references. Unchanged expressions retain their original string instance, and rewritten expressions retain the normal input-size and syntax constraints.

## Modify Source View

Right-click an EmbeddedView or FlexRepeater, or select it and use the Component menu. **Modify Source View** opens its current evaluated `view` property rather than a stale project default. It reopens a closed resource tab without editing project data. Bad-quality, empty and missing source paths are diagnosed while leaving the current design open. This is local source navigation, not remote resource discovery or complete deep-selection editing.

## Correctness review

A failed DesignerDocument transaction replaces its project objects with the validated pre-edit snapshot. Previously the workbench was not notified of that object replacement, so its existing host and inspector could retain the failed objects even though document generation had not advanced. The new `Restored` event tells consumers to rebind. The workbench refreshes the host, explorer and inspector from the restored model without adding an undo entry. Observer failures are bounded diagnostics and do not mask the original edit error.

Flat PropertyInspector fields now retain their owning document, component, generation and inspection-instance version. A delayed LostFocus from a detached field cannot apply its draft to a different component or replay a rejected edit during rollback rebuilding. The inspection version matters because rollback deliberately leaves the committed generation unchanged. This is a guard in the existing flat inspector, not the previously excluded nested-property-inspector proposal.

Nested Execute/Undo/Redo during an active edit is rejected before it can corrupt history. Normal post-commit observer behavior remains separate from rollback. Direct mutations outside Execute are not supported by the designer's revision tracking.

## Reusable caches

`DesignerDocument.Find`, `FindParent` and `FindView` share a lazily built ID/parent/view index. It is invalidated around every transaction and on rollback, Undo and Redo; lookups within a mutation are rebuilt so newly added/removed nodes are visible. Repeated steady-state lookup does not traverse the tree or allocate an iterator.

`LayoutSnapshotCache` in the Layout package owns a read-only flattened slot array keyed by root identity, caller revision and dimensions. The designer's hit-testing and selection adorner use this same cache instead of flattening repeatedly for pointer selection and redraw. Invalid requests do not discard the previous valid snapshot; Clear releases retained model references. Slot bounds are snapshots, while each slot's ComponentNode is the caller's mutable definition, so every model change must advance the supplied revision or clear the cache.

The cache does not introduce a new renderer, physical-GPU acceleration claims or complete transformed-coordinate editing. Native Skia drawing still uses the existing Uno backend. Existing chart full-series reads, general telemetry allocations and whole-project undo snapshots remain separate performance work.

## Validation

The deterministic regressions cover geometry/stacking/ID preservation, nested selections, parent binding read/write semantics, expressions/structures/history options, invalid wrapping, parser-safe reference rewriting, hierarchy invalidation, rollback rebinding and immutable layout caching. The standalone NuGet consumer additionally wraps and undoes a component without any application project references.

`tests/browser/hierarchy.py` drives the release-built Uno application through its actual file picker, context submenus, inspector fields, native numeric input, buttons and resource tabs. It checks Undo/Redo, exported binding/default retention, rejected inspector edits followed by successful correction, actual cache counters during selection, evaluated source navigation, missing-source recovery and tablet hit-testing. Observations are read-only and do not execute managed commands. All seven preceding browser suites remain enabled.

The performance program compares the original scan/flatten strategies against their cached equivalents on the same 2,000-component fixture. It gates result equivalence, cache identity and allocation invariants; elapsed timings are reported, not machine-specific pass thresholds. These measurements are not whole-app FPS, input latency, physical-GPU throughput or an exhaustive performance audit. Executed results and exact source revisions are retained in the matching Actions artifacts.

Reference: [Working with Perspective Components, Ignition 8.3](https://docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/working-with-perspective-components), including Wrap In Container and Modify Source View. No proprietary source or artwork is included. Full Perspective/Gateway/device/scripting/security parity, native desktop runtime qualification, screen-reader qualification and the previously excluded renderer/popup/nested-inspector work remain outstanding.
