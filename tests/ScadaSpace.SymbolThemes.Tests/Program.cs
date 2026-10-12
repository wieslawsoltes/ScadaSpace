using System.Security.Cryptography;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Graphics;
using SkiaSharp;

Directory.CreateDirectory("TestResults/symbol-themes");
var results = new List<object>(); var failures = 0;
void Check(string name, Action action)
{
    try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + " " + error); }
}
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
var primary = new SymbolStateStyle("#BE45A8", "#D8BC55", "#4C9DD1", "#174FA2", false, true);
SymbolData Data(SymbolKind kind, string mode = "simple", SymbolStateStyle? style = null) => new(Value: 45, Options: new()
{ Appearance = mode, State = "service", Orientation = kind == SymbolKind.Vessel ? "vertical" : "right", StateStyle = style ?? primary, AnimationSpeed = 100 });
SKBitmap Pixels(SymbolKind kind, SymbolData data, SymbolRenderCache cache, double time)
{
    using var surface = SKSurface.Create(new SKImageInfo(240, 240)); surface.Canvas.Clear(SKColors.White);
    var count = surface.Canvas.SaveCount; var matrix = surface.Canvas.TotalMatrix;
    cache.Draw(surface.Canvas, SKRect.Create(10, 10, 220, 220), kind, data, time);
    Assert(count == surface.Canvas.SaveCount && matrix == surface.Canvas.TotalMatrix, "State drawing leaked caller canvas state.");
    using var image = surface.Snapshot(); return SKBitmap.FromImage(image);
}
string Hash(SymbolKind kind, SymbolData data, SymbolRenderCache cache, double time)
{ using var pixels = Pixels(kind, data, cache, time); return Convert.ToHexString(SHA256.HashData(pixels.Bytes)); }
foreach (var kind in new[] { SymbolKind.Pump, SymbolKind.Motor, SymbolKind.Valve, SymbolKind.Sensor, SymbolKind.Vessel })
    foreach (var mode in new[] { "mimic", "simple", "p&id" })
        Check(kind + " " + mode + " applies both retained state fills", () =>
        {
            using var cache = new SymbolRenderCache(); var data = Data(kind, mode);
            Assert(Hash(kind, data, cache, 0) != Hash(kind, data, cache, .75));
            Assert(cache.PictureBuildCount == 1 && cache.RetainedPictureCount == 2);
            using var pixels = Pixels(kind, data, cache, 0);
            Assert(pixels.Pixels.Count(p => p == SKColor.Parse(primary.PrimaryColor)) > 10, "Primary fill missing.");
            Assert(pixels.Pixels.Any(p => p == SKColor.Parse(primary.StrokeColor)), "Stroke color missing.");
        });
Check("state tertiary color changes actual vessel liquid pixels", () =>
{
    using var a = new SymbolRenderCache(); using var b = new SymbolRenderCache();
    Assert(Hash(SymbolKind.Vessel, Data(SymbolKind.Vessel), a, 0) != Hash(SymbolKind.Vessel, Data(SymbolKind.Vessel, style: primary with { TertiaryColor = "#EEA322" }), b, 0));
});
Check("flashing and mechanical animation are independent", () =>
{
    using var cache = new SymbolRenderCache(); var flash = Data(SymbolKind.Pump);
    Assert(!flash.Options!.IsAnimated && flash.Options.IsFlashing);
    Assert(Hash(SymbolKind.Pump, flash, cache, .1) == Hash(SymbolKind.Pump, flash, cache, .2));
    var motion = Data(SymbolKind.Pump, style: primary with { EnableAnimation = true, EnableFlashing = false });
    Assert(Hash(SymbolKind.Pump, motion, cache, .1) != Hash(SymbolKind.Pump, motion, cache, .2)); Assert(cache.RetainedPictureCount == 1);
});
Check("zero speed and bad quality hold primary state without retaining alternate pictures", () =>
{
    using var cache = new SymbolRenderCache(); var stopped = Data(SymbolKind.Motor); stopped = stopped with { Options = stopped.Options! with { AnimationSpeed = 0 } };
    Assert(Hash(SymbolKind.Motor, stopped, cache, 0) == Hash(SymbolKind.Motor, stopped, cache, .75)); Assert(cache.RetainedPictureCount == 1);
    var bad = Data(SymbolKind.Motor) with { Quality = Quality.Stale };
    Assert(Hash(SymbolKind.Motor, bad, cache, 0) == Hash(SymbolKind.Motor, bad, cache, .75)); Assert(cache.RetainedPictureCount == 1);
});
Check("profile replacement invalidates pictures once and Clear releases both", () =>
{
    using var cache = new SymbolRenderCache(); var d = Data(SymbolKind.Pump); Hash(SymbolKind.Pump, d, cache, 0);
    var changed = d with { Options = d.Options! with { StateStyle = primary with { PrimaryColor = "#112244" } } };
    Hash(SymbolKind.Pump, changed, cache, 0); Assert(cache.PictureBuildCount == 2); cache.Clear(); Assert(cache.RetainedPictureCount == 0);
});
Check("presentation cache invalidates catalog and light-dark theme while retaining immutable prior results", () =>
{
    var c = SymbolStateCatalog.Empty.With("service", new(primary, primary with { PrimaryColor = "#123456" }, new[] { "Pump" }));
    Func<string, Scalar> read = key => key == "state" ? Scalar.From("service") : Scalar.Null;
    var cache = new SymbolPresentationCache(); var light = cache.Read("Pump", read, new(), states: c);
    Assert(ReferenceEquals(light, cache.Read("Pump", read, new(), states: c)));
    var dark = cache.Read("Pump", read, new(Theme: "dark"), states: c); Assert(dark.StateStyle!.PrimaryColor == "#123456" && light.StateStyle!.PrimaryColor == "#BE45A8");
    var held = cache.Read("Pump", read, new(ReducedMotion: true), states: c); Assert(!held.NeedsAnimation);
    var changed = c.With("service", c.Get("service").WithTheme("light", primary with { EnableFlashing = false }));
    Assert(!cache.Read("Pump", read, new(), states: changed).NeedsAnimation);
});
Check("flash phase is bounded at one cycle per second or slower", () =>
{
    var o = Data(SymbolKind.Motor).Options!; Assert(!o.AlternateFill(0) && !o.AlternateFill(.49) && o.AlternateFill(.5) && !o.AlternateFill(1));
    var half = o with { AnimationSpeed = 50 }; Assert(!half.AlternateFill(.9) && half.AlternateFill(1));
});
Check("flashing playback does not allocate pictures or brushes per frame", () =>
{
    using var cache = new SymbolRenderCache(); using var surface = SKSurface.Create(new SKImageInfo(240, 240)); var data = Data(SymbolKind.Pump); var bounds = SKRect.Create(220, 220);
    for (var i = 0; i < 100; i++) cache.Draw(surface.Canvas, bounds, SymbolKind.Pump, data, i / 30d);
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) cache.Draw(surface.Canvas, bounds, SymbolKind.Pump, data, i / 30d);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    var report = new { iterations = 1000, bytesPerDraw = allocated / 1000d, pictureBuilds = cache.PictureBuildCount, retainedPictures = cache.RetainedPictureCount, note = "Raster managed allocation fixture, not GPU or whole-application FPS." };
    var json = JsonSerializer.Serialize(report); Console.WriteLine("THEME_PERFORMANCE " + json); File.WriteAllText("TestResults/symbol-themes/performance.json", json);
    Assert(allocated < 128000 && cache.PictureBuildCount == 1 && cache.RetainedPictureCount == 2);
});
Check("render primary and alternate theme contact sheet", () =>
{
    using var typeface = SKTypeface.FromFamilyName("DejaVu Sans"); CanvasPainter.DrawingTypeface = typeface;
    using var surface = SKSurface.Create(new SKImageInfo(1200, 520)); surface.Canvas.Clear(SKColors.White);
    var kinds = new[] { SymbolKind.Pump, SymbolKind.Motor, SymbolKind.Valve, SymbolKind.Sensor, SymbolKind.Vessel };
    for (var row = 0; row < 2; row++) for (var col = 0; col < kinds.Length; col++)
    { using var cache = new SymbolRenderCache(); cache.Draw(surface.Canvas, SKRect.Create(col * 240 + 12, row * 260 + 12, 216, 236), kinds[col], Data(kinds[col]), row * .75); }
    using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes("TestResults/symbol-themes/state-themes.png", png.ToArray()); CanvasPainter.DrawingTypeface = null;
});
var summary = JsonSerializer.Serialize(new { tests = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText("TestResults/symbol-themes/results.json", summary);
Console.WriteLine($"RESULT {results.Count - failures}/{results.Count} symbol theme checks passed");
return failures == 0 ? 0 : 1;
