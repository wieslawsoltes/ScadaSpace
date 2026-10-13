# LED displays: segments, native formatting and retained graphics

The LED Display component now draws seven- and fourteen-segment readouts using independently defined beveled vector diodes. This follows the public Perspective component's segment mode, numeric format, color and locale vocabulary; it is not a pixel-identical reproduction or an Ignition project archive/API.

## Component configuration and compatibility

New palette components use `segmentFormat: "14 segment"`, `numberFormat: "#,##0.00"`, `locale: "en-US"`, `backgroundColor: "#161616"`, `diodeOnColor: "#1EC963"` and an independently chosen inactive color `#193B29`. They remain the existing LEDDisplay type, not an additional catalog entry. Palette thumbnails use the same actual renderer.

Older definitions without segmentFormat retain the previous seven-segment mode, background `#22332D`, active `#8CEE87`, inactive `#30473B` and their existing `format` property (default `0.0`). Explicit `numberFormat` takes precedence over the legacy `format` alias. Adding segmentFormat opts into the new presentation defaults unless colors/format are explicitly supplied. Legacy Boolean sources retain their previous numeric 0/1 presentation; explicitly configured new displays require numeric or supported text values. No existing serialized project is rewritten just by rendering it.

The value accepts a number or, as a ScadaSpace extension, supported segment text. Numeric formatting changes presentation only, not the bound value. Fourteen-segment mode covers all ASCII A–Z letters in either case using uppercase-shaped glyphs, decimal/comma marks, colon, minus, plus, slash, degree, percent and spaces. Seven-segment mode has its naturally limited letter subset; unsupported characters generate an explicit diagnostic rather than silently blanking a digit. Arabic-Indic/Persian digits map to the corresponding digit geometry, and common Arabic number separators and invisible numeric direction marks are recognized. This is not an arbitrary Unicode text renderer or a complete right-to-left shaping engine.

Readouts are bounded to 64 UTF-16 characters. Numeric formats use the existing managed .NET format validation (bounded pattern length and precision). Nine common presets are exposed in the Property Editor: integer, one/two/three decimal, grouped integer/one/two decimal, and integer/one-decimal percent. These presets are not asserted to be the original product's exact preset list. Existing custom patterns remain selectable; the model API can supply other valid bounded .NET patterns. Unsupported characters produced by a pattern—for example an unsupported currency symbol—are diagnosed instead of misrepresented.

Locale is a bounded language tag and resolves through the runtime's CultureInfo/globalization data. en-US, de-DE, pl-PL and fr-FR formatting is covered in tests; the browser interaction fixture covers US, German and Polish numeric separators. There is no promise that every ICU locale or Java-style numeric pattern behaves identically to Perspective. Unknown tags can follow the managed runtime's fallback behavior. Colors accept `#RRGGBB` and explicit `#AARRGGBB`, not arbitrary CSS/HSL/style objects.

## Native Property Editor and runtime integration

The existing flat Property Editor uses real ComboBox controls for segmentFormat and numberFormat. Changing a selection is an ordinary undoable document edit. Current custom format values remain in the selection list. Bound properties—including qualified `props.numberFormat` bindings—are not directly editable and show their evaluated value. Binding configuration and removal preserve the existing qualified key rather than adding a duplicate unqualified binding. This is not the previously excluded nested-property-tree editor.

The shared ViewHost accepts numeric or supported text LED values, formats them through the Core presentation cache, and passes the resulting immutable snapshot to the actual HmiSymbol control. Normal property/tag/expression bindings can drive values, modes, formats, locale and colors. Failed inputs display a quality diagnostic and recover the same control after correction. Runtime changes remain local to the view instance and do not modify portable project defaults.

The reusable numeric HmiSymbol.Value dependency property still updates a numeric LED snapshot. Explicit text snapshots are not coerced to that numeric value. Unit text is independently fitted below the segment line; it is an existing ScadaSpace extension and does not consume segment glyphs.

## Reusable rendering and performance

Core owns LedDisplayOptions, LedDisplaySnapshot, LedDisplayPresentation, LedDisplayPresentationCache and LedGlyphs. Graphics owns LedDisplayRenderer and the reused CanvasPainter diode path. Controls owns the existing HmiSymbol/PropertyInspector integration. No rendering or formatting logic requires the application shell. ViewHost allocates a presentation cache only for the relevant renderer family; tanks, pipes and primitives do not allocate unused LED/industrial parsing caches.

```csharp
var presentation = LedDisplayPresentation.Create(
    Scalar.From(12345.67),
    new LedDisplayOptions(SegmentFormat: "14 segment", Locale: "de-DE"));

// The snapshot contains "12.345,67". The renderer can draw into any caller-owned canvas.
LedDisplayRenderer.Draw(canvas, bounds, presentation);

// Existing numeric dependency-property semantics can reformat without mutating the old snapshot.
var changed = presentation.WithNumericValue(25);
```

Character geometry is aspect-fitted and centered rather than stretching segment angles to the control's rectangle. Each diode is painted once with its active or inactive color. Decimal and comma marks are distinct. The plus sign is one winding path, avoiding double alpha at its crossing. The painter reuses its path across diodes; no new native path is allocated per segment. Rendering preserves caller transforms/clips and remains clipped to component bounds.

The presentation cache returns the same immutable snapshot on an unchanged input set, but still reads every current input before doing so. Failed/reentrant reads never publish partial results. The existing per-control SymbolRenderCache records one vector picture for the LED snapshot. Idle playback does not rebuild formatting or diode geometry; changed values/materials correctly invalidate the picture. There is no LED timer, raster copy, canvas readback or global unbounded cache. Binding evaluation and assembling SymbolData can still allocate; this does not eliminate every application allocation.

## Validation and remaining boundaries

The core suite exercises formats, locales, glyph coverage, legacy compatibility, unsupported/oversized input, immutable snapshot reuse and failure recovery. The raster suite tests actual pixels for segment distinctions, every ASCII letter, punctuation, configured colors, alpha coverage, quality states, clipping and cached/immediate equivalence, and writes a contact sheet and warmed allocation fixture. The twelfth real Uno browser suite uses file-picker imports and native pointer/keyboard input to exercise inspector edits/undo, qualified binding protection, locale and text updates, diagnostic recovery, full export isolation and tablet layout. A standalone consumer restores only packed packages to check the new Core APIs.

Results belong to the exact Actions revision. Test source or a successful compilation alone is not browser runtime evidence. Raster allocation measurements are not physical-GPU FPS, and SwiftShader tests do not qualify native desktop runtimes. Full CSS color/style semantics, arbitrary Unicode/RTL shaping, exact source artwork, original numeric-format semantics and complete Perspective display-component parity remain outside this implementation. The excluded chart-renderer/path-cache, configurable-popup and nested-property-tree work is unchanged.

Public references:
- Perspective LED Display component: https://www.docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components/perspective-display-palette/perspective-led-display
- .NET numeric format strings: https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-numeric-format-strings
- SkiaSharp SKFont.MeasureText: https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skfont.measuretext

No proprietary source, logo, font or symbol asset is copied from those references.
