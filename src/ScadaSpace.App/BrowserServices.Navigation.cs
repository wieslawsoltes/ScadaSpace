using System.Text.Json;
using ScadaSpace.Runtime;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    public static string InitialPageUrl()
    {
#if __WASM__
        return Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaNavigation.initial()");
#else
        return "/";
#endif
    }
    public static (Guid Entry, string Url)? TakePageNavigation()
    {
#if __WASM__
        var text = Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaNavigation.take()");
        if (string.IsNullOrEmpty(text)) return null;
        using var json = JsonDocument.Parse(text);
        Guid.TryParse(json.RootElement.GetProperty("entry").GetString(), out var entry);
        return (entry, json.RootElement.GetProperty("url").GetString() ?? "/");
#else
        return null;
#endif
    }
    public static void PublishPageNavigation(NavigationChange change, string fallbackTitle)
    {
#if __WASM__
        var payload = JsonSerializer.Serialize(new { kind = change.Kind.ToString(), entry = change.Current.EntryId.ToString(), url = change.Current.Address.Url, delta = change.Delta, title = string.IsNullOrEmpty(change.Current.Page.Title) ? fallbackTitle : change.Current.Page.Title });
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaNavigation.publish(" + payload + "); '';");
#endif
    }
}
