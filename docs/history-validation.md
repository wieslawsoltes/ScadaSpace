# History and numeric-input validation

See [history binding APIs and review](history-bindings.md) for the implemented behavior and remaining parity boundaries. This record describes test definitions, not a declaration of final execution success; exact-revision Actions artifacts contain the executed results.

The continuation defines 208 engine checks (37 baseline, 10 hardening and 161 parity), 15 browser-history adapter tests, eight standalone package-consumer checks and 126 actual Uno browser assertions across five suites. The history suite contributes 24 assertions and retains the large timestamp and final no-exceptions checks that exposed an intermediate WebAssembly overflow.

The reusable numeric editor is tested through actual pointer and keyboard input for canonical large-value text, synchronized dependency-property values, stepping above Int32, rejecting nonfinite input, and restoring an uncommitted draft with Escape. Existing numeric interactions in the original sample, structured views, repeaters and shared docked views remain enabled.

The table regression distinguishes absent optional column configuration from a failed column binding. Absence may infer columns; an explicit binding failure clears the affected table and displays its bad quality. The browser fixture uses a checkbox to trigger and recover a division-by-zero column expression, while another table with the same valid history query remains visible.

Performance checks compare equivalent snapshot/grouping and streaming aggregation strategies, bounded history windows, cached plot snapshots and cached document queries. They report five warmed batches and allocated bytes, gate correctness/allocation invariants and do not gate machine-dependent elapsed time. They are not physical-GPU or whole-application throughput measurements.

The proposed chart rendering/path-cache enhancement was excluded after a platform upload block. The shipped trace and viewport engines do not imply an expanded chart UI. Full Perspective parity, remote or durable historians, time-weighted Average, industrial safety qualification, native desktop runtime coverage and screen-reader certification remain outstanding.
