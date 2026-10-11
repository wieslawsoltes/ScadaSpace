# Hierarchical Tree controls

ScadaSpace provides a reusable `HmiTree` control, a `Tree` component and a `TagBrowseTree` component for the existing in-process tag provider. These are independent C#/Uno implementations, not Ignition Gateway or Jython interfaces.

## Data and configuration

The Tree's `items` property is an array of objects. This example uses native JSON; project files store values through ScadaSpace's tagged `Scalar` representation.

```json
[
  {
    "id": "line-a",
    "label": "Line A",
    "expanded": true,
    "items": [
      {"id": "pump-a", "label": "Pump A", "data": {"equipment": "Pump01", "pressure": 12.5}},
      {"id": "tank-a", "label": "Tank A", "data": "Tank01"}
    ]
  }
]
```

`label` is text, `expanded` supplies the initial branch state, `items` contains children, and `data` is an arbitrary immutable scalar/object/array. Optional unique string `id` values preserve local identity when the source is reordered; without IDs, identity is the current index path. Duplicate labels are valid. The optional `icon` is a short textual glyph, not an Ignition icon-library URL.

The runtime supports `selection` (a slash-delimited index path such as `0/1`), `selectedPaths` (an array of paths), `selectionData` (contextual output), `filter`, `showFilter`, `interactable`, `multiSelect`, and `rowHeight` (20–96 device-independent units). In multi-select mode Ctrl-click toggles and Shift-click extends native list selection. Left/Right navigate the hierarchy; the native list handles vertical navigation, Home/End and scrolling. Filtering shows matching labels and their ancestors without modifying expansion defaults.

`selectionData` contains objects with a numeric-array `itemPath` and the original contextual `value`. `onItemClicked` executes configured declarative actions. Its C# `ItemInvoked` event carries the item; the component also exposes `lastEvent` containing `itemPath`, `label` and `data`. This does not implement Python event scripts.

Items and selection support the existing property/tag/expression binding machinery. User selection writes retain optimistic revision checks; read-only selection sources are not silently bypassed. Invalid items or selection input produce a visible diagnostic and disable interaction until a valid input arrives. Expansion is local to the view instance and does not mutate project defaults.

## Reusable engine API

```csharp
using ScadaSpace.Core;
using ScadaSpace.Data;

var data = Scalar.FromJson("""
    [{"label":"Plant","items":[{"label":"Pump","data":{"pressure":12}}]}]
    """);
var tree = new DocumentTree(data);
var state = new DocumentTreeState(tree);
state.Reveal("0/0");
state.Select(["0/0"]);
var contextualSelection = state.SelectionData;
var matchingRows = state.Visible("Pump");
```

`DocumentTree` validates input and provides read-only path/key indexes. `DocumentTreeState` owns independent expansion and selection, supports reveal/expand-all and replacement reconciliation, and preserves explicit IDs across reordering. Selection and replacement are validated before committing state, including the separate output-property budget.

`TagTreeBuilder.Build(definitions, root)` produces a deterministic tree grouped by provider and folder. Root filtering respects folder boundaries: `Tank` never accidentally includes `Tank2`. `TagBrowseTree` uses that adapter with `Runtime.Tags.Browse()`. Selected contextual values contain full tag paths. It performs no tag writes, network requests or device discovery. Tag definitions are stable during an existing ProjectRuntime; changing the component root rebuilds its browse snapshot.

## Rendering and accessibility

Only expanded/filter-visible rows are projected into the list data source. Uno's `ItemsStackPanel` realizes/recycles native row controls for the viewport. The tree reuses the application's Uno/Skia rendering path; it does not create a raster screenshot per row or introduce an HTML-only fallback control. Unchanged input snapshots do not rebuild row models every simulation tick.

Custom automation peers expose Tree/TreeItem roles, labels, hierarchy levels and expand/collapse providers while retaining native list selection support. Browser tests inspect the actual peers and exercise pointer/keyboard input. These checks are not a complete screen-reader compatibility audit on every operating system.

## Limits and compatibility boundaries

A tree supports at most 4,096 items and the existing immutable-value limits (10,000 property nodes and bounded nesting). A selection's contextual payload must fit its own property budget. Oversized selection is rejected rather than partially committed. Projection/filtering is in-memory; there is no lazy remote-node loading. Material icon libraries, the full Perspective appearance/CSS schema, arbitrary SVG icons, drag/reparent editing, remote tag-provider protocols and the full Perspective Tag Browse Tree filter/event API remain outside this implementation. Labels currently use ellipsis with a tooltip rather than the full configurable overflow/appearance API.

The palette adds the two components to the existing catalog. Use the normal JSON project importer or existing binding editor to configure structured items; this change does not include the earlier unpublished nested-property inspector or configurable-popup work.

## Validation

`TreeTests` and `TreeHardeningTests` exercise immutable data, paths, selection payloads, ID reconciliation, filtering, tag-folder boundaries and bounded input. `tests/browser/trees.py` imports a real project through the file picker and drives the actual release-built Uno application, including a 1,200-row virtualization fixture. Results and realized-row observations are retained in the normal CI artifacts. Consult the successful run for the exact tested revision and pass counts.

Public references: [Perspective Tree](https://docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components/perspective-display-palette/perspective-tree), [Tree events](https://docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components/perspective-display-palette/perspective-tree/perspective-tree-scripting), [Microsoft list virtualization](https://learn.microsoft.com/en-us/windows/apps/design/controls/listview-and-gridview).
