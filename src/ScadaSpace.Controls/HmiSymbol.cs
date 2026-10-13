using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using ScadaSpace.Core;
using ScadaSpace.Graphics;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ScadaSpace.Controls;

/// <summary>Native vector symbol with retained state pictures and demand-driven, viewport-culled effects.</summary>
public partial class HmiSymbol : SKCanvasElement, IDisposable
{
    private static DependencyProperty Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name, T value) => DependencyProperty.Register(name, typeof(T), typeof(HmiSymbol), new PropertyMetadata(value, (o, _) => ((HmiSymbol)o).Changed()));
    public static readonly DependencyProperty KindProperty = Register(nameof(Kind), SymbolKind.Tank);
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0d);
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0d);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 100d);
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), "");
    public static readonly DependencyProperty SymbolColorProperty = Register(nameof(SymbolColor), "#48AFC0");
    public static readonly DependencyProperty QualityProperty = Register(nameof(Quality), Quality.Good);
    public SymbolKind Kind { get => (SymbolKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string SymbolColor { get => (string)GetValue(SymbolColorProperty); set => SetValue(SymbolColorProperty, value); }
    public Quality Quality { get => (Quality)GetValue(QualityProperty); set => SetValue(QualityProperty, value); }
    private SymbolData _data = new();
    private SymbolData? _snapshot;
    private readonly SymbolRenderCache _cache = new();
    private bool _batch, _disposed, _allowAnimation = true;
    private Quality _errorQuality = Quality.BadExpression;
    public SymbolData Data { get => _data; set { ArgumentNullException.ThrowIfNull(value); if (_data == value) return; _data = value; Changed(); } }
    public bool AllowAnimation { get => _allowAnimation; set { if (_allowAnimation == value) return; _allowAnimation = value; Changed(); } }
    public string? RenderError { get; private set; }
    public string? DataError { get; private set; }
    public long PictureBuildCount => _cache.PictureBuildCount;
    public int RetainedPictureCount => _cache.RetainedPictureCount;
    public bool LastAlternateFill { get; private set; }
    public LedDisplaySnapshot? LedDisplay => _snapshot?.Led ?? Data.Led;
    public long RenderCount => _cache.DrawCount;
    public long AnimationInvalidations { get; private set; }
    public bool Animating => !_disposed && AllowAnimation && IsLoaded && DataError is null && Quality == Quality.Good && Data.Options?.NeedsAnimation == true && IndustrialSymbolRenderer.Supports(Kind);
    public HmiSymbol()
    {
        Loaded += (_, _) => { SymbolAnimation.Update(this); Invalidate(); };
        Unloaded += (_, _) => { SymbolAnimation.Remove(this); _cache.Clear(); };
    }
    private void Changed()
    {
        _snapshot = null; if (_batch || _disposed) return;
        SymbolAnimation.Update(this); Invalidate();
    }
    public void SetSnapshot(SymbolData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_data == data && Value == data.Value && Minimum == data.Minimum && Maximum == data.Maximum && Unit == data.Unit && SymbolColor == data.Color && Quality == data.Quality) return;
        _batch = true;
        try { _data = data; Value = data.Value; Minimum = data.Minimum; Maximum = data.Maximum; Unit = data.Unit; SymbolColor = data.Color; Quality = data.Quality; }
        finally { _batch = false; Changed(); }
    }
    public void SetDiagnostic(string? message, Quality quality = Quality.BadExpression)
    {
        if (DataError == message && _errorQuality == quality) return;
        DataError = message; _errorQuality = quality; AutomationProperties.SetHelpText(this, message ?? ""); Changed();
    }
    public bool IsInAnimationViewport
    {
        get
        {
            if (_disposed || !IsLoaded || XamlRoot is null || ActualWidth <= 0 || ActualHeight <= 0) return false;
            var left = 0d; var top = 0d; var right = XamlRoot.Size.Width; var bottom = XamlRoot.Size.Height;
            try
            {
                for (DependencyObject? element = this; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
                {
                    if (element is UIElement ui && (ui.Visibility != Visibility.Visible || ui.Opacity <= 0)) return false;
                    if (element is Microsoft.UI.Xaml.Controls.ScrollViewer scroll)
                    {
                        var clip = scroll.TransformToVisual(null).TransformBounds(new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight));
                        left = Math.Max(left, clip.Left); top = Math.Max(top, clip.Top); right = Math.Min(right, clip.Right); bottom = Math.Min(bottom, clip.Bottom);
                    }
                }
                var bounds = TransformToVisual(null).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
                return right > left && bottom > top && bounds.Right > left && bounds.Left < right && bounds.Bottom > top && bounds.Top < bottom;
            }
            catch (InvalidOperationException) { return false; }
        }
    }
    internal void AnimateFrame()
    {
        if (!Animating || !IsInAnimationViewport) return;
        // A flash-only symbol needs a new frame at a color transition, not at 30 Hz.
        if (Data.Options is { IsAnimated: false } options
            && options.AlternateFill(SymbolAnimation.ElapsedSeconds) == LastAlternateFill && RenderError is null) return;
        AnimationInvalidations++; Invalidate();
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_disposed) return; var bounds = SKRect.Create((float)area.Width, (float)area.Height);
        try
        {
            if (DataError is not null) { SymbolRenderer.DrawQuality(canvas, bounds, _errorQuality); RenderError = DataError; return; }
            _snapshot ??= Data with
            {
                Value = Value, Minimum = Minimum, Maximum = Maximum, Unit = Unit, Color = SymbolColor, Quality = Quality,
                Options = !AllowAnimation && Data.Options is { } options ? options with { AnimationSpeed = 0 } : Data.Options
            };
            if (_snapshot.Led is { NumericValue: not null } led && led.NumericValue != Value)
                _snapshot = _snapshot with { Led = led.WithNumericValue(Value) };
            var elapsed = AllowAnimation ? SymbolAnimation.ElapsedSeconds : 0;
            _cache.Draw(canvas, bounds, Kind, _snapshot, elapsed);
            LastAlternateFill = Quality == Quality.Good && _snapshot.Options?.AlternateFill(elapsed) == true;
            RenderError = null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { RenderError = ex.Message; SymbolRenderer.DrawQuality(canvas, bounds, Quality.BadExpression); }
    }
    public new void Dispose() { _disposed = true; SymbolAnimation.Remove(this); _cache.Dispose(); }
}
