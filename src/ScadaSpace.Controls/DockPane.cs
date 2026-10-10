using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace ScadaSpace.Controls;

public partial class DockPane : Grid
{
    public TextBlock TitleElement { get; }
    public DockPane(string title, UIElement content)
    {
        RowDefinitions.Add(new() { Height = new GridLength(27) }); RowDefinitions.Add(new());
        TitleElement = ControlTheme.Text(title, 12);
        Children.Add(new Border { Child = TitleElement, Padding = new Thickness(8, 0, 6, 0), Background = ControlTheme.Brush("#D8E0E5"), BorderBrush = ControlTheme.Brush("#B1BEC7"), BorderThickness = new Thickness(0, 0, 0, 1) });
        Grid.SetRow(content, 1); Children.Add(content); Background = ControlTheme.Brush("#F4F6F8");
    }
}

public partial class DockSplitter : Border
{
    private Point? _start;
    public bool Vertical { get; set; }
    public event Action<double>? DragDelta;
    public DockSplitter()
    {
        Background = ControlTheme.Brush("#C2CDD4");
        PointerPressed += (_, e) => { _start = e.GetCurrentPoint(null).Position; CapturePointer(e.Pointer); e.Handled = true; };
        PointerMoved += (_, e) =>
        {
            if (_start is not { } start) return;
            var point = e.GetCurrentPoint(null).Position; var delta = Vertical ? point.Y - start.Y : point.X - start.X;
            _start = point; DragDelta?.Invoke(delta); e.Handled = true;
        };
        PointerReleased += (_, e) => { _start = null; ReleasePointerCapture(e.Pointer); e.Handled = true; };
        PointerCaptureLost += (_, _) => _start = null;
    }
}
