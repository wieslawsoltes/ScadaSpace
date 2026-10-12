using ScadaSpace.Core;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public static partial class SymbolRenderer
{
    public static void Validate(SKRect bounds, SymbolKind kind, SymbolData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(data.Quality) || !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top)
            || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) || bounds.Width > 32768 || bounds.Height > 32768)
            throw new ArgumentException("Invalid symbol kind, quality or bounds.");
        _ = SymbolPresentation.Fraction(data.Value, data.Minimum, data.Maximum);
        if (!double.IsFinite(data.StrokeWidth) || data.StrokeWidth is < 0 or > 1024 || !double.IsFinite(data.Radius) || data.Radius is < 0 or > 32768 || !double.IsFinite(data.Warning)) throw new ArgumentException("Invalid stroke, radius or warning level.");
        NumberFormatting.Validate(data.Format);
        if (kind is SymbolKind.Tank or SymbolKind.Vessel && data.Maximum <= 0) throw new ArgumentException("Tank and vessel capacity must be positive.");
        data.Options?.Validate(kind.ToString()); data.Tank?.Validate();
    }
    public static void Draw(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data)
    {
        ArgumentNullException.ThrowIfNull(canvas); Validate(bounds, kind, data);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        DrawBase(canvas, bounds, kind, data);
        if (IndustrialSymbolRenderer.Supports(kind))
        { using var painter = new CanvasPainter(canvas); IndustrialSymbolRenderer.DrawMotion(painter, bounds, kind, data, 0); }
    }
    internal static void DrawBase(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data)
    {
        if (IndustrialSymbolRenderer.Supports(kind)) IndustrialSymbolRenderer.DrawStatic(canvas, bounds, kind, data);
        else DrawLegacy(canvas, bounds, kind, data);
        if (data.Quality != Quality.Good) DrawQuality(canvas, bounds, data.Quality);
    }
}

/// <summary>One UI-thread-owned vector picture per symbol. No raster image copies or retained global symbol cache.</summary>
public sealed class SymbolRenderCache : IDisposable
{
    private SKPicture? _picture;
    private SymbolData? _data;
    private SymbolKind _kind;
    private SKRect _bounds;
    private SKTypeface? _typeface;
    private CanvasPainter? _motion;
    public long PictureBuildCount { get; private set; }
    public long DrawCount { get; private set; }
    public bool HasPicture => _picture is not null;
    public void Draw(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data, double elapsedSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!double.IsFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (_picture is null || _bounds != bounds || _kind != kind || _data != data || !ReferenceEquals(_typeface, CanvasPainter.DrawingTypeface))
        {
            SymbolRenderer.Validate(bounds, kind, data);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using var recorder = new SKPictureRecorder(); var recording = recorder.BeginRecording(bounds);
            if (IndustrialSymbolRenderer.Supports(kind) && data.Options?.IsAnimated == true && data.Quality == Quality.Good)
                SymbolRenderer.DrawBase(recording, bounds, kind, data);
            else SymbolRenderer.Draw(recording, bounds, kind, data);
            var picture = recorder.EndRecording();
            _picture?.Dispose(); _picture = picture; _data = data; _kind = kind; _bounds = bounds; _typeface = CanvasPainter.DrawingTypeface;
            _motion?.Dispose(); _motion = null; PictureBuildCount++;
        }
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(bounds); canvas.DrawPicture(_picture);
            if (IndustrialSymbolRenderer.Supports(kind) && data.Options?.IsAnimated == true && data.Quality == Quality.Good)
            {
                _motion ??= new(canvas); _motion.Bind(canvas); IndustrialSymbolRenderer.DrawMotion(_motion, bounds, kind, data, elapsedSeconds);
            }
            DrawCount++;
        }
        finally { canvas.RestoreToCount(save); }
    }
    public void Clear() { _picture?.Dispose(); _picture = null; _motion?.Dispose(); _motion = null; _data = null; _typeface = null; }
    public void Dispose() => Clear();
}
