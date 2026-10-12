> Historical local-candidate record. Source publication resumed in PR #10; see [current style documentation](style-classes.md) and the exact-revision Actions reports for current validation. The uncompiled/unpublished status below describes the earlier recovery session, not a later validated release.

# Style class workflows — local, unverified candidate

## Publication and validation status

This work is based on the exact source tree of the previously merged and deployed revision `4144fbc457f9f25ea2b65eef7c24b04117b239a8` (tree `1314e69387f628017e9f809177ad0289b50fee85`). The starting source was recovered from its source archive, not an existing Git working directory; the local history therefore has a clearly labeled recovery commit followed by the candidate commits. Do not confuse those local commit IDs with GitHub commits.

A new branch, `feat/style-class-workflows`, was created remotely at that base revision. The first `GitHub.create_tree` call carrying the new style sources was blocked by the platform because it could not determine the request's safety status. No implementation commit was created remotely, and the blocked source upload was not retried through another route. No new PR, merge or Pages deployment was completed. The published application remains the prior revision.

The execution environment has no .NET SDK. Official SDK retrieval was unsuccessful, and direct package/network downloads were also unavailable. **The new C# code has not been compiled, the Uno editor has not been run, and the new engine/browser/package/performance checks have not been executed.** Static review and Python/YAML parsing cannot establish build or runtime correctness. This is a source candidate, not merge-ready or production-qualified software.

The candidate adds **19 C# regression definitions**, **20 actual-Uno browser assertion definitions**, two standalone package-consumer checks (14 total defined) and a cached style allocation benchmark. The existing eight browser suites remain registered, and the new style suite is appended as the ninth. No prior assertion was removed. Actual local validation consists of the unchanged navigation adapter suite (15 passes), Python syntax checks, workflow parsing, whitespace/patch checks and source archive verification. The separate validation JSON records executed checks and unexecuted scope explicitly.

## Native editor implemented in source

Select a component, then use **Project → Style Classes…**. The Project Browser's **Styles** resource and individual class entries route to the same editor. Component selection is optional; without it, assignment controls are disabled but class authoring remains available.

The editor has a searchable class inventory, grouped Text/Background/Border/Spacing fields, a class name/path field, New/Duplicate/Rename/Delete commands, an assignment checkbox, reference counts and a native preview. Enter the desired new name in the name field before creating, duplicating or renaming. Blank property fields remove that property from the class, allowing earlier classes or component defaults to supply it.

Apply commits the detached class and assignment edits as one normal DesignerDocument transaction and leaves the dialog open. OK applies and closes. Cancel discards edits since the last Apply. Rename updates explicit `StyleClasses` references; deletion stages removal of those references as well as the definition. Neither operation rewrites strings inside arbitrary custom data, script text or event payloads. Invalid fields block application, and stale drafts reject newer document generations instead of overwriting them. Post-commit observer failures remain visible while the draft acknowledges an already committed transaction.

`StyleClassDraft` lives in the reusable Designer package. `StyleRules`, `ResolvedStyle` and `StyleResolver` live in Core. `StyleClassEditor` and `ComponentStyle` live in the reusable Uno Controls package; the application partial only coordinates menus, selection and dialogs.

## Cascade and runtime adapter

Class names are de-duplicated and applied in ordinal alphabetical order. Inline values then override class values. This corrects the prior assignment-list-order behavior and follows the documented alphabetical precedence model, without claiming identical CSS collation or every Perspective styling rule.

A resolver owns a validated snapshot of the class catalog and retains weakly keyed component results. Repeated unchanged lookups reuse an immutable result; same-sized inline or assignment replacements are detected. Concurrent reads of otherwise unchanged nodes are synchronized. Concurrent mutation of project dictionaries is not supported. Missing classes and unsupported property names are returned in the resolution result rather than being misrepresented as rendered CSS.

The runtime creates a catalog at construction and replaces it in `UpdateViews`. Existing workbench document edits rebuild their view hosts through the normal change/rollback path, applying the new styles. Arbitrary style-dictionary mutation on an already built visual host and runtime bindings to style fields are not implemented as live style updates in this candidate.

The local adapter supports foreground, font size, font weight and italics on native text/control roots, label text alignment, background/border/padding where the native root exposes those properties, and decorated-frame opacity. Non-Control graphics/text receive a frame when decoration is configured; buttons use their native corner radius. Corner radius is not universally applied inside every custom/native control template. These are root-level styles, not recursive restyling of all subcontrols, a CSS engine, or typography support inside Skia-drawn symbols/charts.

Opacity is applied to the decorated frame, so the background/border are not left fully opaque while only its inner element fades. Unstyled label foreground brushes are cached across unchanged refresh values; explicit class foreground/font size are not overwritten by the label's normal property refresh. Neither optimization has a measured whole-app speedup in this unbuilt candidate.

## Supported property format and bounds

This is ScadaSpace's typed property vocabulary, not Perspective's project archive/CSS representation. Colors use `#RRGGBB` or **`#AARRGGBB`** (ARGB, not CSS's eight-digit RGBA order). Numbers remain typed `Scalar` numbers, not numeric strings. Font weight is Normal, SemiBold or Bold; italics use a Boolean; label alignment is Left, Center or Right.

Font size accepts 6–200 logical pixels, opacity 0–1, border width 0–128, corner radius 0–1024 and padding 0–256. The catalog allows at most 1,024 classes, each with 128 properties; a component may name at most 64 classes. Names allow slash-separated folders but reject empty/dot/dot-dot segments, commas, control characters and backslashes. The existing overall project and structured-value bounds still apply.

Unknown properties remain in the source model, copy/rename operations and exports and are listed as unsupported in the editor. They are not executed as CSS. Existing missing imported class references can be retained while editing other assignments; newly assigning a nonexistent class is rejected.

## Explicit remaining scope

No advanced stylesheets, CSS variables/selectors, inheritance across DOM subtrees, hover/focus/pressed rules, responsive/media rules, animations, style binding authoring, full theme support or complete Perspective style editor parity is provided. The dialog layout and preview have not been visually inspected in a running Uno build. This source must pass compilation, package-consumer checks, all existing/new browser interactions and visual review before any merge or deployment.

Previously excluded chart-renderer/path-cache, configurable-popup and nested-property-inspector proposals remain untouched. The application remains an independent isolated simulation, not full Ignition Perspective or a production industrial-control system.

Public behavior reference: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/styles/style-classes . The documented style folder, visual editing workflow, alphabetical class precedence and inline override behavior informed the design; no proprietary code or artwork is included.

## Subsequent local review

See [style review validation](style-review-validation.md) for corrected merged budgets, indexed references, atomic edits, explicit rename modes, typed/native color controls and unchanged publication/compilation limitations. The reference-updating rename described above is a ScadaSpace extension, not the documented Perspective rename default.
