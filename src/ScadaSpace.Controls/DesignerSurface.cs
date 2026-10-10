using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Graphics;
using ScadaSpace.Layout;
using ScadaSpace.Runtime;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace ScadaSpace.Controls;

/// <summary>Reusable design surface: selection, marquee, snapped drag/resize, palette drops, rulers and zoom.</summary>
public partial class DesignerSurface : Grid, IDisposable
{
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ZoomMode = ZoomMode.Disabled };
    private readonly Viewbox _scale = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid _board = new();
    private readonly Grid _layers = new();
    private readonly DesignAdorner _adorner = new();
    private readonly Ruler _horizontal = new();
    private readonly Ruler _vertical = new() { Vertical = true };
    private Point? _start;
    private Point _last;
    private bool _resize;
    private bool _marquee;
    private double _zoom = .75;
    public ViewHost Host { get; } = new();
    public DesignerDocument? Document { get; private set; }
    public string ViewPath { get; private set; } = "Overview";
    public bool Preview { get; private set; }
    public double SnapSize { get; set; } = 8;
    public bool ShowGrid { get => _adorner.ShowGrid; set { _adorner.ShowGrid = value; _adorner.Invalidate(); } }
    public double Zoom { get => _zoom; set { _zoom = Math.Clamp(value, .1, 4); ApplyZoom(); } }
    public event Action? SelectionChanged;
    public event Action<string>? Status;
    public event Action? ZoomChanged;
    public DesignerSurface()
    {
        Background = ControlTheme.Brush("#CCD3D8");
        _board.RowDefinitions.Add(new() { Height = new GridLength(20) }); _board.RowDefinitions.Add(new());
        _board.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); _board.ColumnDefinitions.Add(new());
        _board.Children.Add(new Border { Background = ControlTheme.Brush("#DEE4E8") });
        Grid.SetColumn(_horizontal, 1); _board.Children.Add(_horizontal);
        Grid.SetRow(_vertical, 1); _board.Children.Add(_vertical);
        _layers.Children.Add(Host); _layers.Children.Add(_adorner); Grid.SetRow(_layers, 1); Grid.SetColumn(_layers, 1); _board.Children.Add(_layers);
        _scale.Child = _board; _scale.Margin = new Thickness(12); _scroll.Content = _scale; Children.Add(_scroll);
        _adorner.PointerPressed += Pressed; _adorner.PointerMoved += Moved; _adorner.PointerReleased += Released;
        _adorner.PointerCanceled += (_, _) => CancelDrag(); _adorner.PointerCaptureLost += (_, _) => CancelDrag();
        _adorner.PointerWheelChanged += (_, e) => { if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) { Zoom *= e.GetCurrentPoint(_adorner).Properties.MouseWheelDelta > 0 ? 1.1 : 1 / 1.1; e.Handled = true; } };
        _layers.AllowDrop = true;
        _layers.DragOver += (_, e) => { if (!Preview && e.DataView.Contains(StandardDataFormats.Text)) e.AcceptedOperation = DataPackageOperation.Copy; };
        _layers.Drop += async (_, e) =>
        {
            if (Preview || !e.DataView.Contains(StandardDataFormats.Text)) return;
            var text = await e.DataView.GetTextAsync();
            if (text.StartsWith("scadaspace:", StringComparison.Ordinal)) { var point = e.GetPosition(_layers); AddComponent(text[11..], point.X, point.Y); }
        };
        Host.Error += message => Status?.Invoke(message);
    }
    public void Load(DesignerDocument document, ProjectRuntime runtime, string path, bool preview, RuntimeSession? session = null)
    {
        Document = document; ViewPath = path; Preview = preview;
        CancelDrag(); Host.Load(runtime, path, editing: !preview, session: session);
        var view = Host.Instance!.View;
        // Selection belongs to the displayed view; stale inspectors must not read foreign nodes.
        var members = view.Root.DescendantsAndSelf().Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (document.Selection.Any(id => !members.Contains(id))) document.Select(null);
        _board.Width = view.Width + 20; _board.Height = view.Height + 20;
        _layers.Width = view.Width; _layers.Height = view.Height;
        _adorner.IsHitTestVisible = !preview; _adorner.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        _adorner.Document = document; _adorner.Root = view.Root; _adorner.Invalidate();
        _horizontal.Invalidate(); _vertical.Invalidate(); ApplyZoom();
    }
    private void ApplyZoom()
    {
        _scale.Width = _board.Width > 0 ? _board.Width * _zoom : 800;
        _scale.Height = _board.Height > 0 ? _board.Height * _zoom : 600;
        ZoomChanged?.Invoke();
    }
    public void Fit()
    {
        if (_board.Width > 0 && ActualWidth > 30 && ActualHeight > 30) Zoom = Math.Min((ActualWidth - 32) / _board.Width, (ActualHeight - 32) / _board.Height);
    }
    public void RefreshSelection() { _adorner.Invalidate(); SelectionChanged?.Invoke(); }
    public void AddComponent(string type, double? x = null, double? y = null)
    {
        if (Preview || Document is null || Host.Instance is null) return;
        try
        {
            var node = ComponentCatalog.Create(type, DesignerDocument.Snap(x ?? 32, SnapSize), DesignerDocument.Snap(y ?? 32, SnapSize));
            Document.Select(null);
            Document.Execute("Add " + type, project => project.Views.Single(v => v.Path == ViewPath).Root.Children.Add(node));
            Document.Select(node.Id); RefreshSelection(); Status?.Invoke("Added " + type);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Status?.Invoke(ex.Message); }
    }
    private IReadOnlyList<LayoutSlot> Slots() => Host.Instance is { } instance ? LayoutEngine.Flatten(instance.View.Root, instance.View.Width, instance.View.Height) : [];
    private LayoutSlot? Hit(Point point)
    {
        foreach (var slot in Slots().Reverse())
        {
            var radians = -slot.Node.Rotation * Math.PI / 180; var cx = slot.Bounds.X + slot.Bounds.Width / 2; var cy = slot.Bounds.Y + slot.Bounds.Height / 2;
            var x = cx + (point.X - cx) * Math.Cos(radians) - (point.Y - cy) * Math.Sin(radians);
            var y = cy + (point.X - cx) * Math.Sin(radians) + (point.Y - cy) * Math.Cos(radians);
            if (slot.Bounds.Contains(x, y)) return slot;
        }
        return null;
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (Preview || Document is null) return;
        var point = e.GetCurrentPoint(_adorner);
        if (!point.Properties.IsLeftButtonPressed) return;
        _start = point.Position; _last = point.Position;
        var additive = (e.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)) != 0;
        var selected = Slots().Where(s => Document.Selection.Contains(s.Node.Id)).ToArray();
        _resize = selected.Length == 1 && !selected[0].Node.Locked && Math.Abs(point.Position.X - selected[0].Bounds.Right) <= 8 / Zoom && Math.Abs(point.Position.Y - selected[0].Bounds.Bottom) <= 8 / Zoom;
        if (!_resize)
        {
            var hit = Hit(point.Position);
            _marquee = hit is null;
            if (hit is null) { if (!additive) Document.Select(null); }
            else if (!Document.Selection.Contains(hit.Node.Id) || additive) Document.Select(hit.Node.Id, additive);
            SelectionChanged?.Invoke();
        }
        _adorner.CapturePointer(e.Pointer); _adorner.Invalidate(); e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_adorner).Position;
        if (_start is not { } start) return;
        _last = position;
        var dx = DesignerDocument.Snap(position.X - start.X, SnapSize); var dy = DesignerDocument.Snap(position.Y - start.Y, SnapSize);
        _adorner.Delta = new(dx, dy); _adorner.Resizing = _resize;
        _adorner.Marquee = _marquee ? Normalize(start, position) : null;
        _adorner.Invalidate(); Status?.Invoke($"ΔX {dx:0}  ΔY {dy:0}"); e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } start || Document is null) return;
        var end = e.GetCurrentPoint(_adorner).Position;
        var resize = _resize; var marquee = _marquee;
        _start = null; _adorner.ReleasePointerCapture(e.Pointer);
        try
        {
            if (marquee)
            {
                var box = Normalize(start, end);
                foreach (var slot in Slots().Where(s => s.Bounds.Intersects(box))) Document.Selection.Add(slot.Node.Id);
            }
            else if (Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) > 2)
            {
                var dx = DesignerDocument.Snap(end.X - start.X, SnapSize); var dy = DesignerDocument.Snap(end.Y - start.Y, SnapSize);
                if (resize && Document.Selection.Count == 1)
                { var id = Document.Selection.Single(); var node = Document.Find(id)!; Document.Resize(id, node.Width + dx, node.Height + dy, SnapSize); }
                else Document.Move(dx, dy, SnapSize);
            }
        }
        catch (InvalidOperationException ex) { Status?.Invoke(ex.Message); }
        CancelDrag(); RefreshSelection(); e.Handled = true;
    }
    private static RectD Normalize(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    private void CancelDrag() { _start = null; _resize = _marquee = false; _adorner.Delta = default; _adorner.Resizing = false; _adorner.Marquee = null; _adorner.Invalidate(); }
    public void Dispose() => Host.Dispose();

    private partial class Ruler : SKCanvasElement
    {
        public bool Vertical { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            using var p = new CanvasPainter(canvas); p.Rect(SKRect.Create((float)area.Width, (float)area.Height), SKColor.Parse("#DEE4E8"));
            var length = Vertical ? area.Height : area.Width;
            for (var i = 0; i < length; i += 10)
            {
                var major = i % 100 == 0; var tick = major ? 10 : i % 50 == 0 ? 6 : 3;
                if (Vertical) { p.Line(20 - tick, i, 20, i, SKColor.Parse("#87949C")); if (major) { canvas.Save(); canvas.Translate(10, i + 2); canvas.RotateDegrees(-90); p.Text(i.ToString(), 0, 0, 9, SKColor.Parse("#5C6D76"), SKTextAlign.Right); canvas.Restore(); } }
                else { p.Line(i, 20 - tick, i, 20, SKColor.Parse("#87949C")); if (major) p.Text(i.ToString(), i + 3, 10, 9, SKColor.Parse("#5C6D76")); }
            }
        }
    }
    private partial class DesignAdorner : SKCanvasElement
    {
        public DesignerDocument? Document { get; set; }
        public ComponentNode? Root { get; set; }
        public Point Delta { get; set; }
        public bool Resizing { get; set; }
        public bool ShowGrid { get; set; }
        public RectD? Marquee { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            if (Document is null || Root is null) return;
            using var p = new CanvasPainter(canvas);
            if (ShowGrid) for (var y = 0; y < area.Height; y += 16) for (var x = 0; x < area.Width; x += 16) p.Circle(x, y, .75f, SKColor.Parse("#91A4AD").WithAlpha(75));
            var slots = LayoutEngine.Flatten(Root, area.Width, area.Height);
            foreach (var slot in slots.Where(s => Document.Selection.Contains(s.Node.Id)))
            {
                var b = slot.Bounds;
                var color = SKColor.Parse(slot.Node.Locked ? "#8A9398" : "#069CCC");
                canvas.Save();
                if (slot.Node.Rotation != 0) { canvas.Translate((float)(b.X + b.Width / 2), (float)(b.Y + b.Height / 2)); canvas.RotateDegrees((float)slot.Node.Rotation); canvas.Translate((float)(-b.X - b.Width / 2), (float)(-b.Y - b.Height / 2)); }
                var bounds = SKRect.Create((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height);
                p.Rect(bounds, color, 0, true, 1.4f);
                foreach (var (x, y) in new[] { (bounds.Left, bounds.Top), (bounds.Right, bounds.Top), (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom) }) { p.Rect(SKRect.Create(x - 3, y - 3, 6, 6), SKColors.White); p.Rect(SKRect.Create(x - 3, y - 3, 6, 6), color, 0, true); }
                canvas.Restore();
                if (!slot.Node.Locked && (Delta.X != 0 || Delta.Y != 0))
                {
                    var preview = Resizing ? SKRect.Create(bounds.Left, bounds.Top, Math.Max(16, bounds.Width + (float)Delta.X), Math.Max(16, bounds.Height + (float)Delta.Y)) : SKRect.Create(bounds.Left + (float)Delta.X, bounds.Top + (float)Delta.Y, bounds.Width, bounds.Height);
                    p.Rect(preview, color.WithAlpha(30)); p.Rect(preview, color, 0, true, 2);
                }
            }
            if (Marquee is { } box) { var rect = SKRect.Create((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height); p.Rect(rect, SKColor.Parse("#20A0CD").WithAlpha(25)); p.Rect(rect, SKColor.Parse("#20A0CD"), 0, true); }
        }
    }
}
