# Project symbol states and light/dark materials

ScadaSpace's **Project → Symbol States…** command opens a native state editor modeled on the public Perspective Project Properties Symbols workflow. These are independently implemented controls, immutable C# configuration and Skia materials, not an Ignition archive format or a claim of complete visual/functional parity.

## Authoring workflow

Select a state in the searchable left inventory. The right panel contains its name, visual theme, Available/Applied Symbols, four colors, independent animation/flashing switches and paused previews of all five industrial families. Click a family to move it between Available and Applied. The color fields reuse the native alpha-aware picker with explicit Use color and Cancel. Invalid colors hide the old preview and prevent Apply rather than suggesting stale colors are the new configuration.

The nine built-in names remain available: default, running, stopped, faulted, open, closed, partiallyClosed, failedToOpen and failedToClose. Their names cannot be changed or deleted; their theme values and applied families are configurable. Custom state names are case-sensitive ASCII identifiers of 1–64 letters, digits, hyphens or underscores. The catalog allows at most 128 stored overrides, with at most the five supported families per state.

**Add** starts from factory default theme values with no applied families. **Duplicate** copies the selected state's current values and family assignments and records its own creation baseline. **Delete** requires explicit confirmation; components still producing a deleted name report a diagnostic rather than silently presenting a different state.

**Apply** makes one undoable project transaction and leaves the dialog open. **OK** applies and closes. **Cancel** drops only changes made since the last Apply. An unchanged Apply and a reverted-to-original catalog do not create redundant history. The draft rejects stale document generations. If a Changed observer throws after a successful commit, the exception remains visible while the draft records the committed generation.

**Undo Theme** restores the last applied settings for the selected light or dark theme. Its baseline follows a renamed state. For an unapplied newly created state it restores creation values. **Reset Theme** restores the original factory values for built-ins or the creation values for custom states. Reset does not erase the separate last-applied Undo baseline. A rename updates explicit literal component state fields in the same transaction; it does not rewrite expression source, runtime local values or external tags. These boundaries are shown in the editor.

## Rendering and compatibility

Primary Color supplies body fill. Secondary Color supplies the alternate fill while flashing. Stroke Color supplies equipment outlines. Tertiary Color supplies the default vessel liquid. Both light and dark profiles are stored in the project; their values need not match.

All five existing industrial families—Pump, Motor, Valve, Sensor and Vessel—use the materials across Mimic, Simple and P&ID. Pump includes centrifugal and vacuum variants. Independent mechanical and flash switches allow a custom state to move without flashing or flash without moving; effects are not inferred solely from whether the name is running/open. Known failure names still retain their failure indicator. An unapplied family or unknown state name receives an explicit diagnostic, and correcting the state recovers the same native control.

Explicit component `liquidColor` values retain precedence over a state's tertiary default, including older projects. Newly created vessels omit that redundant property so they inherit the state color until an explicit override is added. `SymbolOptions.UseStateLiquidColor` exposes the same choice to direct renderer consumers. Removing an override resumes state inheritance without mutating the catalog. Existing tank warning/unit behavior and vessel warning colors remain independent.

Colors use **#RRGGBB or #AARRGGBB**, not CSS eight-digit RGBA ordering. Arbitrary CSS variables, stylesheets and unsupported annotation properties are not interpreted as implemented theme features.

**Tools → Symbol Library…** previews project state profiles and provides a session-local light/dark selector. Use session defaults changes symbol presentation only: it does not change the project, process values, or the surrounding application's CSS/native theme. Reduced motion disables both mechanical and flashing effects; speed zero also holds the primary state. Editor and palette previews remain paused. A new project or session still starts with its own defaults; ordinary design edits and undo retain existing session preferences.

## Reusable API

Core provides immutable `SymbolStateStyle`, `SymbolStateDefinition`, `SymbolStateCatalog`, `SymbolOptions`, `SymbolDefaults` and the presentation cache. Designer provides `SymbolStateDraft`; RuntimeSession owns active presentation preferences. Graphics owns retained pictures and material drawing, and Controls owns `SymbolStateEditor`, `SymbolLibrary` and `HmiSymbol`.

```csharp
var state = new SymbolStateDefinition(
    new(PrimaryColor: "#4488BB", EnableAnimation: true),
    new(PrimaryColor: "#112244", SecondaryColor: "#6688BB", EnableFlashing: true),
    new[] { "Motor" });

var draft = new SymbolStateDraft(document);
draft.Create("maintenance");
draft.Set("maintenance", state);
draft.Apply(document);

session.Symbols = new SymbolDefaults(Theme: "dark", ReducedMotion: true);
```

Catalog maps, applied-family lists and theme records own their contents. Ordinal lookup is independent of a caller dictionary's comparer. Built-in reset baselines cannot be replaced by caller-supplied overrides. Canonical catalogs omit unchanged built-ins, keeping serialization and no-op history stable. State configuration lives in `ProjectDefinition.SymbolStates`; older projects without it load the built-in defaults.

## Performance and validation

A symbol normally retains one vector SKPicture. Active flashing adds a second picture for the alternate material, not a new recording every frame. A data, size, theme, kind or typeface change rebuilds the bounded cache; Clear, unload and disposal release both native pictures. Moving overlays retain paint/path resources. Flash-only symbols invalidate only when the displayed fill phase changes, rather than at every 30 Hz composition tick. The single existing scheduler still handles mechanical motion and conservative viewport culling.

The local speed model allows at most one flash cycle per second at 100 percent. This is a bounded implementation choice, not a guarantee of exact Ignition timing or accessibility certification. Bad-quality, reduced-motion and editor states do not flash. Offscreen controls remain registered for scrolling recovery but skip paint invalidations. This work does not eliminate every binding/SymbolData allocation or add chart path caching.

The main workflow runs engine regressions, both graphics suites, standalone package consumers and all eleven actual Uno browser suites before Pages can deploy. Raster checks exercise all five families and three appearances, primary/alternate/stroke/liquid pixels, independent effects, cache invalidation, caller canvas state and warmed allocations. Browser input covers authoring, resets, confirmation, undo/redo, runtime state changes, dark materials, effect scheduling, diagnostics/recovery, full export equality and tablet scrolling. Observations are passive and expose no managed command dispatcher.

Executed results belong to the exact Actions revision; source test definitions alone are not success evidence. Raster allocations do not measure physical-GPU throughput or application FPS. Browser CI uses SwiftShader; desktop compilation is not all-platform runtime qualification.

## Remaining scope

This is not a complete Perspective theme engine, project-properties dialog or production SCADA system. Full application/component CSS theming, initial-theme project settings, unrestricted theme/name syntax, expression-aware state refactoring, original artwork/animation matching, complete control APIs and Gateway/device/scripting/security/durable services remain outstanding. Previously excluded chart renderer/path-cache, configurable popup and nested-property-inspector work is unchanged.

Public references:
- Perspective Project Properties, Symbols: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-project-properties
- Perspective Built-In Themes: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/styles/perspective-built-in-themes

The public workflow and property descriptions are references; proprietary source, icon packs and symbol assets are not redistributed.
