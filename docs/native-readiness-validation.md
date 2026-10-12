# Native readiness and main-branch revalidation

PR #13 passed all 287 browser assertions at `609e98b22351f7a19c3e1d52745bfc4481c66a08`. Its main merge `a60dfbae42745888133f4c6b7e4ec889c5d301e7` then passed the engine, raster, package and desktop jobs, but failed three older browser-suite readiness assumptions. The new 30-assertion state suite passed on both revisions. Pages correctly remained blocked by the failed browser job.

## Captured failures

The structured-runtime fixture timed out waiting for its initial file chooser. Its screenshot actually shows the adjacent **Page Configuration** dialog: pointer input reached a different toolbar command, rather than triggering a broken file picker. Its helper previously read one set of command coordinates and clicked immediately. The application had no unhandled or binding errors. The fixture now requires an initial native drawing and equal usable command geometry across three distinct published refresh ticks. It sends the pointer action once, after observing the current layout; it does not retry input or invoke a managed command.

The workspace fixture waited a fixed 500 ms after resizing, then clicked Fit and asserted tablet panel layout. The captured final observation already shows the expected 1024×768 viewport, hidden project filter, visible Property Editor and bottom resource tab, but the previous desktop zoom remains. The fixture now waits for the observed responsive layout before clicking Fit at stable native command coordinates, then requires the component's actual width to match its declared width times the fitted scale. The original tablet assertion remains; a further assertion checks the scaled layout.

The symbol fixture captured its idle-cache baseline after the Preview flag changed and a fixed number of evaluation ticks. Those observations can precede the first drawing of the replacement runtime controls. At failure the native symbols had successfully drawn one picture each, with no data/render errors. The corrected baseline requires a new view identity, explicit runtime mode and a completed successful draw for every measured symbol. It then compares both native control identity and picture count over the idle interval. It cannot pass by replacing a control with another one having the same count. The baseline is retained in the log, and a new assertion verifies that it belongs to fully drawn runtime controls.

## Scope of the fix

`native_readiness.py` reads the existing observation channel only. Its tracker rejects repeated timestamps, missing/nonfinite/offscreen geometry, and resets on layout, viewport, project or view identity changes. Six contract tests exercise these cases without a browser. The structured-runtime, workspace and symbol fixtures use it for native command targeting; the other eight suites and their assertions are retained.

View observations add one read-only `editing` field, obtained from the actual ViewHost. No command/property setter is exposed. The product's renderer, input routing, symbol caches, layout and file-import implementation are unchanged by this test-readiness follow-up. The two-worker concurrency bound, timeout/failure handling and all eleven suites remain enabled. There are no blind retries, dropped assertions or swallowed errors.

The final-head PR gate and the subsequent main gate must both pass. An earlier successful PR run is not sufficient to claim the main build has deployed. Exact counts, source identity and public revision belong to the matching Actions artifacts.

## Shared startup readiness

The first follow-up run passed the three repaired suites, but the same initial toolbar-coordinate problem appeared in the events suite. Its failure screenshot again shows Page Configuration instead of the requested file picker. This confirmed that startup readiness needed a shared observation contract rather than only per-test targeting changes.

The workbench previously published `ready = true` as soon as it had a model and positive page dimensions. It now publishes ready only after the actual view is loaded, key toolbar bounds and page size remain unchanged over three distinct native timer ticks, and a visible custom symbol has completed a drawing or diagnostic when such symbols exist. Text-only views use the stable native layout condition. The readiness decision is latched after startup, so it adds no ongoing traversal cost. It never delays or changes product commands, fonts, layout or rendering; it corrects the passive readiness signal used by all eleven suites. The DOM ready attribute now mirrors that signal rather than always reporting true. Later view transitions and responsive resizes still use their own explicit readiness checks.
