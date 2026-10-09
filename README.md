# ScadaSpace

An independent industrial visualization workbench built with **C#, Uno Platform and Skia**, targeting desktop and WebAssembly. Its engines, graphics and controls are separated into **eleven reusable libraries**.

> **Scope:** this is an implemented simulation workbench, not full Ignition Perspective parity, a drop-in replacement, or a production-qualified industrial-control system. The demonstration never connects to live equipment. See the [compatibility matrix](docs/parity.md) for explicit boundaries.

[Browser application](https://wieslawsoltes.github.io/ScadaSpace/) · [Build and browser tests](https://github.com/wieslawsoltes/ScadaSpace/actions) · [Architecture](docs/architecture.md) · [Validation](docs/validation.md)

## Workbench

The application includes a project/view tree, live tag browser, searchable component palette, property and binding editor, resizable side panes, view tabs, rulers, zoom, grid, selection, multi-selection, marquee, snapped movement, resizing, alignment, z-order, copy/paste and transactional undo/redo. **Preview** switches the same view into an interactive runtime; design input cannot accidentally write process tags.

The included water-treatment project has five views, eight typed simulated tags, live history, three configured alarms, tanks, pumps, valves, gauges, charts and editable setpoints. **Pause simulation** freezes the simulated process without stopping the interface. **Save** stores the project locally; **Export** downloads portable project JSON. Runtime values and in-memory history are not a durable project database.

## Reusable libraries

| Library | Responsibility |
|---|---|
| `ScadaSpace.Core` | Project schema, components, tagged scalar values, qualified values, diagnostics and audit records |
| `ScadaSpace.Tags` | Thread-safe memory provider, typed/ranged writes, optimistic revision checks, ordered subscriptions |
| `ScadaSpace.Expressions` | Bounded expression compiler/interpreter, dependency discovery and lazy evaluation |
| `ScadaSpace.History` | Bounded per-tag rings, time queries, aggregates, quality-preserving decimation and CSV |
| `ScadaSpace.Alarms` | Alarm states, hysteresis, on/off delays, acknowledgement, shelving and journal |
| `ScadaSpace.Runtime` | Per-instance bindings, parameters, transforms, navigation and declarative actions |
| `ScadaSpace.Designer` | Validated document transactions, bounded undo/redo, selection and editing commands |
| `ScadaSpace.Layout` | Coordinate, flex, responsive columns, breakpoint and tab layout calculations |
| `ScadaSpace.Persistence` | Revision-checked, atomic, single-process file project repository |
| `ScadaSpace.Graphics` | Skia vector symbols and time-series/bar/pie chart rendering, independent of Uno |
| `ScadaSpace.Controls` | Reusable Uno symbols, charts, virtualized tables, view host, designer surface and inspectors |

The core engines require no Uno UI. The graphics package can draw into a caller-provided `SKCanvas`. The Uno controls draw custom graphics through `SKCanvasElement`, sharing Uno's Skia composition surface rather than repeatedly copying rasterized screenshots into image controls.

## Components

The catalog contains 31 concrete component types: Label, LEDDisplay, Tank, Thermometer, Progress, Gauge, TrendChart, BarChart, PieChart, Button, ToggleSwitch, CheckBox, NumericEntry, TextField, Slider, Dropdown, RadioGroup, DateTimeInput, Table, AlarmStatusTable, AlarmJournalTable, Rectangle, Ellipse, Line, Pipe, Pump, Valve, Motor, Container, EmbeddedView and FlexRepeater. These names describe ScadaSpace's own API, not binary-compatible Perspective classes.

## Build and run

Install a .NET 10 SDK. `global.json` pins the Uno SDK; project files pin SkiaSharp. No commercial Ignition installation or license is required.

```sh
# Engine regressions: no UI workloads required.
dotnet run --project tests/ScadaSpace.Tests -c Release
dotnet run --project tests/ScadaSpace.Hardening.Tests -c Release

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

Click the circle beside a property to configure a direct/indirect tag, property or expression binding. Bidirectional writes require an enabled direct tag binding with no transform. Transform chains support expressions, linear scale, numeric format and lookup maps. Binding errors retain bad/stale quality instead of presenting a failed computation as a valid process value.

## Boundaries and security

There is no real device driver, OPC-UA transport, Ignition Gateway protocol, Jython engine, SQL named-query service, enterprise identity provider, redundancy or safety certification. Local simulated actors are audit labels, **not authenticated identities**. Browser storage is readable by scripts on the same origin. Never put credentials or sensitive plant information in the demonstration. Separate browser sessions currently own independent simulation state.

Custom drawing uses Uno's selected Skia backend. A successful WebGL context does not prove physical GPU acceleration: CI intentionally uses SwiftShader and reports this. Browser AOT and native platform runtime behavior require additional validation; a desktop compilation is not a desktop runtime test.

Ignition and Perspective are trademarks of Inductive Automation. This project is unaffiliated. Its source and vector artwork are independently written; proprietary source, logos and icon packs are not redistributed. The UI follows a familiar industrial-designer structure but is not claimed to be pixel-identical.

## License

MIT for ScadaSpace source. Referenced packages retain their respective licenses.
