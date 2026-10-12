using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class IndustrialSymbolRenderer
{
    private static void Pump(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o); var mimic = o.Appearance == "mimic";
        if (o.Feet != "hidden")
        {
            var save = p.Canvas.Save();
            try
            {
                p.Canvas.RotateDegrees(Rotation(o.Feet) - 90 - Rotation(o.Orientation), 50, 50);
                using var stand = new SKPath(); stand.MoveTo(28, 72); stand.LineTo(21, 90); stand.LineTo(78, 90); stand.LineTo(72, 73); stand.Close(); Shape(p, stand, c, false);
            }
            finally { p.Canvas.RestoreToCount(save); }
        }
        if (o.Variant == "vacuum")
        {
            if (o.Appearance == "p&id")
            { Box(p, SKRect.Create(4, 36, 18, 30), c, 2); Box(p, SKRect.Create(78, 36, 18, 30), c, 2); p.Circle(50, 50, 36, Shell); p.Circle(50, 50, 36, c, true, 2); }
            else
            {
                Box(p, SKRect.Create(25, 13, 43, 49), c, 6, mimic); Box(p, SKRect.Create(24, 65, 47, 19), c, 2, mimic);
                Box(p, SKRect.Create(69, 68, 18, 15), c); Flange(p, 88, 60, c);
                for (var i = 0; i < (mimic ? 8 : 3); i++) p.Line(31 + i * (mimic ? 4 : 12), 24, 31 + i * (mimic ? 4 : 12), 57, c, mimic ? .8f : 3);
                p.Line(30, 20, 63, 20, c); p.Line(36, 66, 36, 84, c); p.Line(60, 66, 60, 84, c);
            }
            return;
        }
        using var volute = new SKPath(); volute.MoveTo(51, 13); volute.CubicTo(20, 7, 6, 34, 15, 61); volute.CubicTo(21, 82, 56, 89, 71, 67);
        volute.LineTo(76, 51); volute.LineTo(94, 51); volute.LineTo(94, 17); volute.LineTo(75, 17); volute.LineTo(72, 22); volute.Close(); Shape(p, volute, c, mimic);
        if (mimic)
        {
            Flange(p, 90, 17, c); p.Circle(45, 47, 29, Shell); p.Circle(45, 47, 29, c, true, 1.4f); p.Circle(45, 47, 19, c, true, 1);
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4; var x = 45 + (float)Math.Cos(a) * 24; var y = 47 + (float)Math.Sin(a) * 24;
                p.Circle(x, y, 1.8f, c, true, .8f); p.Line(45 + (float)Math.Cos(a) * 30, 47 + (float)Math.Sin(a) * 30, 45 + (float)Math.Cos(a) * 35, 47 + (float)Math.Sin(a) * 35, c, 1.4f);
            }
        }
        else if (o.Appearance == "simple") p.Circle(45, 47, 23, c, true, 1.8f);
    }
    private static void Motor(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o);
        if (o.Appearance == "p&id")
        { p.Line(6, 50, 94, 50, c, 2); p.Circle(50, 50, 32, Shell); p.Circle(50, 50, 32, c, true, 2); p.Text("M", 50, 61, 32, c, SKTextAlign.Center); return; }
        Box(p, SKRect.Create(10, 80, 66, 7), c); Box(p, SKRect.Create(22, 69, 8, 12), c); Box(p, SKRect.Create(62, 69, 8, 12), c);
        Box(p, SKRect.Create(72, 40, 24, 14), c, 1, o.Appearance == "mimic");
        Box(p, SKRect.Create(14, 23, 62, 51), c, 6, o.Appearance == "mimic");
        Box(p, SKRect.Create(5, 28, 14, 42), c, 5, o.Appearance == "mimic");
        Box(p, SKRect.Create(37, 13, 26, 11), c, 2);
        for (var i = 0; i < (o.Appearance == "mimic" ? 9 : 4); i++) p.Line(25 + i * (o.Appearance == "mimic" ? 5 : 12), 29, 25 + i * (o.Appearance == "mimic" ? 5 : 12), 68, c, o.Appearance == "mimic" ? .8f : 2);
    }
}
