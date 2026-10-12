# Style continuation review — local source, not a published release

## Status

This continuation recovers the previous three candidate commits and adds review fixes, native editing controls, regression definitions and documentation. The GitHub `create_tree` source upload was attempted through the connected GitHub tool and blocked again: “we couldn't determine the safety status of the request.” No alternative source-upload route, implementation PR, remote commit, merge or deployment was used. The last verified remote main revision remains `4144fbc457f9f25ea2b65eef7c24b04117b239a8`.

There is no .NET SDK or C# compiler installed in this execution environment. The official SDK installer could not be retrieved (host resolution / download failures). The new C# and Uno source is therefore **not compiled, not browser-executed, and not merge-ready**. Passing JavaScript adapter tests and Python syntax checks do not verify the style implementation. No new performance numbers or rendered UI screenshots are claimed.

## Review fixes

The original candidate used the same 128-property cap for individual class maps and their merged output. Two individually valid classes with disjoint properties could pass project validation but fail in the runtime adapter after merging. Class maps retain their 128-property limit; the merged vocabulary permits the bounded union of 64 assigned classes plus inline values (8,320 maximum). All known types/ranges and unknown-value validity checks still apply. Resolution records the winning class or inline origin for each property in a read-only snapshot.

Color validation previously allocated a character array. `StyleColor` now parses ASCII hexadecimal spans into explicit ARGB channels and supplies canonical text independently of Uno and Skia. Six digits mean opaque RGB; eight mean ARGB, not CSS RGBA. Non-ASCII lookalikes, malformed lengths, whitespace, signs and invalid digits are rejected.

`StyleClassDraft.Update` validates and owns property and assignment changes before mutating either. A rejected assignment cannot leave a retained partial class edit. A reverse-reference index counts distinct referencing components, including dangling imported assignments, without scanning every component for each count. Rename/delete touch only referencing assignment lists; unrelated snapshots retain identity. Semantically restored class maps do not create whole-project undo snapshots just because dictionary insertion order changed.

The resolver and draft comparisons use owned ordinal snapshots rather than trusting incoming dictionary comparers. Replacing `foreground` with `Foreground` in a case-insensitive incoming dictionary is a real change, not a cache hit. Unknown spellings remain explicitly unsupported. `ProjectRuntime.UpdateViews` reuses an identical owned class catalog and replaces it only after a changed catalog validates; invalid replacement leaves the prior project and resolver intact.

## Native editor additions

`StyleValueEditor` is a reusable Uno control. Numeric/hex input keeps precise text entry. Font weight, italic and alignment use native choices with an explicit inherited state. A reset button removes the class-local property rather than storing a fabricated default. Color controls create their native ColorPicker lazily and expose alpha plus explicit hexadecimal input. Use Color transfers the temporary color into the class field; Cancel or light dismissal does not change it. Invalid color text keeps the flyout open and cannot change the field. Class edits still require Apply/OK before affecting the project.

Property groups collapse independently while retaining their inputs. An Applied Properties readout shows the explicit class values. Invalid current values hide the preview instead of presenting the last valid sample as if it matched the current fields. Equivalent preview values do not repeatedly rebuild the preview brushes. These are source implementations; their actual Uno layout, accessibility and interaction remain unverified here.

Rename includes an explicit **Update references on rename** switch. Checked is the existing ScadaSpace refactoring convenience. Unchecked uses `StyleRenameMode.PreserveAssignments`: old names remain in assignments and become dangling references. This matches the documented Perspective rename behavior; it is not equivalent to silently updating all assignments. The default is retained for compatibility with the prior local candidate. Neither mode rewrites strings in arbitrary custom values or event payloads.

## Regression definitions and reproducibility

`StyleTests.cs` retains all 19 prior style tests. `StyleReviewTests.cs` adds cases for merged limits, immutable origins, distinct/indexed references, rename modes, missing-reference collisions, snapshot identity, atomic failure, semantic no-op Apply, ordinal comparison, catalog reuse and ARGB parsing. The browser style suite retains its 20 original assertions and adds color confirmation/cancellation/error cases, typed choices, inherited resets, collapsed sections, invalid-preview presentation and rename-mode export/undo. Passive observations expose native option geometry only; tests send real pointer/keyboard input and do not call managed commands.

Standalone package-consumer definitions now also verify declaration origins and ARGB parsing. Performance definitions add indexed usage counts and span-based color parsing with allocation gates. **These added tests and benchmarks have not executed in this environment.** Exact definition counts are reported separately from executed checks in the downloadable validation JSON.

With a .NET 10 SDK, the existing workflow remains the reproduction entry point: run all engine suites and the performance executable, build the desktop target, publish `net10.0-browserwasm`, verify packed consumers, serve under `/ScadaSpace/`, and execute all nine browser suites including `tests/browser/styles.py`. The local source archive and patch identify the full source state; the bundle includes the labeled source-recovery baseline, not the full remote repository history.

## References and boundaries

- Perspective style editor and alphabetical/inline precedence: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/styles/style-classes
- WinUI ColorPicker configuration and flyout confirmation guidance: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/color-picker

No proprietary artwork or implementation is included. This is not full Perspective style or pixel-equivalent UI parity. CSS, theme resources, animations, states, media rules, live style bindings and complete styled subcontrol semantics remain outstanding. Earlier excluded chart-renderer, configurable-popup and nested-property-inspector work remains untouched. This candidate is still an isolated-simulation development source, not industrial qualification.
