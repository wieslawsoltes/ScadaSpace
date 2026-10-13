# Flat property search

The native Property Editor now has a compact search field, a clear command, and a
bound-only toggle. Search matches literal qualified property names such as
`props.diodeOnColor`, `position.width`, and `custom.note`, ignoring case and outer
whitespace. A category name can match its fields. It does not search live values,
interpret regular expressions, or construct a nested property tree.

The ordinary color pickers, Boolean switches, symbol choices and scalar editors
are retained while the user types. Filtering changes visibility, not the project
or selected component. Matching sections remain visible; unmatched sections are
hidden. A count and explicit no-results message distinguish an empty filter from
an empty inspector. Escape and the clear command reset text and bound-only mode.
Selection changes and Undo/Redo preserve the current local search. Configured
bindings, including `props.`-qualified keys, remain read-only and retain their
live evaluated values. Delete in the search field remains a native text edit.

## Reuse and performance

`PropertyFilter` is independently available in the Designer package. Its input is
bounded to 256 characters. Matching uses ordinal comparisons against paths
computed once per inspector build. Equivalent query changes preserve the query
string and revision without allocating. The Controls package keeps field widgets,
row descriptors, native flyouts, and selection guards rather than rebuilding them
on each search. It publishes a cached read-only matching-path snapshot only when
the predicate or inspected component changes; idle timer refreshes do not refilter.
This is not full inspector virtualization, an FPS benchmark, or hardware-GPU proof.

Ten engine regressions cover literal/qualified matching, case and culture behavior,
combined binding filters, reset, input limits, exception atomicity, unchanged
revision, and zero-allocation matching/equivalent updates. Two independent package
consumer checks exercise the public API without project references. A fifteenth
native Uno browser suite covers visibility, actual color/custom/Boolean edits,
Undo, read-only bindings, retained row counters, keyboard focus, desktop/tablet,
and whole-project export equality. Existing browser suites remain enabled.
Validation results apply only to the exact Actions revision that executed them.

Reference: [Perspective Designer Interface, Property Editor](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-designer-interface#perspective-property-editor),
and [Perspective Component Properties, Search Filter](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/working-with-perspective-components/perspective-component-properties#search-filter).
The independently implemented flat search moves this workflow closer to the
reference. Original regex/wildcard modes, nested editors, and full visual parity
are not claimed. The chart-renderer/path-cache, configurable-popup and nested-tree
proposals remain excluded. No proprietary source, icons or artwork are bundled.
