using System.Globalization;
using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class SymbolRenderer
{
    private static void Tank(CanvasPainter p, SymbolData data, float fraction, SKColor color)
    {
        var o = data.Tank ?? new();
        var warning = o.WarningThreshold is { } threshold ? data.Value / data.Maximum * 100 > threshold : data.Value >= data.Warning;
        var shell = IndustrialSymbolRenderer.Color(warning ? o.TankWarningColor : o.TankColor);
        var liquid = warning ? IndustrialSymbolRenderer.Color(o.LiquidWarningColor) : color;
        var right = o.ShowScale ? 160f : 178f;
        var body = SKRect.Create(22, 26, right - 22, 179);
        using var silhouette = new SKPath();
        silhouette.MoveTo(22, 26); silhouette.CubicTo(22, 10, right, 10, right, 26);
        silhouette.LineTo(right, 197); silhouette.CubicTo(right, 215, 22, 215, 22, 197); silhouette.Close();
        var save = p.Canvas.Save();
        try
        {
            p.Canvas.ClipPath(silhouette, antialias: true);
            p.Gradient(SKRect.Create(22, 10, right - 22, 205), shell, Blend(shell, SKColors.White, .8f), shell);
            if (fraction > 0 && data.Quality == Quality.Good)
            {
                var y = 205 - fraction * 181;
                var fill = liquid.WithAlpha((byte)Math.Round(liquid.Alpha * o.LiquidOpacity));
                p.Rect(SKRect.Create(22, y, right - 22, 215 - y), fill);
                p.Ellipse(SKRect.Create(22, y - 8, right - 22, 16), fill);
                p.Arc(SKRect.Create(22, y - 8, right - 22, 16), 0, 180, liquid, .8f);
            }
        }
        finally { p.Canvas.RestoreToCount(save); }
        var stroke = CanvasPainter.Color(data.Stroke); var width = (float)data.StrokeWidth;
        if (width > 0) { p.Path(silhouette, stroke, true, width); p.Ellipse(SKRect.Create(22, 16, right - 22, 19), stroke, true, width); }
        if (o.ShowScale) for (var i = 0; i <= 4; i++)
        {
            var y = 202 - i * 44.5f; p.Line(165, y, 171, y, Ink, .8f);
            p.Text((data.Minimum * (1 - i / 4d) + data.Maximum * (i / 4d)).ToString("0.##", CultureInfo.InvariantCulture), 175, y + 4, 10, Ink);
        }
        if (o.ShowValue)
        {
            var text = data.Quality != Quality.Good ? "—" : o.ValueFormat switch
            {
                "Integer" => NumberFormatting.Format(data.Value, "0"),
                "Percent" => NumberFormatting.Format(SymbolPresentation.Fraction(data.Value, data.Minimum, data.Maximum) * 100, "0.#") + "%",
                "Currency" => NumberFormatting.Format(data.Value, "C2"),
                _ => NumberFormatting.Format(data.Value, data.Format)
            };
            var unit = o.ResolveUnit(data.Unit);
            if (unit.Length > 0) text = o.UnitPrefix ? unit + " " + text : text + " " + unit;
            p.Text(text, (right + 22) / 2, 122, 20, Ink, SKTextAlign.Center);
        }
    }
    private static SKColor Blend(SKColor a, SKColor b, float t) => new((byte)(a.Red * (1-t) + b.Red * t), (byte)(a.Green * (1-t) + b.Green * t), (byte)(a.Blue * (1-t) + b.Blue * t), a.Alpha);
}
