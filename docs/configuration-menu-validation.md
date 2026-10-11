# Configuration menu synchronization regression

The complete PR #7 suite passed at `18d9547758de1c9ffb91cd732639f4a3447f3d5c`. The main-branch rerun at merge `e057472875a8e61610d921d49b4e429571006544` then exposed a timing assumption in the event-configuration browser fixture. The earlier six suites passed, but the configuration-kind filtering step timed out.

The captured screenshot shows the native ComboBox popup open on **All configurations**, with the original three result rows and no application/binding errors. The fixture previously clicked the selector and immediately issued Home, four Down keys and Enter without waiting for native popup layout/focus. That sequence did not reliably select the intended item.

The fixture now waits for the native **Action** option's realized container bounds, clicks that actual menu row and requires both the two filtered action results and the closed popup. The existing kind-filter assertion remains. A small read-only observation enumerates container bounds only while the popup is open; it cannot set selection or invoke managed commands. The menu, index and filtering implementation are unchanged, and no fixed delay, managed shortcut or blind retry was added.

The whole seven-suite browser run remains the gate, along with the unchanged engine, package, performance and desktop-build checks. Main deployment must pass the same checks and public revision verification. Exact executed counts and results are recorded in the matching Actions artifacts, not inferred from this test definition.

## Fitted viewport input

The first follow-up run selected the kind menu correctly and passed all configuration workflow checks through the last desktop interaction. It then exposed an incomplete Fit-readiness predicate at the tablet step: checking a changed zoom plus `x < 250` can accept old component bounds before the new scale has reached native layout. The final snapshot eventually had the correct fitted bounds, but the tag retained its pre-click value and no application error occurred.

The test now requires the actual component width and height to match its declared dimensions multiplied by the fitted zoom, then observes subsequent runtime refreshes before sending pointer input. It records the exact input bounds/zoom, adds a dedicated scaled-geometry assertion, and retains the original tag-write assertion. A successful tablet screenshot is captured as additional evidence. This changes test synchronization, not runtime event dispatch or the Fit implementation.
