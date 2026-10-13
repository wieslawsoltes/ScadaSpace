# LED validation review

Native tests first exposed selection before the rebuilt inspector had arranged dimensions. The fixture now waits for the selected component's usable native bounds and editable format selector before one pointer action. No assertions or product input routes were bypassed.

## Actual globalization defect and root cause

Both numeric LED controls correctly received `pl-PL`, completed rendering, and still displayed English punctuation. German formatting worked. The downloaded loader selected `globalizationMode: sharded`, using partial ICU files.

The first two attempts set `WasmIncludeFullIcuData` conditionally and then unconditionally. Neither fixed the package: the evaluated flag was already true while the published mode remained sharded. The earlier explanation that framework conditions or nested publish evaluation were responsible was not supported by the resulting artifact.

The .NET 10 browser SDK's own `IcuTests.FullIcuFromRuntimePackWithInvariant` uses **BlazorWebAssemblyLoadAllGlobalizationData**, even for `Template.WasmBrowser`. Its comment links runtime issue #94133 about the incomplete build-property rename. ScadaSpace now sets that consumed property instead of the ineffective Wasm-named switch. This is build configuration, not locale-specific replacement of formatted strings.

The existing post-publication gate still requires mode `all`, one full `icudt.dat` entry, and actual file bytes matching the manifest SHA-256. Its six contract tests remain. Native US/German/Polish rendering assertions remain required. Full locale data increases download size; the report records uncompressed and available compressed bytes. Reusable browser hosts must also provide suitable ICU data.

## References

- https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/Wasm.Build.Tests/IcuTests.cs
- https://github.com/dotnet/runtime/issues/94133

Success is established only by the matching published manifest and executed browser assertions, not by a project property or desktop compilation.
