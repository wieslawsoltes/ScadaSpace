# ScadaSpace

An independent industrial visualization workbench built with **C#, Uno Platform and Skia**, targeting desktop and WebAssembly. Its engines, graphics and controls are separated into **twelve reusable libraries**.

> **Scope:** this is an implemented simulation workbench, not full Ignition Perspective parity, a drop-in replacement, or a production-qualified industrial-control system. The demonstration never connects to live equipment. See the [compatibility matrix](docs/parity.md) for explicit boundaries.

[Browser application](https://wieslawsoltes.github.io/ScadaSpace/) · [Build and browser tests](https://github.com/wieslawsoltes/ScadaSpace/actions) · [Architecture](docs/architecture.md) · [Validation](docs/validation.md)

## Workbench

The application includes a project/view tree, live tag browser, searchable component palette, property and binding editor, resizable side panes, view tabs, rulers, zoom, grid, selection, multi-selection, marquee, snapped movement, resizing, alignment, z-order, copy/paste and transactional undo/redo. **Preview** switches the same view into an interactive runtime; design input cannot accidentally write process tags.

The included water-treatment project has five views, eight typed simulated tags, live history, three configured alarms, tanks, pumps, valves, gauges, charts and editable setpoints. **Pause simulation** freezes the simulated process without stopping the interface. **Save** stores the project locally; **Export** downloads portable project JSON. Runtime values and in-memory history are not a durable project database.

## Reusable libraries

| Library | Responsibility |
|---|---|
| `ScadaSpace.Core` | Project schema, components, immutable scalar/object/array values, qualified values, diagnostics and audit records |
| `ScadaSpace.Data` | Typed document tables and trees, stable identities, queries, immutable edits and CSV |
| `ScadaSpace.Tags` | Thread-safe memory provider, typed/ranged writes, optimistic revision checks, ordered subscriptions |
| `ScadaSpace.Expressions` | Bounded expression compiler/interpreter, dependency discovery and lazy evaluation |
| `ScadaSpace.History` | Bounded per-tag rings, time queries, aggregates, quality-preserving decimation and CSV |
| `ScadaSpace.Alarms` | Alarm states, hysteresis, on/off delays, acknowledgement, shelving and journal |
| `ScadaSpace.Runtime` | Per-instance bindings, parameters, transforms, navigation and declarative actions |
| `ScadaSpace.Designer` | Validated document transactions, bounded undo/redo, selection and editing commands |
| `ScadaSpace.Layout` | Coordinate, flex, responsive columns, breakpoint and tab layout calculations |
| `ScadaSpace.Persistence` | Revision-checked, atomic, single-process file project repository |
| `ScadaSpace.Graphics` | Skia vector symbols and time-series/bar/pie chart rendering, independent of Uno |
| `ScadaSpace.Controls` | Reusable Uno symbols, charts, virtualized tables/trees, view host, designer surface and inspectors |

The core engines require no Uno UI. The graphics package can draw into a caller-provided `SKCanvas`. The Uno controls draw custom graphics through `SKCanvasElement`, sharing Uno's Skia composition surface rather than repeatedly copying rasterized screenshots into image controls.

## Components

The catalog contains 33 concrete component types: Label, LEDDisplay, Tank, Thermometer, Progress, Gauge, TrendChart, BarChart, PieChart, Button, ToggleSwitch, CheckBox, NumericEntry, TextField, Slider, Dropdown, RadioGroup, DateTimeInput, Table, Tree, TagBrowseTree, AlarmStatusTable, AlarmJournalTable, Rectangle, Ellipse, Line, Pipe, Pump, Valve, Motor, Container, EmbeddedView and FlexRepeater. These names describe ScadaSpace's own API, not binary-compatible Perspective classes.

## Build and run

Install a .NET 10 SDK. `global.json` pins the Uno SDK; project files pin SkiaSharp. No commercial Ignition installation or license is required.

```sh
# Engine regressions: no UI workloads required.
dotnet run --project tests/ScadaSpace.Tests -c Release
dotnet run --project tests/ScadaSpace.Hardening.Tests -c Release
dotnet run --project tests/ScadaSpace.Parity.Tests -c Release

# Desktop: the host selects the operating-system backend.
dotnet run --project src/ScadaSpace.App -f net10.0-desktop

# Browser release, served under /ScadaSpace/.
dotnet workload install wasm-tools
dotnet publish src/ScadaSpace.App -f net10.0-browserwasm -c Release -o artifacts/browser
mkdir -p artifacts/site
ln -s "$PWD/artifacts/browser/wwwroot" artifacts/site/ScadaSpace
python -m http.server 8765 --directory artifacts/site
```

Open `http://localhost:8765/ScadaSpace/`. To host at a different path, override `WasmShellWebAppBasePath` at publish time. GitHub Actions builds both targets, executes the engine suites and drives the real browser using Playwright. It retains source, NuGet packages, browser output, screenshots and test reports as downloadable artifacts. Deployment to Pages is gated on successful core, desktop-build and browser jobs on `main`.

## Keyboard and editing

`F5` toggles preview; `Ctrl+S` saves; `Ctrl+Z/Y` undo/redo; `Ctrl+C/X/V/D` copy/cut/paste/duplicate. Delete removes selected components. Arrow keys nudge, with Shift using eight-unit steps. Ctrl/Shift-click extends selection. The lower-right selection handle resizes. Dragging a palette entry inserts it at the drop location.

Click the circle beside a property to configure a direct/indirect tag, property or expression binding. Structured values and expression structures are also supported by the model/runtime API; the dedicated nested-property tree editor is not shipped. Bidirectional writes support enabled tag or property bindings without transforms, including nested paths and property aliases. Writes retain provider or instance revision checks. Transform chains support expressions, linear scale, numeric format and lookup maps. Binding errors retain bad/stale quality instead of presenting a failed computation as a valid process value.

## Structured data and view reuse

Object and array properties are immutable, bounded and structurally comparable. Nested bindings can read and write fields such as `root.custom.model.rows[0].value`. Runtime edits are isolated per view and do not alter exported project defaults. `ExpressionStructure` evaluates JSON-template leaves into an object/array; this is ScadaSpace syntax, not an Ignition archive format.

Data tables support numeric sorting, filtering, source-row identity, and validated double-click cell editing with revision checks. Embedded views and repeaters react to parameter, path and instance-count changes without recreating unchanged children. See [structured runtime and table APIs](docs/structured-runtime.md) for configuration and limits.

## Page navigation and docked sessions

The **Pages** editor configures routes such as `/equipment/:equipment`, primary views, titles, and shared/local docked views as one undoable transaction. **Session** launches the runtime with browser Back/Forward, a page address bar, and hash-based deep links suitable for GitHub Pages. Docked views support push/cover/automatic behavior, breakpoint switching, toggle handles, resizing, and page-local modality while preserving unchanged shared-view state.

`RuntimeSession.NavigatePage` expands configured routes using typed parameters and separate query fields; `PageAddress.WithQuery` builds immutable, encoded addresses. Unicode URL limits are checked after encoding so accepted pages can be reloaded. See [page/session APIs, configuration and limits](docs/page-sessions.md).

## Hierarchical Tree controls

The **Tree** and **Tag Browse Tree (local)** palette entries use a reusable virtualized Uno control. They support hierarchical expansion, filtered ancestors, keyboard/multi-selection, typed contextual data, bound selection, and stable optional item IDs. Local tag browsing groups the existing memory provider by provider and folder; it does not connect to a Gateway or discover devices. See [Tree APIs and configuration](docs/trees.md) for behavior and limits.

## Designer workspace

The workspace uses compact vector toolbar commands, bottom resource tabs, a full-height Property Editor and an auto-hidden/pinnable Components palette. Pane visibility, widths and palette display mode are saved locally; closing resource tabs retains all project edits. Searchable Project Browser nodes, palette buttons and unchanged tabs are reused. See [workspace behavior, reusable APIs and review](docs/designer-workspace.md).

## Event configuration and references

**Component → Configure Events…** and the canvas context menu open a native event/action/settings editor. Add, reorder, duplicate, disable and validate existing local actions; Apply/OK/Cancel work with transactional undo/redo. **Configuration Explorer** searches bindings, transforms, events, actions and embedded-view references and selects their owning component. Generation-indexed search results and retained control maps avoid rebuilding unchanged inventories. See [configuration workflows, runtime hardening and boundaries](docs/configuration-workflows.md).

## Local Tag History bindings

`TagHistory` bindings expose the in-memory historian as read-only typed table data, with fixed or latest-sample-relative windows, expression-driven time/row options, explicit sample aggregates and shared bounded query caching. Failed data/column configurations clear old table rows and show a recoverable diagnostic. Reusable atomic history snapshots, quality-aware trace and viewport state APIs are also available; the existing chart UI is unchanged. See [history binding configuration and review](docs/history-bindings.md) for examples, tests and limits.

## Boundaries and security

There is no real device driver, OPC-UA transport, Ignition Gateway protocol, Jython engine, SQL named-query service, enterprise identity provider, redundancy or safety certification. Local simulated actors are audit labels, **not authenticated identities**. Browser storage is readable by scripts on the same origin. Never put credentials or sensitive plant information in the demonstration. Separate browser sessions currently own independent simulation state.

Custom drawing uses Uno's selected Skia backend. A successful WebGL context does not prove physical GPU acceleration: CI intentionally uses SwiftShader and reports this. Browser AOT and native platform runtime behavior require additional validation; a desktop compilation is not a desktop runtime test.

Ignition and Perspective are trademarks of Inductive Automation. This project is unaffiliated. Its source and vector artwork are independently written; proprietary source, logos and icon packs are not redistributed. The UI follows a familiar industrial-designer structure but is not claimed to be pixel-identical.

## License

MIT for ScadaSpace source. Referenced packages retain their respective licenses.

## Style classes

**Project → Style Classes…** and Project Browser style resources open a native editor with grouped properties, typed options, a cancellable alpha-aware color picker, class preview, assignments and reference-aware rename/delete. Apply, OK and Cancel stage changes through the normal undo/redo transaction. Class values resolve alphabetically; inline values win. See [style APIs, editor behavior and validation boundaries](docs/style-classes.md).
