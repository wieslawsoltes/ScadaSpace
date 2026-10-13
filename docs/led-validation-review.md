# LED validation review

The first complete LED browser run passed all eleven pre-existing suites and its first five LED assertions. The next selector click observed the new component selection before the rebuilt Property Editor had native dimensions. The saved final observation has the correctly arranged number-format selector and no application errors. The fixture now waits for positive native selector dimensions and editability for that same selected component before clicking once. No fixed delay, retry, assertion removal or product command bypass is used.

## Browser globalization data

The next run passed all eleven existing suites and the LED native editing, caching and German-locale assertions. Polish locale then revealed a product packaging defect: both actual displays held `locale: pl-PL` but formatted the number with English punctuation. This was not a failed binding or an unfinished drawing; both controls had successfully rendered the wrong cultural format with no diagnostic. The WebAssembly default startup-culture ICU subset did not contain Polish data.

The browser target now sets the .NET WebAssembly SDK's `WasmIncludeFullIcuData` property. This makes dynamic locale selection use the included full ICU data rather than silently falling back to the startup subset. Desktop builds remain unchanged. It increases the initial globalization payload in exchange for the advertised dynamic-locale functionality; no locale-specific string replacement or relaxed browser assertion is used. Reusable library hosts likewise need appropriate globalization data.

Primary build-property reference: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/build/WasmApp.Common.targets
