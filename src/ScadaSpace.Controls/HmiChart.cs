using ScadaSpace.Graphics;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ScadaSpace.Controls;

public partial class HmiChart : SKCanvasElement
{
    private double? _cursor;
    public ChartKind Kind { get; set; }
    public IReadOnlyList<PlotSeries> Series { get; private set; } = [];
    public ChartOptions Options { get; private set; } = new();
    public HmiChart()
    {
        PointerMoved += (_, args) => { _cursor = (args.GetCurrentPoint(this).Position.X - 47) / Math.Max(1, ActualWidth - 63); Invalidate(); };
        PointerExited += (_, _) => { _cursor = null; Invalidate(); };
        PointerWheelChanged += (_, args) =>
        {
            Options = Options with { WindowSeconds = Math.Clamp(Options.WindowSeconds * (args.GetCurrentPoint(this).Properties.MouseWheelDelta > 0 ? .8 : 1.25), 5, 86400) };
            args.Handled = true; Invalidate();
        };
    }
    public void SetData(IReadOnlyList<PlotSeries> series, ChartOptions? options = null)
    { Series = series; if (options is not null) Options = options; Invalidate(); }
    protected override void RenderOverride(SKCanvas canvas, Size area) => ChartRenderer.Draw(canvas, SKRect.Create((float)area.Width, (float)area.Height), Kind, Series, Options with { CursorFraction = _cursor });
}
