using SkiaSharp;

namespace ScadaSpace.Graphics;

public sealed partial class CanvasPainter
{
    /// <summary>Draw a gravity-aligned liquid body and elliptical surface with one coverage pass.
    /// Opacity applies once to the color alpha, including the surface sheen. No offscreen layer is used.</summary>
    public void LiquidColumn(SKRect body, float surfaceRadius, SKColor color, double opacity)
    {
        if (!float.IsFinite(body.Left) || !float.IsFinite(body.Top) || !float.IsFinite(body.Right) || !float.IsFinite(body.Bottom)
            || !float.IsFinite(body.Width) || !float.IsFinite(body.Height) || body.Width > 32768 || body.Height > 32768
            || !float.IsFinite(surfaceRadius) || surfaceRadius is < 0 or > 32768 || !double.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentException("Liquid geometry and opacity must be finite and within their supported range.");
        if (body.Width <= 0 || body.Height <= 0 || opacity == 0 || color.Alpha == 0) return;
        var fill = color.WithAlpha((byte)Math.Round(color.Alpha * opacity));
        if (fill.Alpha == 0) return;
        if (surfaceRadius == 0) { Rect(body, fill); return; }
        using var path = new SKPath { FillType = SKPathFillType.Winding };
        // Overlapping contours within one winding path cover each pixel once.
        path.AddRect(body);
        path.AddOval(SKRect.Create(body.Left, body.Top - surfaceRadius, body.Width, surfaceRadius * 2));
        var sheen = new SKColor((byte)(fill.Red + (255 - fill.Red) * .18),
            (byte)(fill.Green + (255 - fill.Green) * .18), (byte)(fill.Blue + (255 - fill.Blue) * .18), fill.Alpha);
        using var shader = SKShader.CreateLinearGradient(new(body.MidX, body.Top - surfaceRadius),
            new(body.MidX, body.Top + surfaceRadius), [sheen, fill], [0f, 1f], SKShaderTileMode.Clamp);
        var paint = Paint(SKColors.White); paint.Shader = shader;
        try { Canvas.DrawPath(path, paint); }
        finally { paint.Shader = null; }
    }
}
