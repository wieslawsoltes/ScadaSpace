# Bounded browser validation

`python tests/browser/run_all.py --workers 2` runs the eleven native Uno suites with at most two independent browser-suite processes at once. `--workers 1` retains a serial mode. Every suite continues to launch its own browser/context, import or load its own project, and write its existing distinct evidence directory. The only shared service is the read-only local static application server.

The explicit suite registry includes smoke, structured runtime, pages, trees, history, workspace, events, hierarchy, styles, symbols and symbol states. No individual assertions were removed or replaced by orchestration success. All suites are collected even if an earlier suite fails. A nonzero exit, exception, malformed result or 900-second suite timeout makes the overall invocation fail. A timeout terminates only that newly created suite process group, including its browser children, and does not retry it.

`TestResults/browser-runs` records separate suite logs, ordered per-suite return codes/durations and an aggregate result. Failed logs are also tailed into the job output. The original screenshots, observations, console errors and assertion results remain in each suite's directory and are uploaded alongside the runner report.

Five dependency-free runner contract tests verify the complete unique registry, concurrency limits, complete result collection after failures, error/malformed-result handling and the two-worker bound. Their injected failures are intentional test fixtures; only the unittest result indicates whether those contracts pass. They do not replace executing the actual browser suites.

This changes test orchestration, not rendering performance or product concurrency. Observed job durations vary by runner and are not hardware-independent speed guarantees. The complete core, browser and desktop-build gates still precede Pages deployment and exact public revision verification. Browser tests use SwiftShader; passing them does not qualify a physical GPU or every native desktop operating system.
