# Tree and performance review validation

See [review findings and reproduction](review-performance.md) and [Tree configuration](trees.md). This document records the additional selection/cache contract and current test definitions; exact-revision Actions artifacts establish executed results.

## Selection and idle projection corrections

An explicit change to an empty `selectedPaths` array now clears the primary selection and contextual outputs. On first load, an empty default array still permits a nonempty primary `selection` input. The browser suite drives a declarative button writing an empty bound array, then checks every selection output before continuing filter, reorder, recovery and virtualization scenarios.

The cached tree projection's fast path is separated from its closure-bearing traversal, avoiding even the small closure allocation during an idle cache hit. Expanded/filter-visible projections and selected snapshots remain immutable; mutations invalidate them according to the contracts in the review report.

## Test definitions

The current source defines 169 engine checks (37 baseline, 10 hardening, 122 parity), 15 browser-history adapter tests, six standalone NuGet-consumer checks and 102 actual Uno browser assertions: 21 smoke/typeface, 16 structured runtime, 31 page/navigation and 34 Tree checks. These definition counts do not by themselves claim execution success; inspect the matching workflow reports.

The performance executable compares equivalent synthetic history/tree operations and gates allocation/correctness invariants. It records observational timing without a hardware-dependent pass threshold. Browser evaluation counters separately check one steady-state nested-view evaluation per refresh at every tested depth. See `TestResults/performance.json` and the browser reports in Actions artifacts.

## Review boundaries

The suite exercises asynchronous filter clearing, explicit empty selection arrays, zero-deadband alarm boundaries, pending-delay reset on unusable input, quoted expression punctuation, mutable binding-index invalidation, inclusive/wrapped history windows and nested refresh behavior. This is not exhaustive correctness proof, industrial security assessment, screen-reader qualification or complete Perspective parity. SwiftShader browser tests do not establish physical-GPU performance; desktop compilation does not establish runtime behavior on every native platform.
