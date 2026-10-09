# Compatibility matrix

This document describes implemented behavior, not a declaration of full parity. The public Ignition Perspective 8.3 manual was used as a feature reference. Original implementation, terminology, symbols and project serialization remain ScadaSpace-specific.

| Area | Implemented | Remaining boundary |
|---|---|---|
| Designer shell | Menus, toolbars, project/tag panes, palette, inspector, view tabs, resizable panels | Not pixel-identical; no licensed icon pack, arbitrary dock layout or complete designer window parity |
| Canvas editing | Pointer selection, additive selection, marquee, snapped move/resize, alignment, z-order, clipboard, undo/redo, rulers and zoom | No full nested transformed-coordinate editing, constraint solver or complete Perspective designer shortcut/menu parity |
| Project model | Validated JSON, views, pages, style classes, parameters, custom props and actions | Own schema; does not import/export Ignition project archives |
| Values and tags | Qualified scalar values, memory provider, revisions, write checks, subscriptions, local audit | No real OPC-UA/device transport, UDT inheritance, full datasets/documents, remote providers or Gateway tag manager |
| Bindings | Direct/indirect tag, property, expression; ordered expression/scale/format/map transforms | No SQL/named-query binding, full object/array model, Perspective expression-function parity or Jython transform |
| Bidirectional input | Explicit enabled tag bindings without transforms; type/range/revision checks | No automatic transform inversion or remote transactional tag writes |
| Containers | Coordinate, weighted row/column flex, responsive columns, breakpoint and active-tab calculations | Not full CSS/flex semantics; tab selection is configured through properties, not a complete native tab-container editor |
| Reuse | Instance parameters, embedded views, repeated view instances, recursion limits | Embedded view paths/parameters and repeater shape are configured at construction; complete reactive Perspective repeater/object binding is not present |
| Display and input | Concrete catalog of 31 component types with shared Uno runtime/designer host | Not the complete Perspective component catalog; image/media/map/tree/file-upload, advanced table editing, mobile/device components and specialized charts remain |
| Charts | Live qualified history, time-window zoom, crosshair, time-series/bar/pie, extrema decimation | No complete Power Chart, pen management, range brush or chart annotation parity |
| Alarms | States, hysteresis, delays, acknowledgement, timed shelving, status/journal tables | No alarm pipelines, email/SMS/voice notifications, remote journal database or full Perspective alarm filtering/actions |
| History | Bounded memory storage, UTC queries, sample aggregates, CSV export | No SQL historian, store-and-forward, compression tiers, time-weighted interpolation or durable recovery |
| Events | Declarative write/toggle/navigate/popup/message actions | No Jython or arbitrary scripting, no entire system scripting API |
| Sessions | Local page navigation, back stack, popups and isolated browser session launch | No shared multi-user Gateway, live server push, full docked views, URL route parameters or session authentication |
| Graphics | Independent Skia vector drawing integrated through Uno SKCanvasElement | Backend selected by Uno; not a custom WebGPU backend; no measured physical-GPU performance claim |
| Packaging | Separate engine, graphics and Uno library projects; NuGet pack pipeline | Packages are build artifacts, not published to nuget.org |
| Desktop/browser | Shared Uno application and platform hosts | Browser interactions are tested; a desktop compilation alone does not validate desktop runtime behavior on every OS |
| Enterprise and safety | Explicit isolated simulation boundary; input validation and local audit labels | No IdP/RBAC/security zones, Gateway network, redundancy, backup service, licensing service or industrial safety qualification |

## Public references

- Perspective designer interface: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-designer-interface
- Perspective component reference: https://www.docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components
- Uno SKCanvasElement: https://platform.uno/docs/articles/controls/SKCanvasElement.html
- Uno Skia rendering: https://platform.uno/docs/articles/features/using-skia-rendering.html

No proprietary implementation or graphical assets from these references are included.
