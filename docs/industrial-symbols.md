# Industrial symbols and vector-rendering review

This implementation follows the public Perspective industrial-symbol families and appearance vocabulary using independently drawn Skia geometry. It is not a pixel-identical reproduction, an SVG import path or complete Perspective styling/Gateway compatibility.

## Implemented symbols and controls

Pump, Motor and Valve now have distinct **Mimic**, **Simple** and **P&ID** vector appearances. **Sensor** and **Vessel** are new concrete components, bringing the component catalog to 35. Pumps include centrifugal/vacuum variants, independently positioned feet and four outlet orientations. Motor and sensor directions also support top/bottom/left/right. Valves support reverse flow and six explicit states: default, open, closed, partiallyClosed, failedToOpen and failedToClose. Other industrial symbols support default, running, stopped and faulted.

Mimic uses outlined gradient metalwork, flanges/ribs and instrument housings; Simple reduces detail; P&ID uses schematic geometry. Geometry is normalized and aspect-fitted so a wide component does not stretch a circular pump into an ellipse. Labels and readouts are arranged outside that body and remain upright when the equipment orientation changes. Text options support top/bottom/left/right/hidden, left/center/right/auto justification, bounded font size and explicit hexadecimal color. Arbitrary annotation CSS is diagnosed rather than claimed as implemented.

Vessels support vertical/horizontal orientation, stand, agitator, fill visibility, liquid color/opacity and a structured value with independent capacity. Liquid remains aligned to screen gravity for a horizontal vessel, rather than rotating a vertical waterline sideways. The nested value/capacity fields require numbers and displayValueAsPercent requires a Boolean.

Cylindrical Tank remains a separate display component. Its silhouette now clips metal and liquid fills precisely; it supports capacity, shell/warning colors, liquid opacity, a strict greater-than percentage warning threshold, optional scale, and value-display formatting with prefix/postfix units. Existing gauges, thermometers, LED displays, pipes and drawing primitives retain their distinct renderers, now using the shared picture cache and numerical/drawing-state checks. This does not mean every original display component or drawing mode is available.

## Native library and palette

**Tools → Symbol Library…** opens nine native previews: centrifugal and vacuum pumps, motor, valve, sensor, vessel, tank, gauge and thermometer. Change appearance, machine state, machine/vessel orientation, animation speed or reduced motion. **Use session defaults** sets the current RuntimeSession's automatic appearance/speed and reduced-motion preference without changing process values or portable project defaults. Closing the dialog without applying retains the prior session settings. Its controls and previews are separately reusable in ScadaSpace.Controls.

The palette now uses the real HmiSymbol renderer for its symbol/display thumbnails instead of one generic category glyph. Thumbnail and designer animation is disabled. Existing search, pinning, list/tile display, insertion, drag/drop and control-reuse behavior remain available. The gallery does not create a new authenticated/shared Gateway session or store these preferences in the project.

## Configuration examples

Pump properties (shown in native JSON for readability; project serialization uses tagged Scalars):

```json
{
  "appearance": "auto",
  "state": "running",
  "animationSpeed": "auto",
  "orientation": "right",
  "variant": "centrifugal",
  "feet": "bottom",
  "label": { "text": "P-101", "location": "top" },
  "value": { "text": "42 m³/h", "location": "bottom" }
}
```

Vessel properties:

```json
{
  "appearance": "mimic",
  "orientation": "horizontal",
  "state": "running",
  "displayStand": true,
  "displayAgitator": true,
  "displayFillLevel": true,
  "liquidColor": "#00ACAC",
  "liquidOpacity": 0.7,
  "value": { "value": 50, "capacity": 200, "displayValueAsPercent": true }
}
```

Existing property/tag/expression bindings can drive these options, including nested vessel value fields. A failed binding or invalid appearance/state produces a diagnostic overlay and stops that symbol's animation; correcting the input recovers the same control. Older machine definitions without a state property still map nonzero/true values to running/open and zero/false to stopped/closed. New catalog components explicitly start in default state. Theme-wide state colors are currently fixed, not configurable by the legacy generic color property.

## Reusable architecture and performance

Core owns immutable SymbolOptions, SymbolText, SymbolDefaults and TankOptions, typed readers and overflow-aware range normalization. RuntimeSession owns session-local automatic settings. Graphics exposes standalone SymbolRenderer, family-specific drawing files and SymbolRenderCache to any caller supplying an SKCanvas. Controls owns HmiSymbol, the demand-driven composition scheduler, and SymbolLibrary. None of the drawing logic requires the application shell.

Each HmiSymbol retains one vector SKPicture keyed by its data, dimensions, kind and drawing typeface. It does not snapshot into a raster bitmap or copy an image on every UI refresh. Static geometry is replayed; a small moving overlay uses retained paint/font resources. The cache is per control rather than an unbounded global path map, and its native resources are released on unload/disposal.

SetSnapshot batches property changes and avoids invalidating an unchanged control. A shared CompositionTarget subscription runs only while there are registered animated controls and caps animation invalidations at 30 Hz; there are no per-symbol timers or process writes. Faulted, stopped, bad-data, reduced-motion, designer and thumbnail states do not schedule animation. Hidden/zero-opacity ancestors and fully clipped root/scroll viewports skip invalidation. Offscreen controls remain registered so scrolling can reveal them without waiting for a data change.

Runtime property evaluation still occurs on the existing refresh cadence. The presentation cache reuses unchanged parsed options, but constructing SymbolData and evaluating bindings can still allocate; the caches remove repeated presentation/geometry construction, not all application allocations. Live values can correctly rebuild a symbol picture. Chart-series refresh/path optimizations are separate and remain unchanged.

## Correctness and validation

Engine tests cover explicit vocabulary, automatic settings, nested annotation/value types, quality handling, capacity, range extremes, and finite bounded animation phase including subnormal speeds. Drawing validation preserves the caller's canvas matrix/save stack; quality overlays use a bounded integer stripe loop so large finite offsets cannot cause an increment-stall loop. Tiny/empty bounds are handled without invalid rectangles.

The raster suite renders all 13 SymbolKind values, compares distinct appearances/states/variants, verifies clipping and annotation layout, checks cached-versus-immediate pixel equivalence and invalidation, tests quality/animation behavior, and writes an appearance contact sheet. It also compares warmed, same-data immediate drawing versus cached playback allocations/timing. These measurements use a raster Skia surface: they are not whole-application FPS, user input latency, or physical-GPU performance claims.

The native browser suite imports its fixture through the file picker and uses actual fields, menus and checkboxes to change appearances, states, speed, nested values and error conditions. Passive counters check static-picture reuse and animation scheduling; screenshots record the real Uno output. Session-default application, export isolation and tablet rendering are covered. Existing browser suites remain enabled; their catalog expectations rise to 35 rather than dropping control-reuse assertions. Standalone package consumers exercise symbol presentation and session APIs without project references.

Executed success must be established by the matching Actions artifacts. Source test definitions alone are not proof of runtime behavior. Desktop compilation remains compilation evidence; browser CI uses SwiftShader rather than qualifying physical GPUs or native operating systems.

## Remaining visual and functional boundaries

The symbol library is closer to the documented families, not complete or pixel-identical parity. Project-defined custom state palettes, light/dark state color/flash policies, full CSS/text-style APIs, exact original artwork/animation trajectories, every HMI component and every graphic mode remain outstanding. Session defaults are local, and normal project replacement resets the session. Full Gateway/device/scripting/security/durable-history and industrial qualification remain outside this simulation.

No proprietary source, logos, SVG symbols or icon packs are bundled. Earlier excluded **chart renderer/path-cache**, configurable-popup and nested-property-inspector proposals are not included; this cache is specifically for symbol rendering.

Public references: Perspective Pump, Motor, Valve, Sensor, Vessel and Cylindrical Tank component pages and the Project Properties Symbols section in the Inductive Automation 8.3 manual. The component property vocabulary and visual examples are feature references, not imported implementation/assets.

## Symbol continuation review

The recovered demo fixes bind explicit machine states alongside the old Boolean process values, expand the component gallery to contain new symbols, and honor explicit disabled tank units without duplicating percentage signs.

The drawings now distinguish the vacuum Simple cutaway from the Mimic motor/volute, use curved centrifugal impellers and separate mounting feet, horizontal motor cooling ribs, and valve housings/handwheels rather than using the P&ID bow-tie for every appearance. Vessel Simple frames and instrument fittings are refined independently. Same-edge bottom captions now precede readouts; `auto` justification faces the symbol when annotations are at its sides. Gauge and thermometer graphics fit their aspect ratio rather than stretching their geometry and text to any rectangular slot. A separate display contact sheet complements the industrial appearance sheet.

Liquid opacity zero removes waterline outlines as well as filled surfaces. `SymbolPresentationCache` retains immutable parsed option/annotation records between equal input snapshots, visits every current input before returning a hit, and cannot conceal a failed binding. Failed or reentrant reads do not publish a partial cache entry. Its cache-hit allocation regression is separate from graphics playback measurements. Ordinary designer edits and undo retain the current session's symbol preferences; replacing the project still creates a new session.

Animation invalidation now intersects root and ancestor ScrollViewer bounds and skips fully clipped symbols. Partly visible symbols still animate, and registered offscreen controls remain in the single shared scheduling set so scrolling can reveal them without a new data update. Disposed controls cannot resume scheduling or rebuild native pictures. Arbitrary nonrectangular clips are not used for culling.

The earlier native symbol suite reached export isolation with unchanged process/default values, but compared compact fixture Scalar JSON against serialized Scalars with explicit inactive fields. The corrected assertion compares the entire project exported before and after runtime interactions through actual download commands. It does not discard the isolation requirement. The event editor opening predicate also waits for its native event-row layout, not just allocation of the dialog object. Prior assertions remain enabled; additional scenarios cover viewport culling and preference retention across edit/undo.

Reference appearance images reviewed from the official 8.3 Pump, Motor, Valve and Vessel component pages. These small visual examples guide proportions and distinct drawing modes; they are not bundled, traced into assets, or used as evidence of pixel-identical parity. The Sensor image could not be retrieved in this continuation; its previously implemented independent geometry received fitting/detail improvements, not a claim of exact matching.
