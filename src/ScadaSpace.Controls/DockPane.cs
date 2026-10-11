using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace ScadaSpace.Controls;

public partial class DockPane : Grid
{
    private readonly DesignerToolButton? _pin;
    public TextBlock TitleElement { get; }
    public FrameworkElement? CloseButton { get; }
    public FrameworkElement? PinButton => _pin;
    public DockPane(string title, UIElement content, string? id = null, Action? close = null, Action? pin = null)
    {
        RowDefinitions.Add(new() { Height = new GridLength(22) }); RowDefinitions.Add(new());
        TitleElement = ControlTheme.Text(title, 11);
        var header = new Grid { Padding = new Thickness(5,0,2,0), Background = ControlTheme.Brush("#D7DADD"), BorderBrush = ControlTheme.Brush("#B9C0C5"), BorderThickness = new Thickness(0,0,0,1) };
        header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        header.Children.Add(TitleElement);var tools=new StackPanel {Orientation=Orientation.Horizontal};Grid.SetColumn(tools,1);header.Children.Add(tools);
        if(pin is not null) { _pin=new DesignerToolButton("pin","Pin "+title,pin,id+"-pin") {Width=20,MinWidth=0,Height=20,Padding=new Thickness(3)};tools.Children.Add(_pin); }
        if(close is not null) { var button=new DesignerToolButton("close","Hide "+title,close,id+"-close") {Width=20,MinWidth=0,Height=20,Padding=new Thickness(3)};tools.Children.Add(button);CloseButton=button; }
        Children.Add(header);Grid.SetRow(content,1);Children.Add(content);Background=ControlTheme.Brush("#F5F5F5");
    }
    public void SetPinned(bool pinned) { _pin?.SetGlyph(pinned?"unpin":"pin");_pin?.SetLabel(pinned?"Auto-hide components":"Pin components"); }
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
