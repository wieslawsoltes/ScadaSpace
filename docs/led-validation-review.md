# LED validation review

The first complete LED browser run passed all eleven pre-existing suites and its first five LED assertions. The next selector click observed the new component selection before the rebuilt Property Editor had native dimensions. The saved final observation has the correctly arranged number-format selector and no application errors. The fixture now waits for positive native selector dimensions and editability for that same selected component before clicking once. No fixed delay, retry, assertion removal or product command bypass is used.

## Browser globalization data

The next run passed all eleven existing suites and the LED native editing, caching and German-locale assertions. Polish locale then revealed a product packaging defect: both actual displays held `locale: pl-PL` but formatted the number with English punctuation. This was not a failed binding or an unfinished drawing; both controls had successfully rendered the wrong cultural format with no diagnostic. The WebAssembly default startup-culture ICU subset did not contain Polish data.

The browser target now sets the .NET WebAssembly SDK's `WasmIncludeFullIcuData` property. This makes dynamic locale selection use the included full ICU data rather than silently falling back to the startup subset. Desktop builds remain unchanged. It increases the initial globalization payload in exchange for the advertised dynamic-locale functionality; no locale-specific string replacement or relaxed browser assertion is used. Reusable library hosts likewise need appropriate globalization data.

Primary build-property reference: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/build/WasmApp.Common.targets

## Published-manifest verification

The first ICU property change did not alter the published loader: the downloaded 9d00d66 application still declared `globalizationMode: sharded` and listed the three partial ICU files. The browser correctly repeated the Polish-format failure. Setting an MSBuild property was therefore not sufficient evidence that the intended data was deployed.

The SDK switch now lives in the unconditional project property group, so Uno's target-framework normalization and nested publish evaluation cannot exclude it through the previous browserwasm condition. It is consumed only by the WebAssembly SDK; it does not change desktop globalization. The workflow records evaluated framework/globalization properties and runs a new post-publication gate that parses the actual embedded boot JSON, requires mode `all`, requires the full `icudt.dat` resource, and checks the deployed bytes against its SHA-256 metadata. A wrong mode or missing/mismatched asset fails before the interaction suites. Six dependency-free contracts exercise the gate, including rejection of the previous sharded configuration. The native Polish assertion is still required.

The stacked symbol-editor run also found an earlier LED selection reading a newly recreated component before it had native bounds after Undo. The LED fixture now waits for positive component bounds before each selection or field input, in addition to waiting for its inspector fields. All existing assertions remain; this does not retry a pointer action or change the renderer.

Full-ICU configuration reference: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/features/globalization-icu-wasm.md
