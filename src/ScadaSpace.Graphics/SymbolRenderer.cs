using System.Globalization;
using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public enum SymbolKind { Tank, Gauge, Thermometer, LEDDisplay, Pipe, Pump, Valve, Motor, Rectangle, Ellipse, Line }
public sealed record SymbolData(double Value = 0, double Minimum = 0, double Maximum = 100, string Unit = "", string Color = "#48AFC0", string Format = "0.0", string Route = "elbow", double StrokeWidth = 1, string Stroke = "#9DAAB2", double Radius = 0, double Warning = 85, Quality Quality = Quality.Good);

public static class SymbolRenderer
{
    private static readonly SKColor Ink = SKColor.Parse("#385161");
    private static readonly SKColor Pale = SKColor.Parse("#E2EAEE");
    private static readonly SKColor Alert = SKColor.Parse("#D97855");
    public static void Draw(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        canvas.Save(); canvas.ClipRect(bounds); canvas.Translate(bounds.Left, bounds.Top);
        using var p = new CanvasPainter(canvas);
        var color = CanvasPainter.Color(data.Color);
        var f = data.Maximum > data.Minimum ? Math.Clamp((data.Value - data.Minimum) / (data.Maximum - data.Minimum), 0, 1) : 0;
        switch (kind)
        {
            case SymbolKind.Tank:
                canvas.Scale(bounds.Width / 200, bounds.Height / 240);
                Tank(p, data, (float)f, color); break;
            case SymbolKind.Gauge:
                canvas.Scale(bounds.Width / 220, bounds.Height / 150);
                Gauge(p, data, (float)f, color); break;
            case SymbolKind.Thermometer:
                canvas.Scale(bounds.Width / 100, bounds.Height / 220);
                Thermometer(p, data, (float)f, color); break;
            case SymbolKind.LEDDisplay:
                Led(p, bounds.Width, bounds.Height, data); break;
            case SymbolKind.Pipe:
                Pipe(p, bounds.Width, bounds.Height, data, color); break;
            case SymbolKind.Pump: case SymbolKind.Valve: case SymbolKind.Motor:
                canvas.Scale(bounds.Width / 120, bounds.Height / 100);
                Machine(p, kind, data.Value != 0, color); break;
            case SymbolKind.Rectangle:
                p.Rect(SKRect.Create(1, 1, bounds.Width - 2, bounds.Height - 2), color, (float)data.Radius);
                if (data.StrokeWidth > 0) p.Rect(SKRect.Create(1, 1, bounds.Width - 2, bounds.Height - 2), CanvasPainter.Color(data.Stroke), (float)data.Radius, true, (float)data.StrokeWidth);
                break;
            case SymbolKind.Ellipse:
                p.Ellipse(SKRect.Create(2, 2, bounds.Width - 4, bounds.Height - 4), color);
                if (data.StrokeWidth > 0) p.Ellipse(SKRect.Create(2, 2, bounds.Width - 4, bounds.Height - 4), CanvasPainter.Color(data.Stroke), true, (float)data.StrokeWidth);
                break;
            case SymbolKind.Line: p.Line(2, bounds.Height / 2, bounds.Width - 2, bounds.Height / 2, color, (float)data.StrokeWidth); break;
        }
        canvas.Restore();
        if (data.Quality != Quality.Good) DrawQuality(canvas, bounds, data.Quality);
    }
    private static void Tank(CanvasPainter p, SymbolData d, float fraction, SKColor color)
    {
        var body = SKRect.Create(22, 28, 141, 173);
        p.Gradient(body, SKColor.Parse("#C6D0D5"), SKColor.Parse("#F1F4F5"), SKColor.Parse("#BAC7CD"));
        p.Ellipse(SKRect.Create(22, 190, 141, 24), SKColor.Parse("#C0CCD2"));
        var y = 201 - fraction * 173;
        var liquid = d.Value >= d.Warning ? Alert : color;
        if (fraction > 0)
        {
            p.Rect(SKRect.Create(23, y, 139, 201 - y), liquid.WithAlpha(185));
            p.Ellipse(SKRect.Create(23, 190, 139, 23), liquid.WithAlpha(210));
            p.Ellipse(SKRect.Create(23, y - 10, 139, 20), liquid);
        }
        p.Ellipse(SKRect.Create(22, 17, 141, 22), SKColor.Parse("#E9EEF0"));
        p.Ellipse(SKRect.Create(22, 17, 141, 22), SKColor.Parse("#8A9BA5"), true, 1.2f);
        p.Line(22, 28, 22, 201, SKColor.Parse("#8A9BA5"), 1.2f); p.Line(163, 28, 163, 201, SKColor.Parse("#8A9BA5"), 1.2f);
        p.Arc(SKRect.Create(22, 189, 141, 25), 0, 180, SKColor.Parse("#8A9BA5"), 1.2f);
        for (var i = 0; i <= 4; i++) { var tick = 201 - i * 43.25f; p.Line(167, tick, 172, tick, Ink); p.Text((d.Minimum + (d.Maximum - d.Minimum) * i / 4).ToString("0", CultureInfo.InvariantCulture), 176, tick + 4, 10, Ink); }
        p.Text(d.Quality == Quality.Good ? d.Value.ToString("0.0", CultureInfo.InvariantCulture) + d.Unit : "—", 93, 118, 23, Ink, SKTextAlign.Center);
        p.Line(43, 218, 43, 229, Ink, 4); p.Line(141, 218, 141, 229, Ink, 4);
    }
    private static void Gauge(CanvasPainter p, SymbolData d, float fraction, SKColor color)
    {
        var arc = SKRect.Create(27, 20, 166, 166);
        p.Arc(arc, 180, 180, Pale, 13); p.Arc(arc, 180, 126, color, 13); p.Arc(arc, 306, 32, SKColor.Parse("#D9B75C"), 13); p.Arc(arc, 338, 22, SKColor.Parse("#D77870"), 13);
        for (var i = 0; i <= 10; i++)
        {
            var angle = Math.PI + i * Math.PI / 10;
            float X(float r) => 110 + (float)Math.Cos(angle) * r;
            float Y(float r) => 103 + (float)Math.Sin(angle) * r;
            p.Line(X(71), Y(71), X(77), Y(77), Ink);
            if (i % 2 == 0) p.Text((d.Minimum + (d.Maximum - d.Minimum) * i / 10).ToString("0", CultureInfo.InvariantCulture), X(98), Y(98) + 4, 10, Ink, SKTextAlign.Center);
        }
        var a = Math.PI + fraction * Math.PI;
        p.Line(110, 103, 110 + (float)Math.Cos(a) * 67, 103 + (float)Math.Sin(a) * 67, Ink, 3);
        p.Circle(110, 103, 6, Ink);
        p.Text(d.Quality == Quality.Good ? d.Value.ToString(d.Format, CultureInfo.InvariantCulture) + " " + d.Unit : "—", 110, 139, 18, Ink, SKTextAlign.Center);
    }
    private static void Thermometer(CanvasPainter p, SymbolData d, float fraction, SKColor color)
    {
        p.Rect(SKRect.Create(24, 12, 24, 174), SKColor.Parse("#F0F3F4"), 12); p.Rect(SKRect.Create(24, 12, 24, 174), Ink, 12, true, 1.5f);
        p.Circle(36, 184, 20, SKColor.Parse("#E7ECEF")); p.Circle(36, 184, 20, Ink, true, 1.5f);
        p.Rect(SKRect.Create(30, 175 - fraction * 155, 12, fraction * 155 + 10), color, 5); p.Circle(36, 184, 14, color);
        for (var i = 0; i <= 5; i++) { var y = 176 - i * 31; p.Line(52, y, 60, y, Ink); p.Text((d.Minimum + (d.Maximum - d.Minimum) * i / 5).ToString("0", CultureInfo.InvariantCulture), 64, y + 4, 11, Ink); }
        p.Text(d.Quality == Quality.Good ? d.Value.ToString("0.0", CultureInfo.InvariantCulture) + d.Unit : "—", 46, 217, 13, Ink, SKTextAlign.Center);
    }
    private static void Led(CanvasPainter p, float width, float height, SymbolData d)
    {
        p.Rect(SKRect.Create(0, 0, width, height), SKColor.Parse("#22332D"), 3);
        var text = d.Quality == Quality.Good ? d.Value.ToString(d.Format, CultureInfo.InvariantCulture) : "----";
        if (text.Length > 12) text = "----";
        var scale = Math.Min((width - 16) / Math.Max(1, text.Sum(ch => ch == '.' ? 12 : 30)), (height - (string.IsNullOrEmpty(d.Unit) ? 12 : 27)) / 48);
        p.Canvas.Save(); p.Canvas.Translate(8, 6); p.Canvas.Scale(Math.Max(0.1f, scale));
        foreach (var ch in text)
        {
            if (ch == '.') { p.Circle(4, 44, 2.5f, SKColor.Parse("#8CEE87")); p.Canvas.Translate(12, 0); continue; }
            var mask = ch switch { '0' => 0x3f, '1' => 0x06, '2' => 0x5b, '3' => 0x4f, '4' => 0x66, '5' => 0x6d, '6' => 0x7d, '7' => 0x07, '8' => 0x7f, '9' => 0x6f, '-' => 0x40, _ => 0 };
            (float X1, float Y1, float X2, float Y2)[] segments = [(6, 2, 21, 2), (24, 5, 24, 20), (24, 27, 24, 42), (6, 45, 21, 45), (3, 27, 3, 42), (3, 5, 3, 20), (6, 23, 21, 23)];
            for (var i = 0; i < segments.Length; i++) { var s = segments[i]; p.Line(s.X1, s.Y1, s.X2, s.Y2, SKColor.Parse(mask != 0 && (mask & (1 << i)) != 0 ? "#8CEE87" : "#30473B"), 3); }
            p.Canvas.Translate(30, 0);
        }
        p.Canvas.Restore();
        if (!string.IsNullOrEmpty(d.Unit)) p.Text(d.Unit, width - 8, height - 6, 12, SKColor.Parse("#AACCB7"), SKTextAlign.Right);
    }
    private static void Pipe(CanvasPainter p, float w, float h, SymbolData d, SKColor color)
    {
        var lineWidth = (float)Math.Clamp(d.StrokeWidth, 2, Math.Max(2, Math.Min(w, h) - 2));
        using var path = new SKPath();
        if (d.Route == "straight") { path.MoveTo(0, h / 2); path.LineTo(w, h / 2); }
        else { path.MoveTo(0, lineWidth); path.LineTo(w / 2, lineWidth); path.LineTo(w / 2, h - lineWidth); path.LineTo(w, h - lineWidth); }
        p.Path(path, SKColor.Parse("#8D9FA8"), true, lineWidth + 4);
        p.Path(path, d.Value != 0 ? color : Pale, true, lineWidth);
        if (d.Value != 0) p.Polygon(SKColors.White.WithAlpha(210), new(w - 22, h - (d.Route == "straight" ? h / 2 : lineWidth) - 3), new(w - 16, h - (d.Route == "straight" ? h / 2 : lineWidth)), new(w - 22, h - (d.Route == "straight" ? h / 2 : lineWidth) + 3));
    }
    private static void Machine(CanvasPainter p, SymbolKind kind, bool active, SKColor color)
    {
        var fill = active ? color : SKColor.Parse("#C4CED3");
        if (kind == SymbolKind.Valve)
        {
            p.Line(8, 55, 112, 55, Ink, 5); p.Polygon(fill, new(15, 31), new(59, 55), new(15, 79)); p.Polygon(fill, new(105, 31), new(61, 55), new(105, 79));
            p.Line(60, 20, 60, 55, Ink, 3); p.Line(39, 20, 81, 20, Ink, 5); p.Circle(60, 55, 5, Ink);
        }
        else if (kind == SymbolKind.Pump)
        {
            p.Rect(SKRect.Create(25, 81, 72, 9), Ink, 2); p.Line(32, 74, 32, 82, Ink, 4); p.Line(89, 74, 89, 82, Ink, 4);
            p.Rect(SKRect.Create(72, 21, 43, 22), fill, 2); p.Rect(SKRect.Create(72, 21, 43, 22), Ink, 2, true, 2);
            p.Circle(56, 49, 32, fill); p.Circle(56, 49, 32, Ink, true, 2); p.Polygon(SKColors.White.WithAlpha(220), new(41, 30), new(41, 68), new(73, 49));
            p.Line(7, 50, 23, 50, Ink, 9);
        }
        else
        {
            p.Rect(SKRect.Create(15, 22, 80, 56), fill, 7); p.Rect(SKRect.Create(15, 22, 80, 56), Ink, 7, true, 2);
            for (var i = 0; i < 6; i++) p.Line(30 + i * 10, 30, 30 + i * 10, 69, Ink.WithAlpha(130));
            p.Line(95, 50, 118, 50, Ink, 8); p.Rect(SKRect.Create(25, 80, 66, 8), Ink, 1);
        }
    }
    public static void DrawQuality(SKCanvas canvas, SKRect bounds, Quality quality)
    {
        canvas.Save(); canvas.ClipRect(bounds); using var p = new CanvasPainter(canvas);
        p.Rect(bounds, SKColors.White.WithAlpha(110));
        for (var x = bounds.Left - bounds.Height; x < bounds.Right; x += 18) p.Line(x, bounds.Bottom, x + bounds.Height, bounds.Top, SKColors.OrangeRed.WithAlpha(35), 4);
        p.Rect(SKRect.Create(bounds.Left + 2, bounds.Top + 2, Math.Min(150, bounds.Width - 4), 22), SKColor.Parse("#A64E35"), 2);
        p.Text(quality.ToString(), bounds.Left + 6, bounds.Top + 17, 11, SKColors.White); canvas.Restore();
    }
}
