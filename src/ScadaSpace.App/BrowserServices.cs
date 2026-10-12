using System.Text.Json;
using ScadaSpace.Core;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    private const string StorageKey = "scadaspace.project.v1";
    private static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScadaSpace");
    public static string Query(string key)
    {
#if __WASM__
        return Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get(" + JsonSerializer.Serialize(key) + ") || ''");
#else
        return "";
#endif
    }
    public static string? LoadProject()
    {
#if __WASM__
        return Uno.Foundation.WebAssemblyRuntime.InvokeJS("localStorage.getItem('" + StorageKey + "') || ''");
#else
        var path = Path.Combine(DataDirectory, "project.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
#endif
    }
    public static void SaveProject(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > ProjectCodec.MaximumBytes) throw new FormatException("Project exceeds the storage size limit.");
#if __WASM__
        var error = Uno.Foundation.WebAssemblyRuntime.InvokeJS("(() => { try { localStorage.setItem('" + StorageKey + "', " + JsonSerializer.Serialize(json) + "); return ''; } catch(e) { return String(e); } })()");
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
#else
        Directory.CreateDirectory(DataDirectory);
        var temp = Path.Combine(DataDirectory, "project.json.tmp"); File.WriteAllText(temp, json); File.Move(temp, Path.Combine(DataDirectory, "project.json"), true);
#endif
    }
    public static string Download(string filename, string text, string mime = "application/json")
    {
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("(() => { const blob = new Blob([" + JsonSerializer.Serialize(text) + "], {type:" + JsonSerializer.Serialize(mime) + "}); const url=URL.createObjectURL(blob); const a=document.createElement('a'); a.href=url; a.download=" + JsonSerializer.Serialize(filename) + "; document.body.appendChild(a); a.click(); a.remove(); setTimeout(()=>URL.revokeObjectURL(url),1000); return ''; })()");
        return "Downloaded " + filename;
#else
        var directory = Path.Combine(DataDirectory, "Exports"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(filename)); File.WriteAllText(path, text); return "Exported " + path;
#endif
    }
    public static bool BeginFileImport()
    {
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("(() => { document.getElementById('scada-import-file')?.remove(); const input=document.createElement('input'); input.id='scada-import-file'; input.type='file'; input.accept='.json,application/json'; input.style.display='none'; input.onchange=async()=>{ const f=input.files[0]; if(!f)return; if(f.size>8388608){window.scadaPendingImport={error:'File exceeds 8 MiB.'};return;} try {window.scadaPendingImport={text:await f.text()};}catch(e){window.scadaPendingImport={error:String(e)};} }; document.body.appendChild(input); input.click(); return ''; })()");
        return true;
#else
        return false;
#endif
    }
    public static string? TakeImport()
    {
#if __WASM__
        var json = Uno.Foundation.WebAssemblyRuntime.InvokeJS("(() => { if(!window.scadaPendingImport)return ''; const value=JSON.stringify(window.scadaPendingImport); delete window.scadaPendingImport; return value; })()");
        if (string.IsNullOrEmpty(json)) return null;
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
        return parsed.RootElement.GetProperty("text").GetString();
#else
        return null;
#endif
    }
    public static void SetTelemetry(object value)
    {
#if __WASM__
        PublishViewObservations();
        // Read-only observations for browser automation. No JS-to-managed command/evaluation backdoor is exposed.
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaApp=" + JsonSerializer.Serialize(value, ProjectCodec.Options) + "; document.documentElement.dataset.scadaspaceReady=window.scadaApp.ready?'true':'false'; '';");
#endif
    }
    public static void OpenSession()
    {
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.open(location.pathname+'?session=1','_blank','noopener'); '';");
#endif
    }
}
