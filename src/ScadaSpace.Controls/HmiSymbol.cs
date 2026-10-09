using Microsoft.UI.Xaml;
using ScadaSpace.Core;
using ScadaSpace.Graphics;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ScadaSpace.Controls;

/// <summary>Reusable XAML-bindable vector symbol drawn directly into Uno's Skia composition surface.</summary>
public partial class HmiSymbol : SKCanvasElement
{
    private static DependencyProperty Register<T>(string name, T value) => DependencyProperty.Register(name, typeof(T), typeof(HmiSymbol), new PropertyMetadata(value, (o, _) => ((HmiSymbol)o).Invalidate()));
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
    public SymbolData Data { get; set; } = new();
    public string? RenderError { get; private set; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var bounds = SKRect.Create((float)area.Width, (float)area.Height);
        try
        {
            SymbolRenderer.Draw(canvas, bounds, Kind, Data with { Value = Value, Minimum = Minimum, Maximum = Maximum, Unit = Unit, Color = SymbolColor, Quality = Quality });
            RenderError = null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { RenderError = ex.Message; SymbolRenderer.DrawQuality(canvas, bounds, Quality.BadExpression); }
    }
}
