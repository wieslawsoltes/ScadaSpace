# Translucent symbol materials

The graphics review corrected two compositing errors, without changing project property names or the Uno graphics backend.

The general CanvasPainter gradient previously gave both its shader stops and paint the first stop's alpha. Skia multiplies those alpha values, so a uniformly 50-percent-transparent gradient could be drawn at roughly 25 percent. Mimic materials also painted a solid undercoat before painting that gradient. Those two operations were not equivalent to one configured material. Gradient paints now use opaque white so stop colors supply alpha once; each Mimic shape draws either its gradient or its solid fill, not both. Shader state is reset in a finally block before subsequent primitives.

Tank and vertical-vessel water surfaces previously overlapped translucent rectangles and ellipses. The tank's separate waterline also used the unattenuated color. CanvasPainter.LiquidColumn now records the body and surface as contours in one winding path and paints them once. Its surface sheen changes RGB, not alpha. Flat surfaces use one rectangle. Explicit color alpha is multiplied by liquidOpacity exactly once. Zero opacity removes the whole surface, and shells, warning colors, annotations, and diagnostics remain independent.

The helper is reusable outside Uno. It rejects nonfinite or excessive geometry and invalid opacity before drawing, preserves the caller's canvas state, and uses no offscreen image, SaveLayer, readback, or per-frame path generation. Existing retained SKPicture caches record the changed geometry only when the symbol snapshot changes. Distinct equipment parts may still overlap physically; this is per-material alpha correctness, not an added whole-component opacity API.

MaterialReview is registered in the existing SymbolThemes raster executable. Its checks use actual transparent Skia surfaces rather than metadata: gradient alpha, brush reset, several liquid alpha values, zero opacity, transparent Mimic shells, cylindrical tank waterlines, all vessel appearances, cached alternating materials, and invalid input. It also writes a checkerboard contact sheet. Both existing graphics suites and all eleven native browser suites remain enabled.

Results must be verified on the exact source revision. Local .NET execution was unavailable during authoring. Raster allocation measurements and SwiftShader browser checks do not establish physical GPU throughput. These corrections improve configured appearance fidelity; they are not a claim of full or pixel-identical Perspective parity.

Primary references:
- Perspective Cylindrical Tank, liquidOpacity and warning appearance: https://www.docs.inductiveautomation.com/docs/8.3/appendix/components/perspective-components/perspective-display-palette/perspective-cylindrical-tank
- Perspective Symbols project state colors: https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/perspective-project-properties
- Skia paint and shader alpha: https://skia.googlesource.com/skia/+/d6832d047c24c7692d71909376d180ff4b58fd06/site/user/api/SkPaint_Reference.md
