# Designer workspace and UI review

This iteration follows the public Perspective Designer's workspace structure while retaining ScadaSpace's own project format, implementation and artwork. It does not claim pixel-identical rendering, complete Perspective behavior, or access to a real Ignition Gateway.

## Workspace behavior

The designer now has a compact menu/toolbar area, left Project and Tag Browsers, a full-height right Property Editor, bottom resource tabs and a narrow Components rail. Toolbar commands use independently drawn vector glyphs, not an icon font or proprietary assets. Tooltips and automation names describe every command. Existing simulation, page configuration, import/export, preview, editing and session controls remain available.

The component palette is auto-hidden initially. Click **Components** to reveal it, use its pin button to reserve space above the Property Editor, or unpin to return to the rail. Pane close buttons hide the pane rather than destroying its controls. **View → Panels** and the lower-left **Panel Chooser** restore hidden panes. **View → Reset Panels** restores the default arrangement. Splitters resize the left/right groups and the split between their child panes; no arbitrary drag-docking or floating OS-window layout is provided here.

Panel widths, split ratios, visibility, palette pinning and list/tile mode are stored separately from project content. The browser uses `scadaspace.workspace.v1`; the desktop host uses `workspace.json` in its existing data directory. Settings are bounded, versioned and validated. Splitter writes are coalesced through the existing UI timer instead of synchronously writing storage on every pointer event. A malformed saved configuration falls back to defaults; storage failures are reported. Preferences do not mark the project dirty and are not exported with it.

The existing responsive behavior is retained: the left pane hides below 1,050 pixels, and the right pane below 850. These are simplified workbench breakpoints, not full Perspective mobile/designer layout equivalence.

## Resource tabs

Bottom tabs support activate, close, close others and close all. Closing a resource **never deletes a view or discards project edits**. Closing the active resource selects its nearest surviving neighbor. Closing all resources shows an empty workspace with a reopening action while Save and Export remain usable. Reopening through the Project Browser or empty-workspace action loads the retained view definition.

The current project opens all its views when installed or reloaded; the open-tab list itself is session-local, not persisted. The native host handles Ctrl+W and Ctrl+Tab when those keys reach the application; browsers may reserve them, so the visible close buttons and context menus are the portable interactions. This is not a complete docking/document-window manager.

`DesignerWorkspace` in the UI-independent Designer package maintains validated available/open resource state and immutable snapshots. `DesignerPanels` owns bounded local preferences. `ResourceTabStrip`, `DockPane`, `DesignerIcon`, `DesignerToolButton` and `ComponentPalette` remain separately reusable Uno controls in the Controls package.

## Palette and Project Browser

The palette offers search, collapsible categories, expand/collapse all and a three-column tile mode. Searches reveal matching collapsed categories without overwriting their normal expansion state. Clearing the filter restores those choices. Insertion and drag/drop use the existing designer transactions. The vector glyphs are category-level illustrations, not the original product's complete component icon pack.

Project Browser search matches view names, component names and types while retaining ancestors. Clearing the filter restores expansion choices. Resource/component nodes are reconciled by stable IDs instead of clearing the entire TreeView whenever a document changes. Existing page configuration and style resources are shown without inventing unavailable Gateway/device services.

## Performance and correctness review

Already-active resource activation retains the ViewInstance rather than reloading it. Tabs are keyed and retained when activation or neighboring tab state changes. The 33 component buttons are created once per palette rather than reconstructed on each search. Browser counters verify these control-lifecycle invariants. Project tag definitions and total component count are cached at their change boundaries, and shell history counts use the allocation-free historian accessor rather than copying the full series.

Global edit shortcuts now inspect the focus ancestry of native text, numeric, password and selection editors. Deleting text in Project Browser search must not delete the selected canvas component. Empty-workspace telemetry and inspector state tolerate a missing active view. Page Configuration activation uses explicit resource metadata rather than matching a user-controlled display name. The palette becomes noninteractive when no resource is open. Previously available application commands are retained in a dedicated partial source file to separate command handling from layout and observations.

The pure workspace benchmark measures reactivating the current item in a 128-resource model. It requires unchanged snapshot identity/revision and zero per-operation allocation; elapsed times are observations only. This is not a whole-application latency, FPS or physical-GPU benchmark. Broader incremental scene rendering, whole-project undo snapshots, complete tree virtualization of the designer itself and chart allocation work remain open.

## Validation and remaining boundaries

Nine deterministic workspace tests cover configuration round trips, invalid inputs, resource reconciliation, close/cycle behavior, snapshot immutability and no-op activation. The browser workspace suite uses real pointer/keyboard input for menus, tabs, filters, palette insertion, splitters, storage reloads, undo/redo, closed-tab export and tablet layout. Its JavaScript state is observation-only; it does not expose managed command execution. All preceding application, structured-runtime, page/dock, tree and history/numeric suites remain enabled.

Review evidence applies to the exact tested revision, not an exhaustive correctness proof. Independent vector glyphs and the reorganized shell bring the UI closer to the documented designer but do not establish visual equivalence in every state. The previously blocked chart renderer, configurable-popup and nested-inspector proposals remain excluded. Full scripting, Gateway/device connectivity, enterprise security, distributed persistence, complete component parity, native desktop runtime and screen-reader qualification remain outstanding.

Reference: [Perspective Designer Interface, Ignition 8.3](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-designer-interface). No proprietary implementation or artwork from that reference is bundled.
