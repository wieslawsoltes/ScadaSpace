using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Graphics;
using SkiaSharp;

var results = new List<object>(); var failures = 0;
Directory.CreateDirectory("TestResults/symbols");
void Check(string name, Action test)
{
    try { test(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } catch (FormatException) { return; } throw new Exception("Expected invalid render input to fail."); }
SymbolData Data(SymbolKind kind, string appearance = "mimic", string? state = null) => new(Value: 45, Options: IndustrialSymbolRenderer.Supports(kind)
    ? new SymbolOptions { Appearance = appearance, Orientation = kind == SymbolKind.Vessel ? "vertical" : "right", State = state ?? "default" } : null);
string Image(SymbolKind kind, SymbolData data, SymbolRenderCache? cache = null, double time = 0)
{
    using var surface = SKSurface.Create(new SKImageInfo(240, 240)); surface.Canvas.Clear(SKColors.White);
    if (cache is null) SymbolRenderer.Draw(surface.Canvas, SKRect.Create(10, 10, 220, 220), kind, data);
    else cache.Draw(surface.Canvas, SKRect.Create(10, 10, 220, 220), kind, data, time);
    using var image = surface.Snapshot(); using var pixels = image.Encode(SKEncodedImageFormat.Png, 100);
    return Convert.ToHexString(SHA256.HashData(pixels.ToArray()));
}
foreach (var kind in Enum.GetValues<SymbolKind>())
    Check(kind + " renders clipped geometry without changing caller canvas state", () =>
    {
        using var surface = SKSurface.Create(new SKImageInfo(256, 256)); var canvas = surface.Canvas; canvas.Clear(SKColors.White);
        canvas.Translate(3, 4); var matrix = canvas.TotalMatrix; var saved = canvas.SaveCount;
        SymbolRenderer.Draw(canvas, SKRect.Create(10, 10, 180, 180), kind, Data(kind));
        Assert(canvas.TotalMatrix == matrix && canvas.SaveCount == saved);
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image); Assert(bitmap.GetPixel(0, 0) == SKColors.White && bitmap.GetPixel(255, 255) == SKColors.White);
    });
foreach (var kind in new[] { SymbolKind.Pump, SymbolKind.Motor, SymbolKind.Valve, SymbolKind.Sensor, SymbolKind.Vessel })
    Check(kind + " has distinct Mimic Simple and P&ID geometry", () => Assert(new[] { "mimic", "simple", "p&id" }.Select(mode => Image(kind, Data(kind, mode))).Distinct().Count() == 3));
Check("centrifugal and vacuum pump variants use different geometry", () => {
    var d=Data(SymbolKind.Pump); Assert(Image(SymbolKind.Pump,d)!=Image(SymbolKind.Pump,d with { Options=d.Options! with { Variant="vacuum" } }));
});
Check("machine orientation and valve reverse flow affect actual rendered pixels", () => {
    var d=Data(SymbolKind.Motor);Assert(Image(SymbolKind.Motor,d)!=Image(SymbolKind.Motor,d with {Options=d.Options! with {Orientation="top"}}));
    d=Data(SymbolKind.Valve);Assert(Image(SymbolKind.Valve,d)!=Image(SymbolKind.Valve,d with {Options=d.Options! with {ReverseFlow=true}}));
});
Check("states distinguish open closed partial and failed valves", () => {
    Assert(new[]{"open","closed","partiallyClosed","failedToOpen"}.Select(s=>Image(SymbolKind.Valve,Data(SymbolKind.Valve,state:s))).Distinct().Count()==4);
});
Check("horizontal and vertical vessels show distinct fills and optional structures", () => {
    var d=Data(SymbolKind.Vessel);var hash=Image(SymbolKind.Vessel,d);
    Assert(hash!=Image(SymbolKind.Vessel,d with {Value=0}));Assert(hash!=Image(SymbolKind.Vessel,d with {Options=d.Options! with {DisplayFillLevel=false}}));
    Assert(hash!=Image(SymbolKind.Vessel,d with {Options=d.Options! with {DisplayStand=false,DisplayAgitator=false}}));
    Assert(hash!=Image(SymbolKind.Vessel,d with {Options=d.Options! with {Orientation="horizontal"}}));
});
Check("industrial drawings preserve aspect while annotations remain outside body", () => {
    var o=new SymbolOptions{Label=new("P-101","top"),Readout=new("50%","bottom")};var bounds=SKRect.Create(400,160);
    var layout=IndustrialSymbolRenderer.Arrange(bounds,o);var body=IndustrialSymbolRenderer.BodyBounds(bounds,SymbolKind.Pump,o);
    Assert(body.Width==body.Height && layout.Label.Bottom<=body.Top && layout.Readout.Top>=body.Bottom);
});
Check("unchanged cached pictures are pixel equivalent and built once", () => {
    foreach(var kind in Enum.GetValues<SymbolKind>())
    { using var cache=new SymbolRenderCache();var data=Data(kind);Assert(Image(kind,data)==Image(kind,data,cache));Image(kind,data,cache);Assert(cache.PictureBuildCount==1); }
});
Check("cached symbol drawing has bounded idle managed allocations", () => {
    using var cache=new SymbolRenderCache();using var surface=SKSurface.Create(new SKImageInfo(256,256));var data=Data(SymbolKind.Pump);var bounds=SKRect.Create(220,220);
    for(var i=0;i<50;i++)cache.Draw(surface.Canvas,bounds,SymbolKind.Pump,data);
    var bytes=GC.GetAllocatedBytesForCurrentThread();var start=Stopwatch.GetTimestamp();
    for(var i=0;i<1000;i++)cache.Draw(surface.Canvas,bounds,SymbolKind.Pump,data);
    var elapsed=Stopwatch.GetElapsedTime(start);var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
    for(var i=0;i<30;i++)SymbolRenderer.Draw(surface.Canvas,bounds,SymbolKind.Pump,data);
    var referenceBytes=GC.GetAllocatedBytesForCurrentThread();var referenceStart=Stopwatch.GetTimestamp();
    for(var i=0;i<1000;i++)SymbolRenderer.Draw(surface.Canvas,bounds,SymbolKind.Pump,data);
    var referenceElapsed=Stopwatch.GetElapsedTime(referenceStart);var referenceAllocated=GC.GetAllocatedBytesForCurrentThread()-referenceBytes;
    File.WriteAllText("TestResults/symbols/performance.json",JsonSerializer.Serialize(new{iterations=1000,bytesPerDraw=allocated/1000d,microsecondsPerDraw=elapsed.TotalMicroseconds/1000,
        referenceBytesPerDraw=referenceAllocated/1000d,referenceMicrosecondsPerDraw=referenceElapsed.TotalMicroseconds/1000,pictures=cache.PictureBuildCount,
        note="Same-data raster Skia playback versus immediate drawing after warmup; not whole-app or physical-GPU speed."}));
    Assert(allocated<referenceAllocated/4,"Cached drawing must allocate less than the equivalent immediate drawing.");
    Assert(allocated<128000 && cache.PictureBuildCount==1,"Idle rendering allocated or rebuilt geometry.");
});
Check("animation changes pixels without rebuilding the static picture", () => {
    using var cache=new SymbolRenderCache();var d=Data(SymbolKind.Pump,state:"running");d=d with {Options=d.Options! with {AnimationSpeed=50}};
    Assert(Image(SymbolKind.Pump,d,cache,0)!=Image(SymbolKind.Pump,d,cache,.3));Assert(cache.PictureBuildCount==1);
    var held=d with{Options=d.Options! with{AnimationSpeed=0}};Assert(Image(SymbolKind.Pump,held,cache,0)==Image(SymbolKind.Pump,held,cache,100));
});
Check("value size appearance and quality independently invalidate pictures", () => {
    using var cache=new SymbolRenderCache();var d=Data(SymbolKind.Vessel);Image(SymbolKind.Vessel,d,cache);Image(SymbolKind.Vessel,d with {Value=70},cache);
    Image(SymbolKind.Vessel,d with {Quality=Quality.Stale},cache);Image(SymbolKind.Vessel,d with{Options=d.Options! with{Appearance="simple"}},cache);Assert(cache.PictureBuildCount==4);
    cache.Clear();Assert(!cache.HasPicture);Image(SymbolKind.Vessel,d,cache);Assert(cache.PictureBuildCount==5);
});
Check("invalid numeric input format and state leave canvas transform intact", () => {
    using var surface=SKSurface.Create(new SKImageInfo(300,300));var c=surface.Canvas;c.Translate(3,4);var m=c.TotalMatrix;var saved=c.SaveCount;
    foreach(var d in new[]{new SymbolData(Value:double.NaN),new SymbolData(Format:"Q"),new SymbolData(Minimum:5,Maximum:2),new SymbolData(Options:new(){State="bogus"})})
    { Throws(()=>SymbolRenderer.Draw(c,SKRect.Create(200,200),SymbolKind.Gauge,d));Assert(c.TotalMatrix==m&&c.SaveCount==saved); }
    SymbolRenderer.Draw(c,SKRect.Create(200,200),SymbolKind.Gauge,new(Value:0,Minimum:-double.MaxValue,Maximum:double.MaxValue));Assert(c.SaveCount==saved);
});
Check("bad-quality symbols do not show valid measurement annotations or animate", () => {
    using var cache=new SymbolRenderCache();var d=Data(SymbolKind.Vessel,state:"running") with {Quality=Quality.Stale};d=d with{Options=d.Options! with {AnimationSpeed=50}};
    Assert(Image(SymbolKind.Vessel,d,cache,0)==Image(SymbolKind.Vessel,d,cache,10));Assert(Image(SymbolKind.Vessel,d)!=Image(SymbolKind.Vessel,d with {Quality=Quality.Good}));
});
Check("tank capacity percent opacity and strict warning boundary are rendered", () => {
    var normal=new SymbolData(Value:50,Maximum:200,Tank:new(WarningThreshold:25,ValueFormat:"Percent"));
    Assert(Image(SymbolKind.Tank,normal)!=Image(SymbolKind.Tank,normal with {Value=51}));
    Assert(Image(SymbolKind.Tank,normal)!=Image(SymbolKind.Tank,normal with {Tank=normal.Tank! with {LiquidOpacity=0}}));
    Assert(Image(SymbolKind.Tank,normal)!=Image(SymbolKind.Tank,normal with {Tank=normal.Tank! with {ShowValue=false}}));
});
Check("quality overlays handle tiny and distant finite bounds without leaking canvas state", () => {
    using var surface=SKSurface.Create(new SKImageInfo(32,32));var c=surface.Canvas;var saved=c.SaveCount;
    foreach(var bounds in new[]{SKRect.Create(1,1),SKRect.Create(1e10f,1e10f,4096,4096),SKRect.Empty})
    { SymbolRenderer.DrawQuality(c,bounds,Quality.Stale);Assert(c.SaveCount==saved); }
    Throws(()=>SymbolRenderer.DrawQuality(c,new SKRect(float.NaN,0,10,10),Quality.Stale));
    Throws(()=>SymbolRenderer.Draw(c,SKRect.Create(100,100),SymbolKind.Tank,new(Minimum:-10,Maximum:0)));
});
Check("render all industrial appearance and state contact sheets", () => {
    var kinds=new[]{SymbolKind.Pump,SymbolKind.Pump,SymbolKind.Motor,SymbolKind.Valve,SymbolKind.Sensor,SymbolKind.Vessel};
    using var surface=SKSurface.Create(new SKImageInfo(1200,840));surface.Canvas.Clear(SKColors.White);
    for(var row=0;row<3;row++) for(var col=0;col<kinds.Length;col++)
    {
        var kind=kinds[col];var mode=new[]{"mimic","simple","p&id"}[row];var d=Data(kind,mode);
        d=d with { Options=d.Options! with {Variant=col==1?"vacuum":"centrifugal",Label=new(col==1?"Vacuum pump":kind.ToString()),Readout=new(mode,"bottom",FontSize:12)} };
        SymbolRenderer.Draw(surface.Canvas,SKRect.Create(col*200+8,row*280+8,184,260),kind,d);
    }
    using var image=surface.Snapshot();using var bytes=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes("TestResults/symbols/industrial-gallery.png",bytes.ToArray());
});
File.WriteAllText("TestResults/symbols/results.json",JsonSerializer.Serialize(new {tests=results.Count,failures,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{results.Count-failures}/{results.Count} graphics checks passed");
return failures==0?0:1;
