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
        if (kind == SymbolKind.LEDDisplay)
        { data.Led?.Validate(); if (data.Unit is null || data.Unit.Length > 128) throw new ArgumentException("LED units are limited to 128 characters."); }
    }
    public static void Draw(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data)
    {
        ArgumentNullException.ThrowIfNull(canvas); Validate(bounds, kind, data);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        DrawBase(canvas, bounds, kind, data);
        if (IndustrialSymbolRenderer.Supports(kind)) { using var painter = new CanvasPainter(canvas); IndustrialSymbolRenderer.DrawMotion(painter, bounds, kind, data, 0); }
    }
    internal static void DrawBase(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data, bool alternateFill = false)
    {
        if (IndustrialSymbolRenderer.Supports(kind)) IndustrialSymbolRenderer.DrawStatic(canvas, bounds, kind, data, alternateFill);
        else if (kind == SymbolKind.LEDDisplay) LedDisplayRenderer.Draw(canvas, bounds, LedDisplayRenderer.Resolve(data), data.Quality, data.Unit);
        else DrawLegacy(canvas, DisplayBounds(bounds, kind), kind, data);
        if (data.Quality != Quality.Good) DrawQuality(canvas, bounds, data.Quality);
    }
    private static SKRect DisplayBounds(SKRect bounds, SymbolKind kind)
    {
        if (kind is not (SymbolKind.Gauge or SymbolKind.Thermometer)) return bounds;
        var designWidth = kind == SymbolKind.Gauge ? 220f : 100f; var designHeight = kind == SymbolKind.Gauge ? 150f : 220f;
        var scale = Math.Min(bounds.Width / designWidth, bounds.Height / designHeight);
        var width = designWidth * scale; var height = designHeight * scale;
        return SKRect.Create(bounds.MidX - width / 2, bounds.MidY - height / 2, width, height);
    }
}

/// <summary>At most two UI-thread-owned vector pictures: primary and optional alternate state fill. No per-frame recording.</summary>
public sealed class SymbolRenderCache : IDisposable
{
    private SKPicture? _picture, _alternate;
    private SymbolData? _data;
    private SymbolKind _kind;
    private SKRect _bounds;
    private SKTypeface? _typeface;
    private CanvasPainter? _motion;
    public long PictureBuildCount { get; private set; }
    public long DrawCount { get; private set; }
    public bool HasPicture => _picture is not null;
    public int RetainedPictureCount => (_picture is null ? 0 : 1) + (_alternate is null ? 0 : 1);
    public void Draw(SKCanvas canvas, SKRect bounds, SymbolKind kind, SymbolData data, double elapsedSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(data);
        if (!double.IsFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        var industrial = IndustrialSymbolRenderer.Supports(kind);
        var effects = industrial && data.Options?.NeedsAnimation == true && data.Quality == Quality.Good;
        if (_picture is null || _bounds != bounds || _kind != kind || _data != data || !ReferenceEquals(_typeface, CanvasPainter.DrawingTypeface))
        {
            SymbolRenderer.Validate(bounds, kind, data);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            Build(bounds, kind, data, effects);
        }
        var saved = canvas.Save();
        try
        {
            var alternate = effects && data.Options!.AlternateFill(elapsedSeconds);
            canvas.ClipRect(bounds); canvas.DrawPicture(alternate && _alternate is not null ? _alternate : _picture!);
            if (effects)
            {
                _motion ??= new(canvas); _motion.Bind(canvas);
                IndustrialSymbolRenderer.DrawMotion(_motion, bounds, kind, data, elapsedSeconds, alternate);
            }
            DrawCount++;
        }
        finally { canvas.RestoreToCount(saved); }
    }
    private void Build(SKRect bounds, SymbolKind kind, SymbolData data, bool effects)
    {
        SKPicture Record(bool alternate)
        {
            using var recorder = new SKPictureRecorder(); var recording = recorder.BeginRecording(bounds);
            if (effects) SymbolRenderer.DrawBase(recording, bounds, kind, data, alternate);
            else SymbolRenderer.Draw(recording, bounds, kind, data);
            return recorder.EndRecording();
        }
        SKPicture? primary = null, alternate = null;
        try
        {
            primary = Record(false);
            if (effects && data.Options!.IsFlashing) alternate = Record(true);
            _picture?.Dispose(); _alternate?.Dispose(); _motion?.Dispose(); _motion = null;
            _picture = primary; _alternate = alternate; primary = alternate = null;
            _data = data; _kind = kind; _bounds = bounds; _typeface = CanvasPainter.DrawingTypeface; PictureBuildCount++;
        }
        finally { primary?.Dispose(); alternate?.Dispose(); }
    }
    public void Clear() { _picture?.Dispose(); _alternate?.Dispose(); _motion?.Dispose(); _picture = _alternate = null; _motion = null; _data = null; _typeface = null; }
    public void Dispose() => Clear();
}
