# Architecture and reuse

## Dependency direction

`Core` has no presentation dependency. `Tags` and `Expressions` depend on Core. History and Alarms consume the tag-provider contract. Runtime composes those engines and owns view instances. Designer, Layout and Persistence operate on Core models. Graphics depends on Skia and history data, not Uno. Controls adapts the engines and graphics to Uno; App contains application menus, browser storage and window policy.

No reusable library reaches into `ScadaSpace.App`. Symbols and charts accept plain data, and a model can be tested without launching the UI.

## Qualified data flow

A tag write is validated for path, type, engineering limits, writability and optional expected revision. A successful write creates an immutable `QualifiedValue` containing the scalar, quality, UTC timestamp and monotonic provider revision. Subscriber notifications are queued in commit order and dispatched outside the provider lock. Reentrant writes enqueue rather than recursively notifying other listeners out of order. Subscriber exceptions do not roll back committed values.

A view instance owns its parameter dictionary and evaluated property cache. Evaluation resolves references recursively, detects cycles and enforces a depth limit. Direct, indirect, property and expression bindings feed an ordered transform chain. Bad dependencies retain their quality, and a composite expression records the oldest contributing timestamp. Runtime edits without a binding remain instance-local; they do not alter the saved view definition.

The memory provider is synchronous. Under concurrent/reentrant publication, a write can return after its value is committed but before another thread has drained its notification. A disposed subscription prevents future capture but cannot retract a callback already queued by a committed write.

## Expressions

Expressions are parsed into a bounded AST; they never invoke arbitrary managed code, JavaScript `eval`, reflection, disk or network I/O. Arithmetic, comparisons, lazy booleans, ternaries, `if`, `coalesce`, common numeric functions and string functions are supported. The source cap is 16 KiB, parser depth 64, dependency count 256, node count 1024 and evaluation budget 4096 node visits. This is a documented ScadaSpace language, not the entire Ignition expression/Jython surface.

## History and alarms

Each enabled tag has an independent bounded history ring. UTC queries retain qualified samples. Aggregation currently provides **sample-weighted**, not time-weighted, average/minimum/maximum/count/last results. Decimation groups by time and preserves extrema and quality boundaries. Quality transitions are allowed to exceed the nominal point budget so an outage is not rendered as a continuous good signal.

Alarm events retain active/cleared and acknowledged/unacknowledged state. Hysteresis and delays are evaluated explicitly; bad quality cannot silently clear an alarm. Shelving is a timed display suppression, not acknowledgement. The journal is bounded. Cleared acknowledged events can be pruned, but pending unacknowledged operator actions are not silently evicted; applications with long-running deployments need an external retention strategy.

## Document transactions

`DesignerDocument.Execute` snapshots, mutates and validates the complete project before committing history. Failed edits restore both project and selection. Dirty-state checks use state IDs rather than serializing the project on every frame. Undo history is capped by count and a 64 MiB budget, while retaining at least the most recent operation. An observer failure cannot roll back a committed model while leaving its undo history in a different state.

Position alignment is restricted to siblings in a coordinate container. Flow-managed positions are not treated as editable absolute coordinates. This avoids appearing to commit movements that layout would immediately overwrite.

## Rendering

`SymbolRenderer.Draw` and `ChartRenderer.Draw` accept a caller's `SKCanvas` and bounds. Paint and font objects are reused within a draw operation and deterministically disposed. The Uno `HmiSymbol` and `HmiChart` adapters use `SKCanvasElement.RenderOverride`, allowing Uno to compose these primitives into its existing Skia surface. Native Uno controls remain responsible for text editing, focus, keyboard handling, toggles and dropdowns.

`HmiDataTable` draws only visible rows into the canvas and preserves selection across refreshes. Its current text filtering/sorting scans in-memory rows; it is not a server-backed million-row database grid. It needs further accessibility work because custom-drawn rows are not individual native accessibility peers.

## Reuse examples

```csharp
using ScadaSpace.Core;
using ScadaSpace.Tags;
using ScadaSpace.Runtime;

var tags = new TagStore(new[] {
    new TagDefinition {
        Path = "[memory]Tank/Setpoint",
        DataType = ScalarKind.Number,
        InitialValue = Scalar.From(50),
        Minimum = 0, Maximum = 100, Writable = true
    }
});
using var subscription = tags.Subscribe(change =>
    Console.WriteLine($"{change.Path}: {change.Current.Value}"));
var current = tags.Read("[memory]Tank/Setpoint");
var result = tags.Write("[memory]Tank/Setpoint", Scalar.From(65),
    "application-user", current.Revision);
if (!result.Success) throw new InvalidOperationException(result.Message);
```

```csharp
// In any Uno application referencing ScadaSpace.Controls:
using var runtime = new ProjectRuntime(DemoProject.Create());
var host = new ScadaSpace.Controls.ViewHost();
host.Load(runtime, "Overview", editing: false);
// Add host to the application's visual tree. Call host.Refresh() when its
// input state changes, then dispose the host and runtime when no longer used.
```

```csharp
// Without Uno, using any valid Skia drawing surface:
ScadaSpace.Graphics.SymbolRenderer.Draw(canvas,
    SkiaSharp.SKRect.Create(0, 0, 200, 240),
    ScadaSpace.Graphics.SymbolKind.Tank,
    new ScadaSpace.Graphics.SymbolData(Value: 68.4, Unit: "%"));
```

## Storage boundaries

The file repository provides optimistic revisions and atomic replacement inside one process, using one repository instance per path. It does not implement multi-process locking. The browser demonstration instead uses localStorage for project definitions. Tag values, history, alarm state and local audit history are ephemeral, independent per browser process and deliberately not represented as a distributed Gateway.
