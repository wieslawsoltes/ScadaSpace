# Validation

## Reproducible jobs

The `Validate` workflow builds the engine projects and Skia graphics package, runs the deterministic console regression suites, builds the desktop application, publishes the actual Uno WebAssembly application and drives that browser application with Playwright. Test failures are not converted to success. Pages deployment is gated on successful core, browser and desktop-build jobs on `main`.

The browser tests send real pointer and keyboard input. The application's `window.scadaApp` and `window.scadaViews` objects provide read-only observations of values, view-instance identities, table rows and rendered bounds; neither exposes a JavaScript command dispatcher or managed-code evaluation backdoor. Tests assert outcomes after UI input, not by directly changing the simulation through those objects.

## Artifacts

`core-results-and-source` contains JSON/JUnit engine reports and an archive of the exact tested source. `browser-validation` contains individual assertions, browser console output, the final observed state, rendering-context diagnostics, exported project JSON and screenshots. `browser-app` contains the published application. Package artifacts contain the separate engine and Uno `.nupkg` files.

A screenshot is captured on browser failure as well. Inspect both screenshots and assertion reports; a successful build alone is insufficient evidence that the application starts or that input works.

## Findings addressed during implementation

The first actual browser run exposed a WebAssembly-only tagged-value JSON round-trip failure even though desktop engine tests passed. An explicit `ScalarJsonConverter` was added so the wire representation does not depend on runtime reflection assigning readonly record-struct members. Additional hardening tests cover malformed tagged values, ordered reentrant/concurrent tag notifications, bad-quality preservation, history gaps, transaction rollback and dirty-state savepoints.

## Limits of the evidence

CI Chromium deliberately uses ANGLE/SwiftShader. This exercises Uno's real WebGL/Skia composition path through a software GPU implementation; it does **not** validate physical GPU acceleration, power use, driver compatibility or production rendering throughput. The context probe records renderer information and does not treat a created WebGL context as proof of hardware acceleration.

Desktop CI currently compiles the shared desktop target. It does not certify runtime behavior on Windows, macOS or Linux. No test connects to real process equipment. Browser localStorage and ephemeral in-memory tag/history/alarm state are not durability or distributed-system tests.

## Structured runtime regressions

Three engine executables cover 37 baseline, 10 hardening and 41 structured-value/binding/table checks. The new browser suite imports a fixture through the file picker rather than replacing managed state through JavaScript. It tests bidirectional property input, synchronous expression structures, parameter updates without child replacement, local-state isolation, dynamic repeater creation/removal, numeric table sorting, invalid/valid cell edits, filtered source-row selection, export isolation and tablet resizing.

A browser regression exposed stale designer selection during navigation: the inspector attempted to read a component from the previously displayed view. The view-instance ownership guard was retained; the design surface now removes selection outside the displayed view before rebuilding the inspector.

The original nested-property inspector enhancement was not published after its upload was blocked. It is not included in the tested feature claims.

## Current results

Feature revision `94d855292327b8e4ca0db8534d0816e9b4ae0d18` passed Actions run `38091075163`: 37/37 engine checks, 10/10 hardening checks, 41/41 parity checks, 19/19 baseline browser checks and 16/16 structured-runtime browser checks. Desktop compilation and WebAssembly publication also passed. The initial control-pack command exited successfully without producing an archive; PR #2 subsequently fixed and gated that artifact gap (see [package artifact integrity](package-artifacts.md)). These figures identify an executed feature baseline, not full Perspective compatibility.

The package-consumer gate restores a temporary console application from the generated local NuGet feed without project references and executes four public-API checks. Pages publication includes a build identity that is fetched after deployment and compared with the validated commit. Use the latest run artifacts to verify the final publication revision.

## Page/navigation continuation

The page/navigation branch adds 28 deterministic engine scenarios and 15 JavaScript adapter checks. The browser page suite has 28 checks driven through the actual native Uno controls, plus the existing structured-runtime suite (16) and baseline suite with typeface validation (21). These counts describe the current test definitions; the Actions reports identify which exact revision passed them.

New checks cover page configuration undo/redo, dynamic/static route precedence, parameter/query isolation, shared dock state, modal cover behavior, auto breakpoints, resizing, native browser history, Unicode deep links and reloads, invalid UTF-8, and canonical URL size rejection. Typeface diagnostics verify the family actually decoded by Skia and proportional glyph metrics rather than merely the configured font name.

The additional configurable-popup prototype was not uploaded after a platform block and is not represented as tested or shipped. The published branch preserves the existing simple popup behavior. No physical GPU or native desktop execution qualification is inferred from these tests.
