# Validation

## Reproducible jobs

The `Validate` workflow builds the engine projects and Skia graphics package, runs the deterministic console regression suites, builds the desktop application, publishes the actual Uno WebAssembly application and drives that browser application with Playwright. Test failures are not converted to success. Pages deployment is gated on successful core, browser and desktop-build jobs on `main`.

The browser tests send real pointer and keyboard input. The application's `window.scadaApp` object provides read-only observations of values and rendered bounds; it does not expose a JavaScript command dispatcher or managed-code evaluation backdoor. Tests assert outcomes after UI input, not by directly changing the simulation through that object.

## Artifacts

`core-results-and-source` contains JSON/JUnit engine reports and an archive of the exact tested source. `browser-validation` contains individual assertions, browser console output, the final observed state, rendering-context diagnostics, exported project JSON and screenshots. `browser-app` contains the published application. Package artifacts contain the separate engine and Uno `.nupkg` files.

A screenshot is captured on browser failure as well. Inspect both screenshots and assertion reports; a successful build alone is insufficient evidence that the application starts or that input works.

## Findings addressed during implementation

The first actual browser run exposed a WebAssembly-only tagged-value JSON round-trip failure even though desktop engine tests passed. An explicit `ScalarJsonConverter` was added so the wire representation does not depend on runtime reflection assigning readonly record-struct members. Additional hardening tests cover malformed tagged values, ordered reentrant/concurrent tag notifications, bad-quality preservation, history gaps, transaction rollback and dirty-state savepoints.

## Limits of the evidence

CI Chromium deliberately uses ANGLE/SwiftShader. This exercises Uno's real WebGL/Skia composition path through a software GPU implementation; it does **not** validate physical GPU acceleration, power use, driver compatibility or production rendering throughput. The context probe records renderer information and does not treat a created WebGL context as proof of hardware acceleration.

Desktop CI currently compiles the shared desktop target. It does not certify runtime behavior on Windows, macOS or Linux. No test connects to real process equipment. Browser localStorage and ephemeral in-memory tag/history/alarm state are not durability or distributed-system tests.

## Current results

Use the Actions run and artifacts for the exact commit being evaluated. Final verified run identifiers and counts will be recorded after the complete browser suite succeeds; intermediate compilation success is not presented as full functional validation.
