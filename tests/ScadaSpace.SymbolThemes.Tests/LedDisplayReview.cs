using System.Security.Cryptography;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Graphics;
using SkiaSharp;

internal static class LedDisplayReview
{
    private static void Assert(bool condition, string message="LED rendering assertion failed") { if(!condition)throw new Exception(message); }
    private static SKBitmap Raster(LedDisplaySnapshot display, Quality quality=Quality.Good, int width=360, int height=100, string unit="")
    {
        using var surface=SKSurface.Create(new SKImageInfo(width,height));surface.Canvas.Clear(SKColors.Transparent);
        var saved=surface.Canvas.SaveCount;var matrix=surface.Canvas.TotalMatrix;
        LedDisplayRenderer.Draw(surface.Canvas,SKRect.Create(6,6,width-12,height-12),display,quality,unit);
        Assert(saved==surface.Canvas.SaveCount && matrix==surface.Canvas.TotalMatrix,"LED rendering changed caller state.");
        using var image=surface.Snapshot();return SKBitmap.FromImage(image);
    }
    private static string Hash(LedDisplaySnapshot snapshot,Quality quality=Quality.Good)
    { using var pixels=Raster(snapshot,quality);return Convert.ToHexString(SHA256.HashData(pixels.Bytes)); }
    public static void Run(Action<string,Action> check)
    {
        check("seven and fourteen segment graphics have distinct real diode geometry",()=>{
            var seven=LedDisplayPresentation.Create(Scalar.From(88.88),new("7 segment"));var fourteen=seven with {Options=seven.Options with {SegmentFormat="14 segment"}};
            Assert(Hash(seven)!=Hash(fourteen));
        });
        check("all fourteen segment alphabet characters paint nonblank lit geometry",()=>{
            foreach(var ch in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                var snapshot=LedDisplayPresentation.Create(Scalar.From(ch.ToString()),new(BackgroundColor:"#00000000",DiodeOnColor:"#224466",DiodeOffColor:"#00000000"));
                using var pixels=Raster(snapshot);Assert(pixels.Pixels.Count(p=>p==SKColor.Parse("#224466"))>10,"Blank letter "+ch);
            }
        });
        check("decimal comma colon percent plus and space produce distinct readouts",()=>{
            var hashes=new HashSet<string>();foreach(var text in new[]{"12.3","12,3","12:3","12%3","12+3","12 3"}) hashes.Add(Hash(LedDisplayPresentation.Create(Scalar.From(text),new())));
            Assert(hashes.Count==6);
        });
        check("LED configured background lit and unlit colors reach actual pixels",()=>{
            using var pixels=Raster(LedDisplayPresentation.Create(Scalar.From(12),new(BackgroundColor:"#112233",DiodeOnColor:"#44CC88",DiodeOffColor:"#553355")));
            foreach(var color in new[]{"#112233","#44CC88","#553355"})Assert(pixels.Pixels.Contains(SKColor.Parse(color)),"Missing "+color);
            Assert(pixels.GetPixel(0,0).Alpha==0);
        });
        check("LED diode transparency is applied once without an opaque underlay",()=>{
            using var pixels=Raster(LedDisplayPresentation.Create(Scalar.From("18+,%%"),new("7 segment",BackgroundColor:"#00000000",DiodeOnColor:"#80224466",DiodeOffColor:"#00000000")));
            Assert(pixels.Pixels.Any(p=>p.Alpha==128));Assert(pixels.Pixels.All(p=>p.Alpha<=128));
        });
        check("LED bad quality hides measurement data behind the same diagnostic dashes",()=>{
            var first=LedDisplayPresentation.Create(Scalar.From(12),new());var other=LedDisplayPresentation.Create(Scalar.From(98),new());
            Assert(Hash(first,Quality.Stale)==Hash(other,Quality.Stale));Assert(Hash(first)!=Hash(first,Quality.Stale));
        });
        check("LED handles tiny wide and tall viewports without painting outside bounds",()=>{
            var value=LedDisplayPresentation.Create(Scalar.From(-12345.67),new());
            foreach(var size in new[]{(18,18),(700,55),(80,300),(360,100)})
            {using var pixels=Raster(value,width:size.Item1,height:size.Item2,unit:"m³/h");Assert(pixels.GetPixel(0,0).Alpha==0 && pixels.GetPixel(size.Item1-1,size.Item2-1).Alpha==0);}
        });
        check("LED numeric dependency updates reformat without changing an explicit string",()=>{
            var numeric=LedDisplayPresentation.Create(Scalar.From(12.34),new());
            var updated=LedDisplayRenderer.Resolve(new(Value:25,Led:numeric));Assert(updated.Text=="25.00");Assert(numeric.Text=="12.34");
            var text=LedDisplayPresentation.Create(Scalar.From("RUN"),new());Assert(ReferenceEquals(text,LedDisplayRenderer.Resolve(new(Value:20,Led:text))));
        });
        check("LED cached graphics are pixel equivalent and build once for unchanged data",()=>{
            using var cache=new SymbolRenderCache();using var surface=SKSurface.Create(new SKImageInfo(400,120));var bounds=SKRect.Create(400,120);
            var data=new SymbolData(Value:12.34,Led:LedDisplayPresentation.Create(Scalar.From(12.34),new()));
            cache.Draw(surface.Canvas,bounds,SymbolKind.LEDDisplay,data);using var cached=surface.Snapshot();
            surface.Canvas.Clear(SKColors.Transparent);SymbolRenderer.Draw(surface.Canvas,bounds,SymbolKind.LEDDisplay,data);using var immediate=surface.Snapshot();
            using var a=SKBitmap.FromImage(cached);using var b=SKBitmap.FromImage(immediate);Assert(a.Bytes.SequenceEqual(b.Bytes));
            for(var i=0;i<100;i++)cache.Draw(surface.Canvas,bounds,SymbolKind.LEDDisplay,data);
            var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)cache.Draw(surface.Canvas,bounds,SymbolKind.LEDDisplay,data);
            var allocated=GC.GetAllocatedBytesForCurrentThread()-before;Assert(allocated<128000 && cache.PictureBuildCount==1 && cache.RetainedPictureCount==1);
            File.WriteAllText("TestResults/symbol-themes/led-performance.json",JsonSerializer.Serialize(new{iterations=1000,bytesPerDraw=allocated/1000d,pictures=cache.PictureBuildCount,note="Warm raster managed allocations, not GPU FPS."}));
        });
        check("render LED display variants locales and alphanumeric contact sheet",()=>{
            using var surface=SKSurface.Create(new SKImageInfo(1100,540));surface.Canvas.Clear(SKColor.Parse("#EDF0F3"));
            var entries=new[]{("7 segment",Scalar.From(12345.67),"en-US"),("14 segment",Scalar.From(12345.67),"de-DE"),("14 segment",Scalar.From("RUN P-101"),"en-US"),("14 segment",Scalar.From("ALARM 12:34"),"en-US"),("14 segment",Scalar.From(12345.67),"pl-PL"),("7 segment",Scalar.From(-123.45),"en-US")};
            for(var i=0;i<entries.Length;i++){var e=entries[i];LedDisplayRenderer.Draw(surface.Canvas,SKRect.Create(20+i%2*550,20+i/2*180,510,140),LedDisplayPresentation.Create(e.Item2,new(e.Item1,Locale:e.Item3)),unit:i==4?"m³/h":"");}
            using var image=surface.Snapshot();using var png=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes("TestResults/symbol-themes/led-displays.png",png.ToArray());
        });
    }
}
