using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class IndustrialSymbolRenderer
{
    private static void Valve(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o); p.Line(3, 58, 97, 58, c, 3);
        using var valve = new SKPath(); valve.MoveTo(16, 38); valve.LineTo(84, 78); valve.LineTo(84, 38); valve.LineTo(16, 78); valve.Close(); Shape(p, valve, c, o.Appearance == "mimic");
        if (o.State == "closed" || o.State == "failedToOpen") p.Line(45, 44, 55, 72, c, 5);
        if (o.State == "partiallyClosed") p.Line(46, 53, 54, 63, c, 4);
        p.Line(50, 18, 50, 56, c, 2);
        if (o.Appearance == "mimic")
        { p.Ellipse(SKRect.Create(32, 9, 36, 12), Shell); p.Ellipse(SKRect.Create(32, 9, 36, 12), c, true, 2); p.Line(33, 15, 67, 15, c); Flange(p, 8, 43, c); Flange(p, 87, 43, c); Box(p, SKRect.Create(44, 25, 12, 8), c); }
        else if (o.Appearance == "p&id") { p.Circle(50, 18, 11, Shell); p.Circle(50, 18, 11, c, true, 1.5f); p.Line(43, 18, 57, 18, c); }
        else { p.Line(34, 18, 66, 18, c, 3); }
        var x = o.ReverseFlow ? 35f : 65f; var direction = o.ReverseFlow ? -1 : 1;
        p.Line(35, 88, 65, 88, c, 1.3f); p.Line(x, 88, x - 6 * direction, 84, c); p.Line(x, 88, x - 6 * direction, 92, c);
    }
    private static void Sensor(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o);
        if (o.Appearance == "p&id")
        { p.Line(58, 50, 95, 50, c, 2); p.Circle(35, 50, 28, Shell); p.Circle(35, 50, 28, c, true, 2); p.Line(7, 50, 63, 50, c); p.Text("S", 35, 46, 22, c, SKTextAlign.Center); return; }
        Box(p, SKRect.Create(63, 43, 32, 14), c, 2, o.Appearance == "mimic");
        Box(p, SKRect.Create(51, 35, 13, 30), c, 2, o.Appearance == "mimic");
        Box(p, SKRect.Create(9, 26, 43, 48), c, 6, o.Appearance == "mimic");
        Box(p, SKRect.Create(16, 17, 13, 10), c, 2); p.Line(21, 16, 21, 7, c, 2);
        Box(p, SKRect.Create(18, 36, 24, 19), c, 2); p.Text("S", 30, 50, 13, c, SKTextAlign.Center);
        if (o.Appearance == "mimic") for (var i = 0; i < 5; i++) p.Line(55, 39 + i * 5, 61, 39 + i * 5, c, .7f);
        p.Circle(30, 64, 3, o.IsRunning ? SKColor.Parse("#47AA61") : c);
    }
}
