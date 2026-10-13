# Typed symbol material properties

The flat Property Editor provides alpha-aware color swatches for actual LED background/lit/unlit colors, vessel liquid colors, cylindrical-tank materials, and supported drawing/gauge/thermometer/pipe colors. Boolean options such as vessel stand/agitator/fill, valve reverse flow and tank scale use native checkboxes. Industrial machine state palettes remain in Project → Symbol States; an unused legacy machine `color` property is not misleadingly promoted to a material picker.

Core's allocation-free `SymbolPropertyKinds` describes which renderer inputs have these editor kinds. Controls reuses `StyleValueEditor` for lazy native ColorPicker flyouts and exposes `ColorAccepted` only on explicit confirmation. The optional reset-button visibility removes inherited-style UI from ordinary component properties; existing style-class/state editor behavior is unchanged.

Typing a valid color commits on Enter or leaving the field. Use color commits directly; Cancel retains the old value. Invalid input does not enter the project. Equivalent RGB/ARGB spellings create no redundant history. Color/Boolean changes use ordinary revision-guarded designer transactions with Undo/Redo. Selection, document-generation and inspector-generation guards prevent a detached old input from committing into another component. Qualified bindings remain read-only and update their displayed value without document writes. These are flat editors, not a nested property-tree implementation.

The unchanged vector renderer receives the edited snapshot and retains its existing picture cache. No color picker exists until requested, no new render timer or global cache is introduced, and metadata lookup is allocation-free. Color editing does not replace state palettes or process tags. UI-only inspection and runtime preview retain complete portable project defaults.

Six engine regressions validate the metadata boundaries; the package consumer checks the independent API; the fourteenth real Uno browser suite covers native picker cancellation/validation/alpha, text validation, no-op edits, Undo/Redo, drawing and liquid colors, Boolean flags, qualified bindings, full export equality and tablet use. Existing raster and browser suites remain enabled. Executed claims belong to exact Actions revisions; SwiftShader and raster allocation fixtures are not physical-GPU performance or desktop runtime qualification.

The artwork/schema and bounded hexadecimal color syntax remain ScadaSpace-specific. Full original component/CSS/Gateway parity is not implied. Chart-renderer/path-cache, configurable-popup and nested-property-tree work is unchanged.

## Native text synchronization regression

The first full run passed all thirteen prior browser suites and picker cancellation, alpha application, and Undo/Redo. The immediate Enter/no-op test then exposed a stale text mirror: the native TextBox already contained the new ARGB spelling, while the wrapper's cached text depended on the later TextChanged notification. The document stayed unchanged but the editor failed to restore its original equivalent spelling.

Text now reads directly from the native input. Setting a value also synchronizes a differing native input even when the cached value already equals the requested one. Enter and focus-loss validation read TextBox.Text; the wrapper no longer treats a queued notification as the current input. Existing no-op and invalid-input assertions remain unchanged. Two additional native tests exercise immediate Enter and focus-loss commits of actual changed colors, including one-transaction and Undo checks. The deferred notification still drives the ordinary preview event, without adding a timer or a delayed commit.


## Tablet selection readiness review

Run 38451212817 at head 56c5a6c passed the new material suite, core and desktop jobs,
but the earlier symbol-choice suite timed out selecting Pump after tablet Fit. Its
saved state has no application errors and an empty selection; the script had only
required positive bounds, not bounds at the new zoom. That evidence identifies an
insufficient input precondition, not a proved renderer/input-dispatch defect.

Both symbol editor suites now require three distinct published ticks with matching
view/component identity, revision, viewport and geometry. Native bounds must agree
between both observers and match the declared pixel size times the current zoom.
The exact observation used for each single mouse click is archived. Eight pure
regressions cover stale scale, mismatched rectangles, identity/revision changes,
invalid dimensions and duplicate publications. Every previous browser assertion
remains, with an added tablet scaled-geometry assertion. This helper intentionally
covers unrotated pixel-layout designer fixtures, not arbitrary transformed layouts.
It neither invokes managed commands nor retries failed pointer inputs.
