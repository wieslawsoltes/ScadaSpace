using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class IndustrialSymbolRenderer
{
    private static SKPath VesselPath(bool horizontal, string appearance)
    {
        var path = new SKPath();
        if (appearance != "mimic") { path.AddRoundRect(SKRect.Create(10, 9, 80, 77), appearance == "p&id" ? 0 : 3, appearance == "p&id" ? 0 : 3); return path; }
        if (horizontal)
        { path.MoveTo(17, 12); path.LineTo(83, 12); path.CubicTo(98, 12, 98, 85, 83, 85); path.LineTo(17, 85); path.CubicTo(2, 85, 2, 12, 17, 12); }
        else
        { path.MoveTo(10, 15); path.CubicTo(10, 5, 90, 5, 90, 15); path.LineTo(90, 79); path.CubicTo(90, 90, 10, 90, 10, 79); }
        path.Close(); return path;
    }
    private static void Vessel(CanvasPainter p, SymbolOptions o, SymbolData data)
    {
        var c = StateColor(o); var horizontal = o.Orientation == "horizontal";
        using var body = VesselPath(horizontal, o.Appearance); Shape(p, body, c, o.Appearance == "mimic");
        var fraction = (float)SymbolPresentation.Fraction(data.Value, data.Minimum, data.Maximum);
        var fluid = Color(data.Value >= data.Warning ? o.LiquidWarningColor : o.LiquidColor);
        if (o.DisplayFillLevel && o.LiquidOpacity > 0 && fraction > 0 && data.Quality == Quality.Good)
        {
            var save = p.Canvas.Save();
            try
            {
                p.Canvas.ClipPath(body, antialias: true); var top = 85 - fraction * 76;
                var alpha = (byte)Math.Round(fluid.Alpha * o.LiquidOpacity);
                p.Rect(SKRect.Create(5, top, 90, 88 - top), fluid.WithAlpha(alpha));
                if (!horizontal && o.Appearance == "mimic") p.Ellipse(SKRect.Create(10, top - 4, 80, 8), fluid.WithAlpha(alpha));
                else p.Line(7, top, 93, top, fluid, 1);
            }
            finally { p.Canvas.RestoreToCount(save); }
        }
        p.Path(body, c, true, 1.5f);
        if (o.Appearance == "mimic")
        {
            if (horizontal) { p.Ellipse(SKRect.Create(9, 12, 16, 73), c, true, .8f); p.Arc(SKRect.Create(75, 12, 16, 73), -90, 180, c, .7f); }
            else { p.Ellipse(SKRect.Create(10, 9, 80, 11), c, true, .8f); for (var i = 0; i < 8; i++) p.Line(78, 26 + i * 7, 89, 24 + i * 7, c, .7f); }
        }
        if (o.Appearance == "simple")
        {
            p.Rect(SKRect.Create(12, 11, 76, 73), c, 2, true, .65f);
            for (var i = 0; i < 4; i++) { p.Line(9, 20 + i * 17, 11, 20 + i * 17, c, 1.5f); p.Line(89, 20 + i * 17, 91, 20 + i * 17, c, 1.5f); }
        }
        if (o.DisplayStand) { p.Line(18, 84, 18, 96, c, 2); p.Line(82, 84, 82, 96, c, 2); if (horizontal) { p.Line(24, 85, 24, 95, c); p.Line(76, 85, 76, 95, c); } }
        if (o.DisplayAgitator) { p.Line(49, 9, 49, 76, c, .8f); p.Line(51, 9, 51, 76, c, .8f); }
    }
}
