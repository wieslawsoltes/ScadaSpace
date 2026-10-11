# Local Tag History bindings and historian review

ScadaSpace now exposes its existing local in-memory historian through a read-only `TagHistory` binding. The implementation is independent of Ignition's project format and Gateway. It does not connect to SQL, remote providers or real process equipment.

## Configure a binding

Select `TagHistory` in the existing binding-type selector and put a native JSON query in Source. The generic source editor is used; there is no specialized history-query wizard in this iteration. For a Table, set `props.source` to `data` and bind `props.data`.

```json
{
  "path": "[default]Plant/Tank01/Level",
  "windowSeconds": 120,
  "aggregate": "SampleAverage",
  "returnSize": 40
}
```

The path accepts the existing bounded view-parameter substitution, for example `[default]Plant/{equipment}/Level`. A fixed interval uses `start` and `end` instead:

```json
{
  "path": "[default]Plant/Tank01/Level",
  "start": "{root.custom.startMillis}",
  "end": "{root.custom.endMillis}",
  "aggregate": "Raw",
  "limit": 1000
}
```

Numeric times are integral Unix milliseconds. Strings can be numeric ScadaSpace expressions or round-trip `O` format ISO timestamps. Numeric options can also be expressions. Query expressions use the normal bounded expression resolver, including its quality and cycle checks. Unknown fields, duplicate JSON members, unsupported modes, invalid ranges, and conflicting `start`/`windowSeconds` fields are rejected.

Without `end`, the query ends at the latest retained sample, **not wall-clock now**. Without `start`, it covers `windowSeconds` (default 120; 0.001–86,400). This makes idle queries stable but does not fabricate samples for a stale source. Raw results retain stored quality and values; the successful binding's Good quality means the query succeeded, not that every measurement is good.

Each result is a tall array of objects with `timestamp`, `path`, `value`, `quality` and `count` fields. `quality` is an explicit quality-name string. Raw `count` is one; aggregated `count` is the number of good source samples. Supported aggregate modes are Raw, SampleAverage, Minimum, Maximum, Count and Last. **SampleAverage is sample-weighted**, unlike Perspective's time-weighted Average. The name `Average` is rejected rather than implying that compatibility.

Raw `limit` defaults to 1,000 and accepts 1–1,500 rows. An oversized window fails before copying its samples; it is not silently truncated. Aggregated `returnSize` defaults to 100 and accepts 1–1,500 buckets. Core structured-value node/depth limits still apply, particularly for compound values. Query results and aliases to them cannot be written back.

## Reuse and consistency

`HistoryQueryCache.Get(HistoryQueryRequest)` is independent of Uno. It returns an immutable `HistoryQueryResult` shared for an unchanged request and history stamp. The default cache holds at most 32 requests and is synchronized for concurrent callers. `ProjectRuntime.HistoryQueries` shares it among views in that runtime; independent runtime sessions do not share process data. `ViewInstance.WithHistory` attaches a historian to a separately constructed view and checks that an explicitly supplied cache belongs to it.

`Historian.ReadWindow` and `ReadAggregateWindow` capture their stamp and result under the same lock. A stamp contains retained count, latest UTC ticks and latest revision. A cache hit is valid at the metadata-read instant. It does not promise a transaction spanning multiple tags or later calls. Snapshots cannot be modified through their read-only collections.

The separate `HistoryWindowCache` holds one current plot window per path, with bounded capacity and optional extrema decimation. It retains original samples separately for trace queries. `HistoryTrace.At` performs a binary-search trace, uses the final revision for an exact timestamp tie, interpolates only between good numeric samples, and neither extrapolates nor crosses bad-quality intervals. `TrendViewport` supplies reusable UTC live/hold/pan/anchored-zoom/range-selection state with bounded time spans. These APIs are **not wired to an expanded chart UI in this PR**.

## Correctness and performance changes

Aggregation now scans only the requested ordered-ring interval into per-bucket accumulators. It no longer first copies the full query or allocates per-sample bucket lists. Stable sample-mean accumulation avoids overflow for large finite values, and a real good-null Last sample is distinguished from an empty interval. Exact integer tick boundaries define bucket membership; interior intervals are half-open and the requested final endpoint belongs to the last bucket. Very short ranges can contain empty zero-width buckets.

Tables no longer leave stale rows visible after a data binding or column configuration fails. They clear those rows, block interaction with failed results, display a native diagnostic, and rebuild when valid input returns. Invalid column type names are diagnosed instead of silently inferred. This is failure presentation, not a security or industrial safety qualification.

The performance executable compares reference-equivalent streaming aggregation and indexed history queries, records cached plot/document lookup allocations, and retains the previous tree/binding scenarios. Five warmed batches produce median timings and allocated bytes. CI checks result equivalence and allocation invariants rather than machine-dependent timing thresholds. This does not establish whole-app FPS, physical GPU throughput, or a universal speedup.

## Validation and exclusions

Engine suites cover atomic snapshots, timestamp ties, cache invalidation and limits, concurrent readers, trace quality gaps, viewport limits, finite extreme means, null quality, exact buckets, dynamic binding inputs and read-only behavior. `tests/browser/history.py` imports a project through the file picker and uses native controls to append history, change query limits/ranges, introduce column errors, recover, sort and export. The standard smoke, structured-binding, page and tree browser suites remain enabled. Standalone NuGet checks exercise the query and binding APIs without project references.

A proposed chart renderer/native path-cache upload was blocked and is **not included**. Existing HmiChart rendering and interaction remain unchanged; full-series chart refresh and render allocation work remain open. Earlier excluded popup and nested-property inspector enhancements also remain absent. Multi-tag wide output, periodic sampling/interpolation, full calculation modes, remote/SQL/durable historians, Gateway scripting, complete Power Chart/Perspective parity, desktop runtime and screen-reader qualification are not provided by these changes.

Public behavior references: [Perspective Tag History bindings](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/working-with-perspective-components/bindings-in-perspective/tag-history-bindings-in-perspective) and [Perspective Time Series Chart](https://www.docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components/perspective-chart-palette/perspective-time-series-chart). These are feature references, not included proprietary implementations.
