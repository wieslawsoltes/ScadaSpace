# Style classes and native authoring

ScadaSpace's local style-class workflow is implemented in the reusable Core, Designer and Controls libraries. It uses the public Perspective workflow as a behavior reference, not its source, assets, project archive or CSS implementation.

## Open and edit

Use **Project → Style Classes…**, the Project Browser's **Styles** resource, or an individual style resource. Selecting a component first enables its assignment checkbox. Without a selected component, class authoring remains available.

The editor presents a searchable class inventory, grouped Text/Background/Border/Spacing inputs, a preview and explicit Applied Properties. Numeric/color fields accept text; font weight, italics and text alignment offer typed native choices. Blank values and the reset button remove the class-local declaration rather than recording a default. Collapsing a group retains its staged values. Unknown style properties are preserved and identified, not executed as CSS.

Color flyouts create a native alpha-enabled ColorPicker on demand. **Use color** accepts a validated temporary color; **Cancel** or light dismissal preserves the previous field. Invalid text remains visible with a diagnostic, cannot be accepted, and does not replace the existing field. Invalid class properties hide the preview rather than presenting the last valid sample as current input.

Enter the desired name/path before **New**, **Duplicate** or **Rename**. Class names may contain slash-separated folders. **Update references on rename** is an explicit local refactoring convenience; disabling it preserves old assignment names, which become missing until reassigned, matching the documented Perspective rename behavior. Deletion stages removal of the definition and its explicit assignments. Neither operation rewrites arbitrary strings in event parameters or custom values.

**Apply** commits all staged class/assignment changes as one undoable document transaction and keeps the dialog open. **OK** applies and closes. **Cancel** discards only changes since the last Apply. Invalid or stale edits cannot partially overwrite the document. A document observer exception after a successful commit is still reported, but the draft records that the edit already committed. A semantic no-op does not produce a whole-project undo entry merely because dictionary insertion order changed.

## Cascade and reusable APIs

`StyleRules` validates class names, property kinds and bounds. `StyleColor` parses ASCII hexadecimal spans into explicit ARGB channels without a temporary character array. `StyleResolver` owns an ordinal catalog, applies unique class names alphabetically, then overlays inline declarations. `ResolvedStyle` contains immutable values, missing classes, unsupported properties and the winning declaration's class/inline origin.

Resolver cache hits compare the current component assignments and inline map with owned snapshots. Equal-sized replacements and case-only changes are detected even when an incoming dictionary uses a case-insensitive comparer. Concurrent reads of otherwise unchanged nodes are synchronized; concurrent mutation of project dictionaries is not supported. `ProjectRuntime.UpdateViews` reuses unchanged catalogs and validates changed catalogs before replacing either the project or resolver.

`StyleClassDraft` owns class maps and component assignment lists, and indexes distinct referencing components. Property/assignment updates validate both sides before either changes. Reference counts do not traverse the project per read; rename/delete rewrite only affected assignment lists. The standard `DesignerDocument` still owns complete-project validation and undo snapshots at actual commits; this work does not replace that transaction mechanism.

`StyleValueEditor` is a native `UserControl`, so disabled input uses Control's enabled/focus behavior, not only pointer hit-test suppression. `StyleClassEditor` coordinates detached edits and preview. `ComponentStyle` adapts the typed values to native TextBlock/Control roots and decoration frames. Labels retain explicit class text formatting across runtime refreshes, and unchanged unstyled label foreground values reuse their brush. Frame opacity includes the background and border, not only the inner content.

## Formats and limits

Colors are `#RRGGBB` or **`#AARRGGBB`**. Eight digits are ARGB, not CSS RGBA. Numbers use typed Scalar numbers; font weight is Normal/SemiBold/Bold, italics are Boolean, and label alignment is Left/Center/Right. Font size spans 6–200 logical pixels, opacity 0–1, border width 0–128, corner radius 0–1024 and padding 0–256.

Catalogs allow 1,024 classes with up to 128 properties each; a component may name 64 classes. The resolved result has a separate 8,320-property union budget so independently valid classes do not fail merely because their combined size exceeds the individual-class limit. Known properties still retain their type/range checks. Overall project and structured-value budgets remain active. Existing missing imported names may be retained; assigning a newly nonexistent class is rejected.

## Recovery and verification

Six earlier local commits through `7cc37ceebe1a4b3974259aaf706fc76d27082b22` were initially unpublished and uncompiled. PR #10 recovered their source on real main ancestry in functional commits, retaining the original local bundle separately. The initial native compilation exposed an inaccessible legacy FontWeights helper and Grid lacking IsEnabled; the adapters now use the public WinUI helper and a native enabled control root.

The complete candidate at `be6e012a1cee77a25689374639848f24c372d03f` passed its core and desktop jobs: 295 engine checks, 15 adapter tests, 16 package-consumer checks and allocation invariants. Its 184 tracked files match the downloaded source archive. Those results do not alone establish browser success; the final PR and main Actions reports are the merge/deployment evidence. All nine browser suites remain enabled, including actual style authoring, typed input, color cancellation/errors, rendering precedence, rename modes, export/undo and tablet rendering.

Benchmarks use warmed equivalent operations and allocation/correctness gates, not machine-dependent time thresholds. Cached cascade reads, indexed reference counts and ARGB parsing measured zero bytes per repeated operation in that core run. This is not whole-application FPS or physical-GPU evidence.

## Remaining boundaries

This is a bounded local vocabulary, not full CSS or Perspective style parity. Native root-level styling does not restyle every custom subcontrol or Skia symbol's internal typography. Styles apply when a visual host is built/rebuilt; arbitrary live style-map mutation and runtime style bindings are not supported here. Advanced stylesheets, variables, pseudo-class states, animation, media rules, complete themes, arbitrary docking and pixel-identical UI remain outstanding.

Previously excluded chart-renderer/path-cache, configurable-popup and nested-property-inspector proposals remain untouched. Browser CI uses SwiftShader; desktop compilation is not desktop runtime qualification. The application is an independent isolated simulation, not an industrial-control safety/security certification.

References: [Perspective Style Classes](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/styles/style-classes), [WinUI ColorPicker](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/color-picker), [WinUI Control](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.control).

## Native interaction follow-up

The first complete native run of the recovered source passed all eight prior suites and 25 style checks, including color confirmation/cancellation/errors, rendering, class edits, assignments and undo. It then timed out selecting a class after clearing an inventory filter: the requested row still had its previous filtered position while the full list returned. The test now requires the complete matching projection and ordered realized bounds before further pointer input; it retains the selection assertion rather than retrying clicks.

The screenshots also exposed swatches that measured only four logical pixels wide after their text was cleared. Color and inheritance controls now occupy their full 27/25-pixel columns, with a browser geometry regression. Class management buttons fill their existing grid cells. A local Chromium reproduction attempt was administrator-blocked at navigation; candidate runtime evidence continues to come from the release-built CI browser suites, not that attempted local run.

The next run exposed a real layout problem after widening these controls: the reset column occupied the native property scroll area's right edge and was clipped by its scrollbar overlay. The form now reserves a 24-pixel right gutter and stretches within its viewport. The geometry regression also requires the reset button to end before that right-edge overlay, not merely report a positive width. The original reset interaction and resulting inherited-font assertion remain unchanged.
