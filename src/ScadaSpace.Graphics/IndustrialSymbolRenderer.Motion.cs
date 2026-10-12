using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class IndustrialSymbolRenderer
{
    /// <summary>Small moving overlay; caller may retain painter paints while static geometry stays in a picture.</summary>
    public static void DrawMotion(CanvasPainter p, SKRect bounds, SymbolKind kind, SymbolData data, double seconds)
    {
        if (data.Quality != Quality.Good) return;
        var o = data.Options ?? new SymbolOptions { Orientation = kind == SymbolKind.Vessel ? "vertical" : "right", State = data.Value != 0 ? kind == SymbolKind.Valve ? "open" : "running" : kind == SymbolKind.Valve ? "closed" : "stopped" };
        var save = p.Canvas.Save();
        try
        {
            p.Canvas.ClipRect(bounds); if (!BeginBody(p.Canvas, bounds, kind, o)) return;
            var phase = o.Phase(seconds); var c = StateColor(o);
            if (kind == SymbolKind.Pump && o.Variant == "centrifugal" && o.Appearance != "p&id")
            {
                p.Canvas.RotateDegrees((float)(phase * 360), 45, 47);
                var blades = o.Appearance == "mimic" ? 8 : 6;
                for (var i = 0; i < blades; i++)
                {
                    p.Curve(50, 46, 57, 37, 53, 28, 44, 26, c, o.Appearance == "mimic" ? 1 : 1.8f);
                    p.Canvas.RotateDegrees(360f / blades, 45, 47);
                }
                p.Circle(45, 47, 5, Shell); p.Circle(45, 47, 5, c, true, 1.5f);
            }
            else if (kind == SymbolKind.Vessel && o.DisplayAgitator)
            {
                var span = o.IsAnimated ? 4 + 12 * (float)Math.Abs(Math.Cos(phase * Math.PI * 2)) : 15;
                p.Ellipse(SKRect.Create(50 - span, 71, span, 6), Shell); p.Ellipse(SKRect.Create(50 - span, 71, span, 6), c, true, 1);
                p.Ellipse(SKRect.Create(50, 71, span, 6), Shell); p.Ellipse(SKRect.Create(50, 71, span, 6), c, true, 1);
            }
            else if (o.IsRunning)
            {
                // A restrained travelling state indicator avoids full-area flashing.
                var x = 30 + (float)phase * 40; p.Circle(x, 94, 2, c);
            }
        }
        finally { p.Canvas.RestoreToCount(save); }
    }
}
