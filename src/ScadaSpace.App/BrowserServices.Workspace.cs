using System.Text.Json;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    public static string? LoadWorkspace()
    {
#if __WASM__
        return Uno.Foundation.WebAssemblyRuntime.InvokeJS("(()=>{try{return localStorage.getItem('scadaspace.workspace.v1') || '';}catch{return '';}})()");
#else
        var path=Path.Combine(DataDirectory,"workspace.json");return File.Exists(path)?File.ReadAllText(path):null;
#endif
    }
    public static void SaveWorkspace(string json)
    {
        if(json.Length>4096)throw new FormatException("Workspace settings exceed 4 KiB.");
#if __WASM__
        var error=Uno.Foundation.WebAssemblyRuntime.InvokeJS("(()=>{try{localStorage.setItem('scadaspace.workspace.v1',"+JsonSerializer.Serialize(json)+");return '';}catch(e){return String(e);}})()");
        if(!string.IsNullOrEmpty(error))throw new IOException(error);
#else
        Directory.CreateDirectory(DataDirectory);var path=Path.Combine(DataDirectory,"workspace.json");File.WriteAllText(path+".tmp",json);File.Move(path+".tmp",path,true);
#endif
    }
}
