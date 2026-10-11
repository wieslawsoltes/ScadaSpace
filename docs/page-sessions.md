# Page navigation and docked sessions

This is an independent implementation of specific Perspective-style page behaviors. It uses ScadaSpace's project schema and C# APIs, not the Ignition Gateway protocol or Python scripting API.

## Page configuration

Use the **Pages** toolbar button or **Project → Page configuration** in design mode. The reusable `PageConfigurationEditor` edits page routes, primary views, titles, parameters, shared and local docks, display/content modes, size constraints, and corner priority. Applying is one validated, undoable document transaction. Cancel does not change the project; applying after another project edit is rejected instead of overwriting it.

Routes begin with `/` and may contain whole-segment placeholders such as `/equipment/:equipment`. Literal routes win over parameter routes. Duplicate route shapes and invalid parameter names are rejected. Route captures are strings, merged with view/page defaults and explicit navigation inputs. Query values remain separate, read-only strings under `page.props.urlParams`; they never silently overwrite view inputs.

## Reusable navigation APIs

```csharp
using ScadaSpace.Core;
using ScadaSpace.Runtime;

// runtime is a ProjectRuntime containing a configured /equipment/:equipment page.
var session = new RuntimeSession(runtime);
var ok = session.NavigatePage("/equipment/:equipment",
    new Dictionary<string, Scalar> { ["equipment"] = Scalar.From("Pump Łódź") },
    new Dictionary<string, Scalar> { ["mode"] = Scalar.From("auto + manual") });
if (!ok) throw new InvalidOperationException(session.LastError);
```

`Navigate(url, parameters, replace)` remains available for explicit URLs. `NavigatePage` expands an existing configured pattern, validates the combined inputs, and encodes query values before changing history or dock state. Both preserve the current page on validation failure.

`PageAddress.WithQuery` produces a new address by merging strings, booleans, and round-trip-precision numbers. Null removes a query field. Structured query values are rejected. Existing address snapshots and supplied dictionaries are not mutated. Unicode is encoded once with strict UTF-8 decoding. Limits apply to the canonical encoded URL as well as raw input, so accepted addresses remain valid after copying or reloading. The current limit is 4,096 URL characters, 32 segments, 512 characters per decoded segment, and 64 query fields.

## Browser history

An isolated session is launched with **Session**. GitHub Pages deep links use the fragment, for example `?session=1#/equipment/Pump%20A?mode=auto`, keeping the deployment base path and app-level query flags separate. Browser Back/Forward, the session navigation bar, and declarative navigation actions share bounded history snapshots. Duplicate `popstate` and `hashchange` notifications coalesce; replacing ScadaSpace's history entry preserves unrelated host metadata. Unknown adapter operations and invalid traversal distances are rejected.

The C# history retains at most 128 entries. Explicit non-URL navigation parameters are retained within that runtime history but are not persisted across a full reload. Direct links and reloads reconstruct inputs from the URL and project defaults. A separate browser tab launches an independent simulation rather than connecting to a shared server session.

## Docks and layout

Each edge supports one effective view, with a page-local edge overriding the shared edge. Unchanged shared dock instances retain local input across page navigation. Local overrides and removed definitions dispose/recreate the affected host rather than leaking state to another view.

Display modes are `Visible`, `OnDemand`, and `Auto`. Content modes are `Push`, `Cover`, and `Auto`. An Auto dock follows its configured viewport-width breakpoint and resets manual open/closed overrides when the breakpoint is crossed. Runtime opening accepts additional parameters without modifying project defaults. Visible docks cannot be closed. Resizing clamps to the configured minimum and maximum.

`PageLayout.Arrange` is a UI-independent geometry engine for all four edges and both corner priorities. Opposing docks share constrained space proportionally, reserving up to 32 units for the primary view. It returns nonnegative geometry for small and zero-sized viewports. `SessionPageHost` uses these results for the primary view, actual Uno view hosts, toggle handles, splitters, and page-local modal shading. Other primary/dock controls are disabled while a modal dock is open. Declarative `OpenDock`, `CloseDock`, `ToggleDock`, `NavigateBack`, and `NavigateForward` actions are supported.

## Typography correction

The application previously referenced a Roboto resource not supplied by its dependency set. It now uses Uno's existing Open Sans resources for both native UI and custom Skia drawing. The browser suite records the decoded typeface family and measured narrow/wide glyph advances, in addition to screenshots. No font binaries are added to this source repository.

## Validation and remaining boundaries

`PageTests`, `PageContextTests`, and `PageAddressTests` contain 28 deterministic route, history, dock, geometry, and query scenarios in addition to the existing 88 engine scenarios. The 15 Node tests execute the actual browser adapter with a controlled history/location harness. `tests/browser/pages.py` exercises the real release-built Uno UI: configuration/undo/redo, session launch, route/query inputs, shared state, modal/auto/cover docks, resizing, browser history, deep links, Unicode reloads, rejected addresses, and exports. These are separate layers of evidence; Node adapter tests are not a substitute for the native browser interactions.

Not implemented here: multiple dock definitions on the same edge, full Perspective CSS/animation behavior, full page-property writeability, shared remote sessions, identity/RBAC, Gateway scripting, or distributed persistence. Views are fitted within each region rather than reproducing every Perspective layout rule.

The configurable popup prototype from this continuation was blocked at source upload and is **not included** in the published branch or these tests. Existing simple popup behavior remains; independent popup IDs, configurable position/drag/resize/modal options are not shipped by this PR. The previously blocked nested-property inspector enhancement is also excluded.

## Public references

- Perspective pages and page properties: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/pages-in-perspective
- Perspective sessions: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-sessions
- Uno font resources: https://platform.uno/docs/articles/features/custom-fonts.html
