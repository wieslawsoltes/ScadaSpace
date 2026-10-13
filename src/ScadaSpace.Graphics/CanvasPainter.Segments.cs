using SkiaSharp;

namespace ScadaSpace.Graphics;

public sealed partial class CanvasPainter
{
    /// <summary>Beveled LED diode using a reused path, not a new native path per segment.</summary>
    public void Diode(float x1, float y1, float x2, float y2, SKColor color, float thickness = 3)
    {
        var dx=x2-x1; var dy=y2-y1; var length=MathF.Sqrt(dx*dx+dy*dy);
        if (!float.IsFinite(length) || !float.IsFinite(thickness) || thickness <= 0 || thickness > 512)
            throw new ArgumentException("Diode geometry and thickness must be finite and bounded.");
        if (length<=0) return;
        var ux=dx/length; var uy=dy/length; var half=Math.Min(thickness/2,length/3); var bevel=half;
        _curve.Rewind(); _curve.MoveTo(x1,y1);
        _curve.LineTo(x1+ux*bevel-uy*half,y1+uy*bevel+ux*half);
        _curve.LineTo(x2-ux*bevel-uy*half,y2-uy*bevel+ux*half);
        _curve.LineTo(x2,y2);
        _curve.LineTo(x2-ux*bevel+uy*half,y2-uy*bevel-ux*half);
        _curve.LineTo(x1+ux*bevel+uy*half,y1+uy*bevel-ux*half);
        _curve.Close(); Path(_curve,color);
    }
    public void DiodePlus(SKColor color)
    {
        _curve.Rewind(); _curve.FillType = SKPathFillType.Winding;
        _curve.AddRect(SKRect.Create(3, 21.5f, 22, 3)); _curve.AddRect(SKRect.Create(12.5f, 11, 3, 24));
        Path(_curve, color);
    }
    public void FittedText(string text, SKRect bounds, SKColor color, float maximumSize=12)
    {
        if (bounds.Width<=0 || bounds.Height<=0) return;
        _font.Size=Math.Min(maximumSize,bounds.Height*.8f);
        var width=_font.MeasureText(text);
        if (width>bounds.Width) _font.Size*=bounds.Width/width;
        Canvas.DrawText(text,bounds.Right,bounds.Bottom-_font.Size*.2f,SKTextAlign.Right,_font,Paint(color));
    }
}
