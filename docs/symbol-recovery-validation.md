# Native symbol recovery validation

PR #11 passed all ten browser suites at `d9ca24cdd6b551069eb2878c2d541e83e8b23166`. The main-branch rerun at merge `46c1de0a3fe2b77c19619f63ae3a464a5d0da17a` passed the first nine suites, then exposed an incomplete readiness predicate in the symbol binding-recovery test.

The test corrected a deliberately failed binding and waited for `dataError == null` and animation eligibility. Those values change during input evaluation, before the subsequent native drawing frame clears `renderError`. The assertion could therefore observe the old drawing diagnostic while the corrected model had already scheduled its next frame. The saved post-failure state shows successful recovery: null data/render errors, running state, advancing rendering, no binding diagnostics and no unhandled browser/application exceptions.

The test now records the draw count before correcting the input and waits for a higher successful draw count, null data/render errors and animation eligibility. It then checks the unchanged native control identity. This exercises the full correction-to-drawing path and fails if the symbol never repaints; no fixed delay or blind retry is used, and the original recovery assertion is retained.

Passive symbol observations gain a weakly associated object identity, following the existing ViewInstance observation approach. The observation table does not retain a symbol after its other owners release it and exposes no command or property setter. A separate assertion verifies distinct identities for the native controls. The renderer, model, scheduler and error-recovery implementation are unchanged by this follow-up.

All ten browser suites, engine/raster tests, standalone package checks and desktop/browser build gates remain enabled. Source definitions are not passing evidence; final results belong to the exact matching Actions run. Main must pass its own browser revalidation and Pages revision check before deployment is considered complete.
