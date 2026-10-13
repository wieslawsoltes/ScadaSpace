using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

/// <summary>Aspect-fitted seven/fourteen-segment vector graphics with independent lit/unlit colors and punctuation.</summary>
public static class LedDisplayRenderer
{
    private static readonly (float X1,float Y1,float X2,float Y2)[] Segments = [
        (4,0,26,0),(28,3,28,20),(28,26,28,43),(4,46,26,46),(2,26,2,43),(2,3,2,20),
        (4,23,13,23),(17,23,26,23),(5,4,13,19),(15,4,15,19),(25,4,17,19),
        (13,27,5,42),(15,27,15,42),(17,27,25,42)];
    public static LedDisplaySnapshot Resolve(SymbolData data) => data.Led?.WithNumericValue(data.Value)
        ?? LedDisplayPresentation.Create(Scalar.From(data.Value), LedDisplayOptions.Legacy with { NumberFormat=data.Format });
    public static void Draw(SKCanvas canvas, SKRect bounds, LedDisplaySnapshot display, Quality quality=Quality.Good, string unit="")
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(display); display.Validate();
        if (!Enum.IsDefined(quality) || !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right)
            || !float.IsFinite(bounds.Bottom) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height)
            || bounds.Width>32768 || bounds.Height>32768 || unit is null || unit.Length>128)
            throw new ArgumentException("Invalid LED bounds, quality or unit length.");
        if (bounds.Width<=0 || bounds.Height<=0) return;
        var options=display.Options; var on=IndustrialSymbolRenderer.Color(options.DiodeOnColor); var off=IndustrialSymbolRenderer.Color(options.DiodeOffColor);
        var saved=canvas.Save();
        try
        {
            canvas.ClipRect(bounds); using var painter=new CanvasPainter(canvas);
            painter.Rect(bounds,IndustrialSymbolRenderer.Color(options.BackgroundColor),2);
            var pad=Math.Min(8,Math.Min(bounds.Width,bounds.Height)*.1f);
            var unitHeight=unit.Length==0?0:Math.Min(16,bounds.Height*.25f);
            var availableWidth=Math.Max(0,bounds.Width-2*pad); var availableHeight=Math.Max(0,bounds.Height-2*pad-unitHeight);
            var text=quality==Quality.Good?display.Text:"----";
            var width=LedGlyphs.Measure(text,options.FourteenSegments);
            var scale=Math.Min(availableWidth/width,availableHeight/54);
            if (scale>0)
            {
                var digits=canvas.Save();
                try
                {
                    canvas.Translate(bounds.MidX-width*scale/2,bounds.Top+pad+(availableHeight-54*scale)/2+2*scale);
                    canvas.Scale(scale);
                    foreach(var character in text)
                    {
                        var glyph=LedGlyphs.Get(character,options.FourteenSegments);
                        DrawGlyph(painter,glyph,options.FourteenSegments,on,off); canvas.Translate(glyph.Advance,0);
                    }
                }
                finally { canvas.RestoreToCount(digits); }
            }
            if(unitHeight>0) painter.FittedText(unit,SKRect.Create(bounds.Left+pad,bounds.Bottom-pad-unitHeight,availableWidth,unitHeight),on);
        }
        finally { canvas.RestoreToCount(saved); }
    }
    private static void DrawGlyph(CanvasPainter p, LedGlyph glyph, bool fourteen, SKColor on, SKColor off)
    {
        if(glyph.Mark!=LedMark.None)
        {
            switch(glyph.Mark)
            {
                case LedMark.Dot: p.Circle(4,45,2,on); break;
                case LedMark.Comma: p.Diode(5,43,1,51,on,3); break;
                case LedMark.Colon: p.Circle(5,15,2,on); p.Circle(5,34,2,on); break;
                case LedMark.Percent: p.Circle(7,12,4,on,true,2); p.Circle(21,36,4,on,true,2); p.Diode(4,42,24,5,on,2); break;
                case LedMark.Plus: p.DiodePlus(on); break;
                case LedMark.Slash: p.Diode(3,44,25,2,on,3); break;
                case LedMark.Degree: p.Circle(7,7,4,on,true,2); break;
            }
            return;
        }
        for(var i=0;i<(fourteen?14:6);i++)
        { var s=Segments[i]; p.Diode(s.X1,s.Y1,s.X2,s.Y2,(glyph.Segments&(1<<i))!=0?on:off); }
        if(!fourteen) p.Diode(4,23,26,23,(glyph.Segments&192)!=0?on:off);
    }
}
