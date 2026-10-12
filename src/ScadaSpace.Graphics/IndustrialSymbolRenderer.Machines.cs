using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class IndustrialSymbolRenderer
{
    private static void PumpFeet(CanvasPainter p, SymbolOptions o, SKColor color)
    {
        if (o.Feet == "hidden" || o.Variant == "vacuum") return;
        var save = p.Canvas.Save();
        try
        {
            p.Canvas.RotateDegrees(Rotation(o.Feet) - 90 - Rotation(o.Orientation), 50, 50);
            using var feet = new SKPath();
            if (o.Appearance == "p&id")
            { feet.MoveTo(25, 71); feet.LineTo(18, 91); feet.LineTo(79, 91); feet.LineTo(73, 71); feet.Close(); }
            else
            {
                feet.MoveTo(25, 72); feet.LineTo(18, 87); feet.LineTo(14, 89); feet.LineTo(14, 92); feet.LineTo(26, 92); feet.LineTo(34, 77); feet.Close();
                feet.MoveTo(61, 77); feet.LineTo(69, 92); feet.LineTo(82, 92); feet.LineTo(82, 89); feet.LineTo(77, 87); feet.LineTo(71, 73); feet.Close();
            }
            Shape(p, feet, color, false);
        }
        finally { p.Canvas.RestoreToCount(save); }
    }
    private static void Pump(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o); var mimic = o.Appearance == "mimic"; PumpFeet(p, o, c);
        if (o.Variant == "vacuum") { VacuumPump(p, o, c); return; }
        using var volute = new SKPath();
        if (o.Appearance == "p&id")
        {
            volute.MoveTo(43, 17); volute.LineTo(94, 17); volute.LineTo(94, 46); volute.LineTo(73, 46);
            volute.CubicTo(71, 81, 35, 94, 17, 68); volute.CubicTo(0, 43, 19, 17, 43, 17); volute.Close();
        }
        else
        {
            volute.MoveTo(48, 11); volute.CubicTo(20, 8, 5, 30, 11, 56); volute.CubicTo(16, 82, 46, 89, 65, 72);
            volute.CubicTo(73, 65, 76, 55, 76, 48); volute.LineTo(93, 48); volute.LineTo(93, 17);
            volute.LineTo(82, 17); volute.LineTo(81, 23); volute.CubicTo(69, 25, 64, 13, 48, 11); volute.Close();
        }
        Shape(p, volute, c, mimic);
        if (mimic)
        {
            Flange(p, 91, 17, c); p.Circle(45, 47, 29, Shell); p.Circle(45, 47, 29, c, true, 1.1f); p.Circle(45, 47, 19, c, true, 1);
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4; var x = 45 + (float)Math.Cos(a) * 24; var y = 47 + (float)Math.Sin(a) * 24;
                p.Circle(x, y, 1.8f, c, true, .8f); p.Line(45 + (float)Math.Cos(a) * 30, 47 + (float)Math.Sin(a) * 30, 45 + (float)Math.Cos(a) * 35, 47 + (float)Math.Sin(a) * 35, c, 1.4f);
            }
        }
        else if (o.Appearance == "simple") p.Circle(45, 47, 22, c, true, 1.8f);
    }
    private static void VacuumPump(CanvasPainter p, SymbolOptions o, SKColor c)
    {
        if (o.Appearance == "p&id")
        {
            Box(p, SKRect.Create(4, 36, 18, 30), c, 2); Box(p, SKRect.Create(78, 36, 18, 30), c, 2);
            p.Circle(50, 50, 38, Shell); p.Circle(50, 50, 38, c, true, 1.8f); return;
        }
        if (o.Appearance == "simple")
        {
            using var outline = new SKPath(); outline.MoveTo(31, 10); outline.LineTo(56, 10); outline.QuadTo(65, 10, 65, 20);
            outline.LineTo(65, 54); outline.QuadTo(65, 64, 76, 64); outline.LineTo(82, 64); outline.LineTo(82, 56); outline.LineTo(88, 56);
            outline.LineTo(88, 94); outline.LineTo(82, 94); outline.LineTo(82, 85); outline.LineTo(74, 85);
            outline.QuadTo(66, 85, 65, 91); outline.LineTo(27, 91); outline.QuadTo(22, 91, 22, 85); outline.LineTo(22, 62);
            outline.LineTo(27, 62); outline.LineTo(27, 19); outline.QuadTo(27, 10, 31, 10); outline.Close(); Shape(p, outline, c, false);
            p.Rect(SKRect.Create(35, 19, 8, 48), c); p.Rect(SKRect.Create(52, 19, 6, 48), c);
            p.Line(36, 20, 56, 20, c, 2); Box(p, SKRect.Create(32, 67, 30, 15), c);
            for (var i = 0; i < 5; i++) p.Line(34 + i * 5, 79, 39 + i * 5, 70, c, 2);
            p.Line(65, 20, 74, 20, c, 2); p.Line(74, 20, 79, 12, c, 2); return;
        }
        Box(p, SKRect.Create(21, 8, 44, 46), c, 6, true);
        p.Line(22, 19, 65, 19, c); p.Line(22, 22, 65, 22, c, .8f);
        for (var i = 0; i < 11; i++) p.Line(24 + i * 3.7f, 23, 24 + i * 3.7f, 52, c, .65f);
        Box(p, SKRect.Create(65, 27, 9, 24), c); Box(p, SKRect.Create(74, 30, 6, 16), c);
        using var outlet = new SKPath(); outlet.MoveTo(44, 58); outlet.LineTo(54, 58); outlet.LineTo(54, 74);
        outlet.QuadTo(56, 85, 72, 85); outlet.LineTo(89, 85); outlet.LineTo(89, 94); outlet.LineTo(67, 94);
        outlet.QuadTo(44, 92, 44, 78); outlet.Close(); Shape(p, outlet, c, true);
        Box(p, SKRect.Create(22, 58, 44, 24), c, 2, true); Box(p, SKRect.Create(87, 62, 6, 34), c);
        Box(p, SKRect.Create(39, 61, 7, 5), c); Box(p, SKRect.Create(39, 74, 7, 5), c); p.Line(42, 66, 42, 74, c);
    }
    private static void Motor(CanvasPainter p, SymbolOptions o)
    {
        var c = StateColor(o);
        if (o.Appearance == "p&id")
        { p.Circle(50, 50, 37, Shell); p.Circle(50, 50, 37, c, true, 1.3f); p.Text("M", 50, 61, 30, c, SKTextAlign.Center); return; }
        using var stand = new SKPath(); stand.MoveTo(27, 72); stand.LineTo(23, 89); stand.LineTo(16, 89); stand.LineTo(16, 93);
        stand.LineTo(76, 93); stand.LineTo(76, 89); stand.LineTo(69, 89); stand.LineTo(66, 72); stand.Close(); Shape(p, stand, c, false);
        var mimic = o.Appearance == "mimic";
        Box(p, SKRect.Create(77, 43, 18, 9), c, 0, mimic);
        Box(p, SKRect.Create(14, 18, 65, 61), c, 6, mimic);
        Box(p, SKRect.Create(8, 18, 20, 61), c, 7, mimic);
        if (mimic)
        {
            Box(p, SKRect.Create(41, 10, 19, 8), c, 1); p.Line(41, 13, 60, 13, c, .7f);
            Box(p, SKRect.Create(27, 19, 45, 59), c, 0, true);
            for (var i = 0; i < 15; i++) p.Line(30, 23 + i * 3.6f, 69, 23 + i * 3.6f, c, .65f);
            p.Line(30, 19, 30, 78, c, .7f); p.Line(70, 19, 70, 78, c, .7f); Box(p, SKRect.Create(29, 44, 4, 10), c);
        }
        else
        {
            for (var i = 0; i < 8; i++) p.Line(29, 24 + i * 7, 77, 24 + i * 7, c, 1.8f);
            p.Line(29, 19, 29, 78, c, 1.8f);
        }
    }
}
