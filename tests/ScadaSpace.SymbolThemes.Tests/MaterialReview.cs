using ScadaSpace.Core;
using ScadaSpace.Graphics;
using SkiaSharp;

internal static class MaterialReview
{
    private static void Assert(bool value, string message = "Material assertion failed")
    { if (!value) throw new Exception(message); }
    private static SKBitmap Raster(Action<SKCanvas> draw, int width = 200, int height = 240)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Transparent);
        var matrix = surface.Canvas.TotalMatrix; var count = surface.Canvas.SaveCount;
        draw(surface.Canvas);
        Assert(matrix == surface.Canvas.TotalMatrix && count == surface.Canvas.SaveCount, "Caller drawing state changed.");
        using var image = surface.Snapshot(); return SKBitmap.FromImage(image);
    }
    private static void Coverage(SKBitmap pixels, byte maximum)
    {
        Assert(pixels.Pixels.Any(p => p.Alpha == maximum), "No fully covered pixel has the configured alpha.");
        Assert(pixels.Pixels.All(p => p.Alpha <= maximum), "Overlapping fill passes increased alpha.");
    }
    public static void Run(Action<string, Action> check)
    {
        check("gradient shader alpha is applied exactly once", () => {
            using var pixels = Raster(c => { using var p = new CanvasPainter(c); var color = new SKColor(50, 100, 150, 128); p.Gradient(SKRect.Create(10, 10, 100, 100), color, color, color); });
            Assert(pixels.GetPixel(60, 60).Alpha == 128); Coverage(pixels, 128);
        });
        check("gradient brushes do not leak into the next solid primitive", () => {
            using var pixels = Raster(c => { using var p = new CanvasPainter(c); var color = new SKColor(50, 100, 150, 128); p.Gradient(SKRect.Create(0, 0, 180, 200), color, color, color); p.Rect(SKRect.Create(30, 30, 100, 100), SKColors.Blue); });
            Assert(pixels.GetPixel(60, 60) == SKColors.Blue);
        });
        foreach (var alpha in new byte[] { 255, 128, 32 })
            check("liquid surface and body use single coverage at alpha " + alpha, () => {
                using var pixels = Raster(c => { using var p = new CanvasPainter(c); p.LiquidColumn(SKRect.Create(10, 70, 160, 130), 12, new SKColor(40, 120, 170, alpha), .5); });
                var expected = (byte)Math.Round(alpha * .5); Coverage(pixels, expected);
                Assert(pixels.GetPixel(90, 72).Alpha == expected && pixels.GetPixel(90, 120).Alpha == expected);
            });
        check("zero liquid opacity includes surface sheen and waterline", () => {
            using var pixels = Raster(c => { using var p = new CanvasPainter(c); p.LiquidColumn(SKRect.Create(10, 70, 160, 130), 12, SKColors.Blue, 0); });
            Assert(pixels.Pixels.All(p => p.Alpha == 0));
        });
        check("Mimic material does not composite a second undercoat", () => {
            var options = new SymbolOptions { Orientation = "vertical", Appearance = "mimic", DisplayFillLevel = false, DisplayAgitator = false, DisplayStand = false,
                StateStyle = new(PrimaryColor: "#80336699", StrokeColor: "#00000000") };
            using var pixels = Raster(c => SymbolRenderer.Draw(c, SKRect.Create(200, 240), SymbolKind.Vessel, new(Value: 45, Options: options)));
            Assert(pixels.GetPixel(100, 130).Alpha == 128); Coverage(pixels, 128);
        });
        check("cylindrical tank opacity controls the entire liquid silhouette", () => {
            var data = new SymbolData(Value: 45, Color: "#336699", StrokeWidth: 0, Tank: new(TankColor: "#00000000", TankWarningColor: "#00000000", LiquidOpacity: .5, WarningThreshold: 100, ShowValue: false, ShowScale: false));
            using var pixels = Raster(c => SymbolRenderer.Draw(c, SKRect.Create(200, 240), SymbolKind.Tank, data));
            Assert(pixels.GetPixel(100, 170).Alpha == 128); Coverage(pixels, 128);
        });
        foreach (var mode in new[] { "mimic", "simple", "p&id" })
            check("vessel liquid alpha composes correctly in " + mode, () => {
                var options = new SymbolOptions { Orientation = "vertical", Appearance = mode, DisplayAgitator = false, DisplayStand = false, LiquidOpacity = .5,
                    StateStyle = new(PrimaryColor: "#00000000", TertiaryColor: "#80336699", StrokeColor: "#00000000") };
                using var pixels = Raster(c => SymbolRenderer.Draw(c, SKRect.Create(200, 240), SymbolKind.Vessel, new(Value: 45, Options: options)));
                Coverage(pixels, 64);
            });
        check("alternate translucent state pictures retain distinct exact alpha", () => {
            var options = new SymbolOptions { State = "service", Orientation = "vertical", Appearance = "mimic", DisplayFillLevel = false, DisplayAgitator = false, DisplayStand = false, AnimationSpeed = 100,
                StateStyle = new(PrimaryColor: "#80336699", SecondaryColor: "#40336699", StrokeColor: "#00000000", EnableFlashing: true) };
            var data = new SymbolData(Value: 45, Options: options); using var cache = new SymbolRenderCache();
            using var first = Raster(c => cache.Draw(c, SKRect.Create(200, 240), SymbolKind.Vessel, data, 0));
            using var second = Raster(c => cache.Draw(c, SKRect.Create(200, 240), SymbolKind.Vessel, data, .75));
            Assert(first.GetPixel(100, 130).Alpha == 128 && second.GetPixel(100, 130).Alpha == 64);
            Assert(cache.PictureBuildCount == 1 && cache.RetainedPictureCount == 2);
        });
        check("liquid helper rejects invalid opacity without changing canvas state", () => {
            using var pixels = Raster(c => {
                using var p = new CanvasPainter(c);
                foreach (var opacity in new[] { double.NaN, double.PositiveInfinity, -.1, 1.1 })
                {
                    var rejected = false;
                    try { p.LiquidColumn(SKRect.Create(10, 70, 160, 130), 12, SKColors.Blue, opacity); }
                    catch (ArgumentException) { rejected = true; }
                    Assert(rejected);
                }
                p.LiquidColumn(SKRect.Empty, 12, SKColors.Blue, .5);
            });
            Assert(pixels.Pixels.All(p => p.Alpha == 0));
        });
        check("render transparent material review contact sheet", () => {
            using var surface = SKSurface.Create(new SKImageInfo(1000, 300));
            using var painter = new CanvasPainter(surface.Canvas);
            for (var y = 0; y < 300; y += 12) for (var x = 0; x < 1000; x += 12)
                painter.Rect(SKRect.Create(x, y, 12, 12), ((x + y) / 12) % 2 == 0 ? SKColors.White : SKColor.Parse("#E0E0E0"));
            var kinds = new[] { SymbolKind.Pump, SymbolKind.Motor, SymbolKind.Valve, SymbolKind.Sensor, SymbolKind.Vessel };
            for (var i = 0; i < kinds.Length; i++)
                SymbolRenderer.Draw(surface.Canvas, SKRect.Create(i * 200 + 10, 10, 180, 280), kinds[i], new(Value: 45, Options: new() {
                    State = "service", StateStyle = new(PrimaryColor: "#80336699", TertiaryColor: "#8044BBCC"), Orientation = kinds[i] == SymbolKind.Vessel ? "vertical" : "right" }));
            using var image = surface.Snapshot(); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes("TestResults/symbol-themes/translucent-materials.png", bytes.ToArray());
        });
    }
}
