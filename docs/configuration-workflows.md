# Event configuration and Configuration Explorer

This iteration adds the native three-pane event/action workflow and a searchable configuration inventory to the independent ScadaSpace designer. They follow the public Perspective Designer workflow, but do not implement proprietary project archives, Jython, remote Gateway services, or the complete event/action catalog.

## Configure Events

Select one component in Design mode and choose **Component → Configure Events…**, or right-click a canvas component and choose **Configure Events…**. The canvas context menu selects the clicked component first and also offers the existing editing commands.

The event list is at the left. The middle list organizes actions; Add, Remove, Move Up/Down and Duplicate operate on that event's ordered list. The right pane edits the action type, target, value type/value, parameters and Enabled option. Target selectors list local tags, configured pages, views or dock IDs as appropriate; typed target input remains available. Numeric values use round-trip formatting rather than display rounding. Structured values and action parameters use bounded native JSON, not executable code.

**Apply** commits all staged event edits as one undoable document operation and keeps the dialog open. **OK** applies and closes. **Cancel** discards only edits made since the last Apply. Invalid field values retain the dialog and do not partially update the project. An editor opened before an intervening document generation change cannot overwrite the newer project. Merely opening/configuring events never executes them or writes tags.

`EventConfigurationDraft` is UI-independent and stages only component event configuration, not another full project clone. Its public action records own immutable parameter/value snapshots. The existing `DesignerDocument` still performs its normal whole-project validation and undo snapshot when a real Apply occurs; this work does not replace that transaction strategy.

## Supported host events and actions

The current host wires native `onClick`, `onDoubleClick` and `onContextMenu` interactions. Button activation additionally raises `onActionPerformed`, while existing projects' `onAction` handler remains supported. Existing Tree/Tag Browse Tree `onItemClicked` and Table `onRowClick`/`onEditCellCommit` hooks are exposed. Native routing and hit testing still determine which elements receive interactions; these are not complete browser DOM event objects or Perspective propagation semantics.

Already-configured custom/unwired event names are preserved and explicitly labeled. The editor does not advertise unimplemented startup/shutdown, keyboard, touch, arbitrary scripts or security actions as operational. It offers the existing WriteTag, ToggleTag, Navigate, NavigateBack/Forward, Open/Close/ToggleDock, simple Open/ClosePopup and SendMessage actions. Existing simple popup behavior is unchanged; no excluded configurable-popup proposal is introduced here.

Actions gain an Enabled flag, defaulting to true for backward compatibility. Dispatch validates and snapshots the whole action sequence before the first side effect, so callbacks cannot mutate a later action's target/value/parameters while it is running. Reentrant dispatch is rejected, including recursion through tag observers, and the guard is released on failure. Disabled actions are retained but skipped. A malformed configuration tail cannot cause an otherwise-valid prefix to execute. Runtime target failures still stop execution after any already-successful actions; this is not a transactional multi-tag write, distributed rollback or authenticated security boundary.

Local actions are synchronous and ordered. This is deliberately documented rather than claiming Perspective's asynchronous script scheduling. Resource bounds are 64 handlers per component and 256 actions per handler, together with existing project/structured-value limits.

## Configuration Explorer

**Component → Configuration Explorer** opens a project-wide inventory of bindings, transforms, events, actions, embedded views and repeaters. Filter by kind and text across view/component/location/source. Select a row to inspect its owned source details; **Go To Reference** closes the inventory and selects that component in the designer. This iteration navigates to the component; it does not automatically open every possible specialized editor.

`ConfigurationIndex` rebuilds only when the owning `DesignerDocument` or its generation changes. The index stores immutable reference records rather than mutable component objects. Repeating an unchanged filter reuses the same result collection without traversing the document. The 25,000-entry bound fails explicitly rather than silently omitting configurations. All designer definition mutations must use the document transaction API to participate in generation tracking.

Project action validation checks parameter node/depth totals without allocating a discarded immutable copy. Dispatch executes its already-owned records directly rather than converting them back into mutable actions. The inventory cache-hit path is separated from the closure-bearing tree scan. These changes retain the same limits and fail-before-dispatch contract.

Palette and resource-tab observation maps are retained read-only views instead of newly allocated dictionaries on every telemetry refresh. Unconfigured generic mouse handlers are not attached to every HMI control. New performance checks compare rebuilding and caching the same configuration query; elapsed times are observational and allocation/result-equivalence invariants are the CI gates.

## Validation and scope

Deterministic tests cover detached edits, repeated Apply, undo/redo, cancellation isolation, stale-document rejection, order/disable changes, malformed maps, owned dispatch sequences, reentrancy/error recovery, configuration reference kinds, immutable cached queries and invalid filters. Standalone NuGet consumers exercise the new Core/Designer APIs without project references.

`tests/browser/events.py` uses actual Uno menus, canvas right-click, lists, fields and dialog buttons. It configures and executes ordered actions, tests failure/Cancel/Apply/undo/redo, native click/double-click/context-menu events, inventory filtering, Go To Reference, export and tablet interaction. Observations contain no managed command dispatcher. All prior browser suites remain enabled; source revisions and executed results are captured by Actions.

The UI is closer in structure and interaction, not certified pixel-identical. Arbitrary dock/window positioning, full icon/typography/CSS parity, Jython, action security/permissions, complete event payloads and propagation/default behavior remain outstanding. Previously excluded chart-renderer/path-cache, configurable-popup and nested-property-inspector work remains excluded. The application remains an isolated local simulation; browser CI uses SwiftShader and desktop CI compiles rather than qualifying runtime behavior on every OS.

References:
- Perspective Designer Interface: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-designer-interface
- Component Events and Actions: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/working-with-perspective-components/component-events-and-actions
