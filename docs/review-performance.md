# Correctness and performance review

This review completes the Tree/local tag-browser development branch and addresses hot paths in the existing engines and Uno host. It is not a declaration that every Perspective feature, visual state, security boundary, or device integration is complete. The [compatibility matrix](parity.md) remains authoritative about unsupported product scope.

## Findings and changes

| Area | Finding | Implemented change and regression evidence |
|---|---|---|
| Tree filter input | Runtime polling could restore the previous filter between keyboard deletion and asynchronous TextChanged notification. | Track the committed filter separately from in-progress editor text. Actual browser tests clear the filter, recover expansion, and continue selection/editing. |
| Tree state | Reading selection scanned the whole document; repeated visible-tree reads rebuilt an immutable projection. | Indexed preorder identities, cached selection/data snapshots, projection invalidation only for relevant changes, targeted recursive expansion. Unit tests cover mutation isolation, no-op reuse, replacement and error rollback. |
| Native tree rows | Selection-only refresh reassigned ItemsSource; small branch changes recreated unrelated rows. | Reuse immutable row models and projections, splice small changes through ObservableCollection, replace large changes once. Browser checks retain selection and enforce bounded realized rows for 1,200 items. |
| History | A bounded time query copied and scanned every retained sample, then allocated a second result. | Binary lower/upper bounds on the ordered ring; copy only the requested interval in at most two contiguous segments. Inclusive endpoints, duplicate timestamps, wraparound and snapshot ownership are tested. |
| History API/CSV | Counting or reading the latest sample required a full query; CSV rounded numeric values and accepted formula-like text. | Allocation-free `Count`/`Latest` accessors; round-trip numeric formatting and formula-neutralized textual cells. These are new reusable APIs; existing chart/telemetry consumers are not all converted to incremental reads. |
| Alarms | Every tag notification scanned every alarm; a zero-deadband limit could alternate at exactly its setpoint. Nonnumeric input did not reset pending delay. | Group subscriptions by source tag, preserve timer-driven full evaluation, keep boundary state stable, reset pending delay on unusable input, and avoid pruning scans below retention capacity. Counting-provider tests verify unrelated/affected read counts. |
| Expression parser | Quoted parentheses, separators and operators could be treated as grammar tokens. | Grammar tests require the correct token kind. Literal punctuation and punctuation references remain values; malformed quoted operators are rejected. Existing evaluation and nesting limits are retained. |
| Property bindings | Each recursive property read scanned and sorted the component's entire binding dictionary. | Per-component exact/descendant indexes, checked once per Evaluate for designer shape changes; bounded parsed-path cache. Mutable definition contents remain live. Same-size replacements, quoted paths and qualified aliases are tested. |
| Write capability | A writable alias to a read-only property could appear editable even though committing was rejected. | Follow bounded property aliases for capability checks. Tag-provider permission/type/revision checks still apply at commit time. |
| Page/repeater refresh | The session host recursively refreshed descendants, then ViewHost recursively refreshed them again. Newly loaded children could initially lack page context. | Pass context when loading and use one steady-state traversal. The browser fixture measures actual evaluation counters through three nested embedded levels and checks identical deltas and stable binding indexes. |

## Reproduce the performance checks

```sh
dotnet run --project tests/ScadaSpace.Performance -c Release
```

The executable creates its own deterministic in-memory data and writes `TestResults/performance.json`. It compares a 32-sample history window against a full-snapshot/filter reference, cached tree projection reads against a flat projection reference over 1,200 items, and records evaluation cost for 500 bound properties. Reference results are checked for equality before measuring. The fixtures are synthetic and do not connect to process equipment.

Each measurement uses warm-up calls and five timed batches. The report records median microseconds and allocated bytes per operation, fixture sizes, runtime and operating system. Timing is observational, not a pass/fail threshold: shared CI machines do not provide stable absolute latency. CI instead requires identical reference results, bounded narrow-window allocation, stable indexes and no retained-tree-sized allocation for an idle cached projection.

The report compares the named strategies, not two complete application binaries. It does not establish a universal application speedup, rendering FPS, input latency, power use, or physical GPU acceleration. Actual browser tests remain a separate gate using Uno with Chromium/SwiftShader.

## Cache and lifecycle contracts

A `ViewInstance` remains a single-thread-affine runtime object. Call `Evaluate()` after changing its mutable designer definitions. Binding-shape changes, including same-count replacements, are detected at that boundary. Changing Source/Enabled/Transforms on an existing definition does not require rebuilding the structural index. Parsed paths are capped at 512 entries per view; the cache is cleared when that limit is reached.

No-op parameter writes retain their revision after validating an explicitly supplied expected revision. Equal page context does not invalidate the current value cache. A new input or navigation still updates the dependent values on the next evaluation. View construction may perform initialization evaluations; the one-pass assertion concerns steady-state refresh, not a claim of exactly one initialization call.

Alarm tag callbacks process only alarms belonging to that tag. Hosts must continue calling `AlarmEngine.Evaluate()` for time-based delays and shelving expiration even when no process values change. The existing event-retention rule is unchanged: unacknowledged operator actions are not silently discarded. This is not a durable or distributed alarm service.

## Review boundaries and remaining work

The review inspected the project/value/path model, tag provider, expressions/binding/writeback paths, history/alarm engines, document transactions, layout/persistence boundaries, drawing/control adapters, page/session lifecycle, packaging and browser test integration. This was a code and regression review, not exhaustive fuzzing, accessibility certification, industrial security assessment or desktop runtime qualification.

Further performance work remains in whole-project undo snapshots, chart full-series reads and rendering allocations, in-memory grid filtering/sorting, polling/telemetry serialization, and broader incremental layout/render invalidation. These areas are not claimed to be optimized by the present changes. Remote lazy tree loading, drag/reparent editing, complete tree appearance/event APIs, full scripting/Gateway behavior, and the previously excluded popup/structured-inspector enhancements remain outside this merge.

See [Tree APIs and configuration](trees.md), [validation](validation.md), and the exact-revision Actions reports for executed coverage.
