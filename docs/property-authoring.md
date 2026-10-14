# Typed property authoring and inspector presentation

The flat inspector creates String, Number, Boolean, Object, Array and Null
properties through native Add Property / Add Custom Property dialogs. Each type
keeps its own input draft while switching types; Cancel discards all of them.
Object and Array inputs accept ordinary JSON, including nested typed values,
without interpreting it as executable code or as the project's tagged format.
The selected root kind must match the JSON. Null is explicit and read-only in the
creation form. A duplicate name is rejected only in the selected category, so
Props and Custom can have independently owned values with the same name.

## Correctness fixes

Property fields previously used the rounded process-display `Scalar.ToString()`
format for numeric edit text. `PropertyValueText` now uses invariant round-trip
formatting, including very small and large finite binary64 values. Display
formatting elsewhere remains unchanged. Numbers are still binary64, not arbitrary
precision decimals. Null displays as `null` rather than as an ambiguous blank.
Malformed structured inline edits are now caught as local validation errors;
they do not escape the native event handler or publish a partial document change.

The reusable `PropertyCreationDraft` owns a document, component identity, category
and generation. It validates names, values and duplicate membership before the
transaction, rejects changed/replaced documents, and cannot apply twice after a
successful add. Even a postcommit observer exception cannot duplicate a committed
operation. The native host additionally guards the current inspection, and blocks
canvas shortcuts, resource switching and Preview while the creation dialog is
open. Successful additions participate in existing Undo/Redo and selection rules.

## Visual and allocation changes

Shared row construction places native binding controls in a left gutter, followed
by a wider, aligned name column and the value. Accessible names and full-path
tooltips distinguish binding commands. Number, Boolean and String values use
independent orange, blue and green shades respectively; null/structured/quality
text is neutral. This follows the documented type-color convention without using
proprietary graphics. Color swatches, family dropdowns and Boolean controls retain
their ordinary editing behavior.

Scalar, color and choice bound-text refreshes compare value and quality before
formatting or touching native text. Timestamp/revision-only changes do not force
new strings or repeat JSON serialization. Brushes are shared per inspector.
This reduces that specific idle-refresh work; it is not a claim of zero whole-app
allocation, higher physical-GPU FPS, or full inspector virtualization.

## Limits and validation

New typed input is bounded to 65,536 UTF-16 characters, names to 128 characters,
and JSON retains the existing 24-depth / 10,000-node constraints. Imported larger
structured values are not silently truncated. Changes beyond the authoring input
budget are rejected explicitly. Existing schemas, source bindings, state palettes,
process-display formatting and project export format are unchanged.

Seventeen engine regressions cover lossless values, culture independence, input
limits, invalid JSON, duplicates, ownership, revision rejection, no-mutation
staging, Undo/Redo and postcommit observers. Three additional standalone package
checks execute typed creation and tiny-number round trips. The seventeenth native
browser suite defines 29 assertions covering actual creation/editing, type drafts,
recoverable invalid input, precision, type colors, binding gutter placement,
read-only bindings, idle formatting counters, keyboard isolation, tablet use and
whole-project equality after cancel/reject/undo. All earlier suites remain enabled.
Execution results belong to exact CI revisions, not just these definitions.

Reference: [Perspective Component Properties](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/working-with-perspective-components/perspective-component-properties)
(Property Data Types, User Created Properties and Bindings). This work is flat
property authoring, not the excluded nested-property-tree implementation. Full
Perspective/Gateway, arbitrary-precision data, pixel-identical appearance and
native desktop runtime qualification are not implied. The excluded chart-renderer
and configurable-popup proposals remain unchanged.
