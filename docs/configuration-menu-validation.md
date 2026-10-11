# Configuration menu synchronization regression

The complete PR #7 suite passed at `18d9547758de1c9ffb91cd732639f4a3447f3d5c`. The main-branch rerun at merge `e057472875a8e61610d921d49b4e429571006544` then exposed a timing assumption in the event-configuration browser fixture. The earlier six suites passed, but the configuration-kind filtering step timed out.

The captured screenshot shows the native ComboBox popup open on **All configurations**, with the original three result rows and no application/binding errors. The fixture previously clicked the selector and immediately issued Home, four Down keys and Enter without waiting for native popup layout/focus. That sequence did not reliably select the intended item.

The fixture now waits for the native **Action** option's realized container bounds, clicks that actual menu row and requires both the two filtered action results and the closed popup. The existing kind-filter assertion remains. A small read-only observation enumerates container bounds only while the popup is open; it cannot set selection or invoke managed commands. The menu, index and filtering implementation are unchanged, and no fixed delay, managed shortcut or blind retry was added.

The whole seven-suite browser run remains the gate, along with the unchanged engine, package, performance and desktop-build checks. Main deployment must pass the same checks and public revision verification. Exact executed counts and results are recorded in the matching Actions artifacts, not inferred from this test definition.
