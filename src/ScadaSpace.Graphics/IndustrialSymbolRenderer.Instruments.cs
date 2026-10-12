using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class IndustrialSymbolRenderer
{
    private static void Valve(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o);
        if (o.Appearance == "p&id")
        {
            using var bow = new SKPath(); bow.MoveTo(12, 33); bow.LineTo(87, 67); bow.LineTo(87, 33); bow.LineTo(12, 67); bow.Close();
            Shape(p, bow, c, false);
        }
        else
        {
            using var body = new SKPath(); body.MoveTo(15, 38); body.LineTo(32, 41); body.QuadTo(50, 26, 68, 41);
            body.LineTo(87, 38); body.LineTo(87, 62); body.LineTo(68, 59); body.QuadTo(50, 74, 32, 59); body.LineTo(15, 62); body.Close();
            Shape(p, body, c, o.Appearance == "mimic");
            Box(p, SKRect.Create(11, 33, 5, 34), c); Box(p, SKRect.Create(87, 33, 5, 34), c);
            Box(p, SKRect.Create(42, 22, 17, 30), c, 0, o.Appearance == "mimic");
            p.Line(47, 21, 47, 9, c, 1.3f); p.Line(54, 21, 54, 9, c, 1.3f);
            if (o.Appearance == "mimic")
            {
                Box(p, SKRect.Create(40, 26, 21, 7), c); p.Line(40, 29, 61, 29, c, .7f);
                for (var i = 0; i < 4; i++) { p.Line(38 + i * 7, 23, 38 + i * 7, 35, c, .8f); }
            }
            using var wheel = new SKPath(); wheel.MoveTo(32, 5); wheel.LineTo(68, 5);
            for (var i = 0; i < 6; i++) wheel.QuadTo(67 - i * 6, 12, 62 - i * 6, 9);
            wheel.LineTo(32, 5); wheel.Close(); Shape(p, wheel, c, false);
        }
        if (o.State is "closed" or "failedToOpen") p.Line(46, 39, 54, 61, c, 3);
        if (o.State == "partiallyClosed") p.Line(47, 45, 53, 55, c, 3);
        // Direction is retained as a small independent flow annotation, not a false connection to pipes.
        var x = o.ReverseFlow ? 37f : 63f; var direction = o.ReverseFlow ? -1 : 1;
        p.Line(37, 81, 63, 81, c, .9f); p.Line(x, 81, x - 5 * direction, 78, c, .9f); p.Line(x, 81, x - 5 * direction, 84, c, .9f);
    }
    private static void Sensor(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o);
        if (o.Appearance == "p&id")
        { p.Line(61, 50, 94, 50, c, 1.5f); p.Circle(35, 50, 28, Shell); p.Circle(35, 50, 28, c, true, 1.3f); p.Line(7, 50, 63, 50, c); p.Text("S", 35, 46, 21, c, SKTextAlign.Center); return; }
        var mimic = o.Appearance == "mimic";
        Box(p, SKRect.Create(67, 43, 27, 13), c, 2, mimic);
        using var nut = new SKPath(); nut.MoveTo(53, 37); nut.LineTo(63, 32); nut.LineTo(69, 37); nut.LineTo(69, 62); nut.LineTo(63, 67); nut.LineTo(53, 62); nut.Close(); Shape(p, nut, c, mimic);
        Box(p, SKRect.Create(8, 24, 44, 51), c, 7, mimic); Box(p, SKRect.Create(17, 15, 13, 10), c, 2);
        p.Line(23, 14, 23, 5, c, 2); Box(p, SKRect.Create(16, 34, 27, 21), c, 2);
        p.Text("S", 30, 49, 13, c, SKTextAlign.Center);
        if (mimic)
        {
            for (var i = 0; i < 6; i++) p.Line(73 + i * 3, 45, 73 + i * 3, 54, c, .65f);
            p.Line(57, 37, 57, 62, c, .8f); p.Line(65, 37, 65, 62, c, .8f);
            foreach (var x in new[] { 14f, 45f }) { p.Circle(x, 30, 1, c); p.Circle(x, 68, 1, c); }
        }
        p.Circle(30, 65, 2.5f, o.IsRunning ? SKColor.Parse("#47AA61") : c);
    }
}
