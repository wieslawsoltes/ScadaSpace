using System.Text.Json;
using ScadaSpace.Core;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    /// <summary>Passive native dialog observations only; no setter or command dispatcher.</summary>
    internal static void PublishSymbolStateEditor(object? value)
    {
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaSymbolEditor=" + JsonSerializer.Serialize(value, ProjectCodec.Options) + "; '';");
#endif
    }
}
