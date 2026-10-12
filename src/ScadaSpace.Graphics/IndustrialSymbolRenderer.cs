using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public readonly record struct SymbolArrangement(SKRect Body, SKRect Label, SKRect Readout);

/// <summary>Independent vector drawings of the documented industrial families. Normalized geometry, gravity-aligned liquid, upright text.</summary>
public static partial class IndustrialSymbolRenderer
{
    private static readonly SKColor Outline = SKColor.Parse("#606060"), Shell = SKColor.Parse("#F4F4F4"), Metal = SKColor.Parse("#BEBEBE");
    public static bool Supports(SymbolKind kind) => kind is SymbolKind.Pump or SymbolKind.Motor or SymbolKind.Valve or SymbolKind.Sensor or SymbolKind.Vessel;
    public static SKColor Color(string text) { var c = StyleColor.Parse(text); return new(c.Red, c.Green, c.Blue, c.Alpha); }
    public static SymbolArrangement Arrange(SKRect bounds, SymbolOptions options)
    {
        var body = bounds; body.Inflate(-3, -3);
        SKRect Reserve(SymbolText text)
        {
            if (text.Text.Length == 0 || text.Location == "hidden") return SKRect.Empty;
            var height = Math.Min((float)text.FontSize + 5, Math.Max(1, body.Height * .25f));
            var width = Math.Min(100, Math.Max(1, body.Width * .28f)); SKRect slot;
            switch (text.Location)
            {
                case "top": slot = SKRect.Create(body.Left, body.Top, body.Width, height); body.Top += height; break;
                case "bottom": slot = SKRect.Create(body.Left, body.Bottom - height, body.Width, height); body.Bottom -= height; break;
                case "left": slot = SKRect.Create(body.Left, body.Top, width, body.Height); body.Left += width; break;
                default: slot = SKRect.Create(body.Right - width, body.Top, width, body.Height); body.Right -= width; break;
            }
            return slot;
        }
        var label = Reserve(options.Label); var readout = Reserve(options.Readout); return new(body, label, readout);
    }
    public static SKRect BodyBounds(SKRect bounds, SymbolKind kind, SymbolOptions options)
    {
        var area = Arrange(bounds, options).Body;
        var ratio = kind == SymbolKind.Vessel ? options.Orientation == "horizontal" ? 1.75f : .7f : 1f;
        var w = Math.Min(area.Width, area.Height * ratio); var h = w / ratio;
        return SKRect.Create(area.MidX - w / 2, area.MidY - h / 2, Math.Max(0, w), Math.Max(0, h));
    }
    private static SKColor StateColor(SymbolOptions o) => o.IsFaulted ? SKColor.Parse("#C43D35") : o.State == "partiallyClosed" ? SKColor.Parse("#C28A25") : o.IsRunning ? SKColor.Parse("#258748") : Outline;
    private static float Rotation(string orientation) => orientation switch { "left" => 180, "top" => -90, "bottom" => 90, _ => 0 };
    private static bool BeginBody(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolOptions o)
    {
        var b = BodyBounds(bounds, kind, o); if (b.Width < 1 || b.Height < 1) return false;
        canvas.Translate(b.Left, b.Top); canvas.Scale(b.Width / 100, b.Height / 100);
        if (kind != SymbolKind.Vessel) canvas.RotateDegrees(Rotation(o.Orientation) + (kind == SymbolKind.Valve ? 90 : 0), 50, 50);
        return true;
    }
    public static void DrawStatic(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data)
    {
        var o = data.Options ?? new SymbolOptions { Orientation = kind == SymbolKind.Vessel ? "vertical" : "right", State = data.Value != 0 ? kind == SymbolKind.Valve ? "open" : "running" : kind == SymbolKind.Valve ? "closed" : "stopped" };
        o.Validate(kind.ToString());
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(bounds); using var p = new CanvasPainter(canvas);
            var bodySave = canvas.Save();
            try
            {
                if (BeginBody(canvas, bounds, kind, o))
                    switch (kind)
                    {
                        case SymbolKind.Pump: Pump(p, o); break;
                        case SymbolKind.Motor: Motor(p, o); break;
                        case SymbolKind.Valve: Valve(p, o); break;
                        case SymbolKind.Sensor: Sensor(p, o); break;
                        case SymbolKind.Vessel: Vessel(p, o, data); break;
                    }
            }
            finally { canvas.RestoreToCount(bodySave); }
            var layout = Arrange(bounds, o); Text(p, layout.Label, o.Label, Quality.Good); Text(p, layout.Readout, o.Readout, data.Quality);
            if (o.IsFaulted)
            {
                var x = bounds.Right - 10; p.Circle(x, bounds.Top + 10, 7, StateColor(o)); p.Text("!", x, bounds.Top + 14, 11, SKColors.White, SKTextAlign.Center);
            }
        }
        finally { canvas.RestoreToCount(save); }
    }
    private static void Text(CanvasPainter p, SKRect r, SymbolText text, Quality quality)
    {
        if (r.IsEmpty) return;
        var save = p.Canvas.Save();
        try
        {
            p.Canvas.ClipRect(r);
            var align = text.Justify == "left" ? SKTextAlign.Left : text.Justify == "right" ? SKTextAlign.Right : SKTextAlign.Center;
            p.Text(quality == Quality.Good ? text.Text : "—", align == SKTextAlign.Left ? r.Left : align == SKTextAlign.Right ? r.Right : r.MidX,
                r.MidY + (float)text.FontSize * .34f, (float)text.FontSize, Color(text.Color), align);
        }
        finally { p.Canvas.RestoreToCount(save); }
    }
    private static void Shape(CanvasPainter p, SKPath path, SKColor color, bool mimic)
    {
        p.Path(path, mimic ? Metal : Shell);
        if (mimic)
        {
            var save = p.Canvas.Save();
            try { p.Canvas.ClipPath(path, antialias: true); p.Gradient(path.Bounds, SKColor.Parse("#9E9E9E"), SKColor.Parse("#FAFAFA"), SKColor.Parse("#B9B9B9")); }
            finally { p.Canvas.RestoreToCount(save); }
        }
        p.Path(path, color, true, 1.8f);
    }
    private static void Box(CanvasPainter p, SKRect rect, SKColor color, float radius = 0, bool mimic = false)
    { using var path = new SKPath(); path.AddRoundRect(rect, radius, radius); Shape(p, path, color, mimic); }
    private static void Flange(CanvasPainter p, float x, float y, SKColor color)
    {
        Box(p, SKRect.Create(x, y, 5, 31), color, 1); p.Line(x + 2.5f, y + 3, x + 2.5f, y + 28, color, .8f);
        for (var n = 0; n < 3; n++) { p.Line(x - 2, y + 5 + n * 10, x, y + 5 + n * 10, color); p.Line(x + 5, y + 5 + n * 10, x + 7, y + 5 + n * 10, color); }
    }
}
