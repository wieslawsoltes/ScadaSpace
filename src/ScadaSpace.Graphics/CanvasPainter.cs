using SkiaSharp;

namespace ScadaSpace.Graphics;

/// <summary>Frame-local paints are reused across primitives and deterministically disposed.</summary>
public sealed class CanvasPainter(SKCanvas canvas) : IDisposable
{
    /// <summary>Optional application-owned typeface. Set before drawing and retain it until rendering has stopped.</summary>
    public static SKTypeface? DrawingTypeface { get; set; }
    public SKCanvas Canvas { get; private set; } = canvas;
    public void Bind(SKCanvas canvas) { ArgumentNullException.ThrowIfNull(canvas); Canvas = canvas; }
    private readonly SKPaint _paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _curve = new();
    private readonly SKFont _font = new(DrawingTypeface ?? SKTypeface.Default, 13);
    public static SKColor Color(string value, string fallback = "#415969") => SKColor.TryParse(value, out var color) ? color : SKColor.Parse(fallback);
    private SKPaint Paint(SKColor color, bool stroke = false, float width = 1)
    {
        _paint.Color = color; _paint.Style = stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill; _paint.StrokeWidth = width; _paint.Shader = null;
        return _paint;
    }
    public void Rect(SKRect rect, SKColor color, float radius = 0, bool stroke = false, float width = 1) => Canvas.DrawRoundRect(rect, radius, radius, Paint(color, stroke, width));
    public void Ellipse(SKRect rect, SKColor color, bool stroke = false, float width = 1) => Canvas.DrawOval(rect, Paint(color, stroke, width));
    public void Circle(float x, float y, float radius, SKColor color, bool stroke = false, float width = 1) => Canvas.DrawCircle(x, y, radius, Paint(color, stroke, width));
    public void Line(float x1, float y1, float x2, float y2, SKColor color, float width = 1) => Canvas.DrawLine(x1, y1, x2, y2, Paint(color, true, width));
    public void Path(SKPath path, SKColor color, bool stroke = false, float width = 1) => Canvas.DrawPath(path, Paint(color, stroke, width));
    public void Text(string text, float x, float baseline, float size, SKColor color, SKTextAlign align = SKTextAlign.Left)
    {
        _font.Size = size; Canvas.DrawText(text, x, baseline, align, _font, Paint(color));
    }
    public void Polygon(SKColor color, params SKPoint[] points)
    {
        if (points.Length == 0) return;
        using var path = new SKPath(); path.MoveTo(points[0]); for (var i = 1; i < points.Length; i++) path.LineTo(points[i]); path.Close(); Path(path, color);
    }
    public void Curve(float x, float y, float c1x, float c1y, float c2x, float c2y, float endX, float endY, SKColor color, float width = 1)
    {
        _curve.Rewind(); _curve.MoveTo(x, y); _curve.CubicTo(c1x, c1y, c2x, c2y, endX, endY); Path(_curve, color, true, width);
    }
    public void Arc(SKRect rect, float startAngle, float sweep, SKColor color, float width)
    { using var path = new SKPath(); path.AddArc(rect, startAngle, sweep); Path(path, color, true, width); }
    public void Gradient(SKRect rect, SKColor left, SKColor center, SKColor right)
    {
        using var shader = SKShader.CreateLinearGradient(new(rect.Left, 0), new(rect.Right, 0), [left, center, right], [0f, .45f, 1f], SKShaderTileMode.Clamp);
        var paint = Paint(left); paint.Shader = shader; Canvas.DrawRect(rect, paint); paint.Shader = null;
    }
    public void Dispose() { _curve.Dispose(); _font.Dispose(); _paint.Dispose(); }
}
