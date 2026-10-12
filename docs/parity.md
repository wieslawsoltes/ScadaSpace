# Compatibility matrix

This document describes implemented behavior, not a declaration of full parity. The public Ignition Perspective 8.3 manual was used as a feature reference. Original implementation, terminology, symbols and project serialization remain ScadaSpace-specific.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Designer shell | Compact vector commands, persistent project/tag/property panes, auto-hidden/pinned searchable list/tile palette, non-destructive bottom resource tabs and resizable panels | Not pixel-identical; no licensed icon pack, arbitrary dock layout or complete designer window parity; dedicated nested-property tree editor not published |
| Canvas editing | Pointer selection, additive selection, marquee, snapped move/resize, alignment, z-order, clipboard, undo/redo, reference-preserving coordinate wrapping, source-view navigation, rulers and zoom | No full nested transformed-coordinate editing, constraint solver or complete Perspective designer shortcut/menu parity |
| Project model | Validated JSON, views, pages, style classes, parameters, custom props and actions | Own schema; does not import/export Ignition project archives |
| Values and tags | Qualified scalar/object/array values, immutable nested writes, memory provider, revisions, write checks, subscriptions, local audit | No real OPC-UA/device transport, UDT inheritance, complete Ignition dataset/document semantics, remote providers or Gateway tag manager |
| Bindings | Direct/indirect tag, nested property and expression bindings, synchronous expression structures, local read-only Tag History queries, ordered expression/scale/format/map transforms, isolated property-change observers | No SQL/named-query or HTTP binding, asynchronous expression-structure coordination, full Perspective expression-function parity or Jython transform |
| Bidirectional input | Enabled untransformed tag/property bindings, nested writeback, alias traversal, instance-local input, source read-only/type/range/revision checks | No automatic transform inversion or remote transactional tag writes |
| Containers | Coordinate, weighted row/column flex, responsive columns, breakpoint and active-tab calculations | Not full CSS/flex semantics; tab selection is configured through properties, not a complete native tab-container editor |
| Reuse | Reactive embedded paths/parameters and repeater counts/instance arrays, preserved child-local state, incremental disposal, recursion limits | Index-keyed, at most 100 instances; not complete Perspective repeater/CSS/parameter-direction semantics |
| Display and input | Concrete catalog of 35 component types with shared Uno runtime/designer host | Not the complete Perspective component catalog; image/media/map/file-upload, mobile/device components and specialized charts remain |
| Tables | Typed array-of-object data, source-row identity, numeric UI sorting/filtering, revision-checked cell editing and selection; reusable multi-key queries/paging/CSV | No complete grouped grid, multi-sort/pagination UI, custom cell renderer catalog, keyboard/accessibility peers or full Perspective table event payloads |
| Hierarchical trees | Virtualized native Tree, immutable hierarchy adapter, local tag-provider browser, expansion/filtering, keyboard/multi-selection, typed selection data and automation peers | Not complete Perspective appearance/icon/overflow APIs, drag/reparent editing, lazy remote loading or remote Tag Browse Tree protocols |
| Industrial symbols | Mimic/Simple/P&ID Pump, Motor, Valve, Sensor and Vessel; variants, directions, state/quality display, annotations, capacity/fill controls, native gallery and local auto defaults; project state colors/light-dark profiles, independent effects and native state authoring; bounded retained pictures and small motion overlays | Independently drawn, not pixel-identical; no full application CSS/annotation styling, exact original artwork or complete component catalog |
| Charts | Live qualified history, time-window zoom, crosshair, time-series/bar/pie, extrema decimation | No complete Power Chart, pen management, range brush or chart annotation parity |
| Alarms | States, hysteresis, delays, acknowledgement, timed shelving, status/journal tables | No alarm pipelines, email/SMS/voice notifications, remote journal database or full Perspective alarm filtering/actions |
| History | Bounded memory storage, atomic UTC snapshots, streaming sample aggregates, cached tall document queries, quality-aware trace, CSV export | No SQL historian, store-and-forward, compression tiers, time-weighted interpolation or durable recovery |
| Events | Native three-pane staged event/action editor, ordered enabled actions, native click/double-click/context and button activation hooks, immutable dispatch plans, searchable configuration references | No Jython, asynchronous script actions, full event payload/propagation/security semantics or entire system scripting API |
| Sessions | Parameterized routes, separate query context, typed URL construction, bounded back/forward snapshots, hash deep links, shared/local docked views, push/cover/auto modes, resizing, page configuration editor, existing simple popups and isolated session launch | No shared multi-user Gateway, session authentication, full page-property writeability, same-edge multi-dock definitions, CSS/animation parity, or configurable popup-window parity |
| Graphics | Independent Skia vector drawing integrated through Uno SKCanvasElement | Backend selected by Uno; not a custom WebGPU backend; no measured physical-GPU performance claim |
| Packaging | Twelve separate engine/data/graphics/Uno library projects; NuGet pack pipeline | Packages are build artifacts, not published to nuget.org |
| Desktop/browser | Shared Uno application and platform hosts | Browser interactions are tested; a desktop compilation alone does not validate desktop runtime behavior on every OS |
| Enterprise and safety | Explicit isolated simulation boundary; input validation and local audit labels | No IdP/RBAC/security zones, Gateway network, redundancy, backup service, licensing service or industrial safety qualification |

## Verification scope

Engine suites cover the value model, tags, expressions, bindings, history, alarms, document transactions, pages/docks, trees, numeric input and workspace/configuration APIs. The browser-history adapter and standalone NuGet consumers are tested independently. Eleven browser suites exercise the actual Uno controls: the original application, structured runtime, page navigation, hierarchical trees, local history/numeric input, designer workspace, event configuration, hierarchy editing, style authoring, industrial symbols and project symbol states. Tests use real pointer/keyboard input; observations do not expose a managed command dispatcher.

Counts and pass/fail claims apply only to the exact revision in the corresponding Actions artifacts. Build success alone is not runtime evidence. See [validation](validation.md), [configuration workflows](configuration-workflows.md), and the retained source, reports and screenshots for executed scope.

## Public references

- Perspective designer interface: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-designer-interface
- Perspective component reference: https://www.docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components
- Uno SKCanvasElement: https://platform.uno/docs/articles/controls/SKCanvasElement.html
- Uno Skia rendering: https://platform.uno/docs/articles/features/using-skia-rendering.html

No proprietary implementation or graphical assets from these references are included.

See [page navigation and docked sessions](page-sessions.md) and [hierarchical trees](trees.md) for shipped behavior and explicit boundaries.

See [local history bindings and review](history-bindings.md) for the added query APIs, streaming aggregation, table error recovery and the excluded chart-renderer work.

See [designer workspace and UI review](designer-workspace.md) for the independently implemented layout, tab/pane behavior, control reuse, keyboard fixes and remaining visual boundaries.

See [designer hierarchy editing and performance review](hierarchy-editing.md) for wrapping, evaluated source navigation, rollback recovery and indexed layout/lookup APIs.

See [industrial symbols](industrial-symbols.md) for the independent family drawings, native control integration, quality/animation behavior and explicit remaining graphics boundaries.

See [project symbol states](symbol-states.md) for native state authoring, theme materials, retained flashing, package APIs and explicit compatibility limits.
