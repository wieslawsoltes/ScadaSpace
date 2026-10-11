using System.Text.Json;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    public static void SetFontDiagnostics(string asset, string? family, string? error, float narrowAdvance, float wideAdvance)
    {
#if __WASM__
        // Read-only evidence from the actual loaded Skia typeface, not an adapter-capability guess.
        var json = JsonSerializer.Serialize(new { asset, family, error, loaded = family is not null && error is null, narrowAdvance, wideAdvance });
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaFont=" + json + "; '';");
#endif
    }
}
