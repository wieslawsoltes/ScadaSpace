# Structured runtime, typed tables and reuse

These are ScadaSpace APIs and serialization conventions. They implement specific Perspective-style behaviors, not the complete Ignition property system or project format.

## Immutable values and paths

```csharp
using ScadaSpace.Core;

var original = Scalar.FromJson("""
    {"rows":[{"name":"Pump A","value":12}],"settings":{"unit":"bar"}}
    """);
var changed = PropertyPath.Parse("rows[0].value").Set(original, Scalar.From(25));
// original still contains 12; changed contains 25.
var nativeJson = changed.ToJson();
```

Arrays preserve order; object equality ignores insertion order. Factories copy incoming collections and expose read-only views. Nested updates preserve untouched sibling identities. Values are bounded to 10,000 nodes and 24 levels; parsed native JSON also has an 8 MiB input limit. Paths allow dot names, non-negative array indices and JSON-quoted keys, for example `rows[0]["pressure.bar"]`. Intermediate members must already exist; writes do not silently create array holes. These limits are not a global project-memory budget.

Project JSON retains tagged values (`kind`, `number`, `boolean`, `text`) and adds `items` or `fields` for arrays/objects. Native JSON accepted by `Scalar.FromJson` is a separate convenience format.

## Instance properties and bindings

A `ViewInstance` owns local runtime values and parameters. Input uses `Write(node, path, value, actor, expectedRevision)`; a successful write does not mutate `ComponentNode.Props` or `Custom` in the project definition. The actor is an audit label, not an authenticated identity. Call `Evaluate()` after source changes, then read qualified values with `Read`.

References support `this`, `parent`, `root`, component ID or unambiguous component name, with `props`, `custom` or `style` scopes. View parameters use `view.params`. Bindings may target a leaf inside an object/array; reading the container assembles those descendant results. Read-only, disabled and bad-quality sources cannot be made writable by a property alias. Bidirectional bindings must be untransformed tag or property bindings; transformations are not automatically inverted.

```csharp
entry.Bindings["value"] = new BindingDefinition {
    Kind = BindingKind.Property,
    Source = "root.custom.model.rows[0].value",
    Bidirectional = true
};
```

`PropertyChanged` reports changed values/quality after evaluation. Observer failures are diagnosed separately from binding computation. It is not a Jython property-change script engine or a remote notification transport.

An expression structure uses native JSON as its `Source`. String leaves are expressions; other leaves are constants:

```json
{"pressure":"{[default]Plant/Pressure}","status":"'ready'","limits":[0,100]}
```

Each evaluation is synchronous and all-or-error. This does not implement asynchronous `Wait On All`, named queries, HTTP polling or Ignition's persisted expression-structure schema.

## Typed document tables

`ScadaSpace.Data.DocumentTable` adapts an array of objects. It provides stable source-row indices, optional unique string/number keys, typed comparisons, stable multi-key sorting, filtering, paging and immutable cell edits. A cell object containing `value` is treated as a wrapped cell; its other metadata survives edits. CSV output quotes fields and neutralizes formula-like text by default.

For a runtime `Table`, configure these properties:

```json
{
  "source":"data",
  "data":[{"id":"p1","name":"Pump A","pressure":12.5}],
  "rowKey":"id",
  "editable":true,
  "columns":[
    {"field":"name","title":"Equipment","weight":2,"editable":false},
    {"field":"pressure","title":"Pressure","type":"Number"}
  ]
}
```

The example shows native values; convert them to tagged `Scalar` values when constructing project JSON. The UI supports single-column header sorting, filtering and double-click cell editing. Core multi-key queries/paging do not imply a complete multi-sort/pagination UI. Selection exposes `props.selection` containing source `row`, `key` and `data`. An edit captures the original data revision and rejects stale commits. Runtime edits remain local unless data has an explicitly writable binding. Legacy data encoded as a JSON string remains readable.

The data adapter has a 256-column limit. Filtering and sorting are in-memory operations; drawing is virtualized to visible rows, but this is not a server-backed database grid. Grouping, arbitrary cell renderers, full keyboard/accessibility peers, advanced selection and the complete Perspective table event payloads remain outstanding.

## Reactive embedded views and repeaters

`EmbeddedView` reads `view` and a `params` object. Legacy `param.name` values remain supported. `FlexRepeater` also accepts `count`, or an `instances` array of parameter objects. Each repeated instance receives its zero-based `index`.

Refresh applies only externally changed inputs. Unchanged children retain their `ViewInstance` and local state; new children receive fresh defaults. Removed children are disposed. A changed view path replaces affected children. Omitted parameters revert to view defaults where available. Recursion is rejected, nesting is bounded, and a repeater supports 0–100 instances.

Instances are keyed by index, not arbitrary application keys. Layout is a horizontal sequence of uniformly fitted views, not full CSS flex/repeater parity. Reordering, rich item layout, complete parameter direction metadata and distributed session state require further implementation.

## Validation and editor boundary

`tests/ScadaSpace.Parity.Tests` covers 16 structured-value, 17 binding and 8 table scenarios. `tests/browser/parity.py` imports a real project through the file picker and exercises inputs, expression structures, table sorting/editing, repeater resizing and local-state isolation through pointer and keyboard input. Read-only telemetry observes outcomes and actual rendered bounds.

The dedicated nested-property tree/JSON sub-editor was not published in this iteration because its upload was blocked. The original inspector remains; project JSON editing and the reusable runtime APIs provide the shipped configuration path. It must not be presented as a completed structured inspector UI.
