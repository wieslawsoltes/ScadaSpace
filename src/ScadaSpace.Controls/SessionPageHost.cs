using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;
using ScadaSpace.Layout;
using ScadaSpace.Runtime;
using Windows.Foundation;
using Windows.System;

namespace ScadaSpace.Controls;

/// <summary>Reusable runtime page composition: primary view, edge docks, resize handles and page-local modality.</summary>
public partial class SessionPageHost : Grid, IDisposable
{
    private sealed record Region(Guid Id, ViewHost Host, ContentControl Frame, Button Handle, DockSplitter Splitter)
    {
        public IReadOnlyDictionary<string, Scalar> Inputs { get; set; } = new Dictionary<string, Scalar>();
    }
    private readonly Canvas _canvas = new();
    private readonly ContentControl _primary;
    private readonly Border _shade = new() { Background = ControlTheme.Brush("#99000000"), Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Region> _regions = new(StringComparer.Ordinal);
    private RuntimeSession? _session;
    private ProjectRuntime? _runtime;
    private Guid _entry;
    private bool _refreshing;
    public ViewHost PrimaryHost { get; } = new();
    public IReadOnlyDictionary<string, ViewHost> DockHosts => _regions.ToDictionary(p => p.Key, p => p.Value.Host, StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> DockHandles => _regions.ToDictionary(p => p.Key, p => (FrameworkElement)p.Value.Handle, StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> DockSplitters => _regions.ToDictionary(p => p.Key, p => (FrameworkElement)p.Value.Splitter, StringComparer.Ordinal);
    public PageLayoutResult? Layout { get; private set; }
    public event Action<string>? Error;
    public event Action? ValueChanged;
    public SessionPageHost()
    {
        _primary = Frame(PrimaryHost); _canvas.Children.Add(_primary); _canvas.Children.Add(_shade); Children.Add(_canvas);
        Background = ControlTheme.Brush("#E5ECEF");
        PrimaryHost.Error += message => Error?.Invoke(message); PrimaryHost.ValueChanged += () => ValueChanged?.Invoke();
        SizeChanged += (_, _) => { _session?.SetViewportWidth(ActualWidth); Refresh(); };
        _shade.PointerPressed += (_, e) => { CloseModal(); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { CloseModal(); e.Handled = true; } };
    }
    private static ContentControl Frame(ViewHost host) => new()
    {
        Content = new Viewbox { Child = host, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch },
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
        Padding = new Thickness(0), Background = ControlTheme.Brush("#F4F7F8"), IsTabStop = false
    };
    public void Load(ProjectRuntime runtime, RuntimeSession session)
    {
        Detach(); _runtime = runtime; _session = session; _entry = Guid.Empty;
        session.SetViewportWidth(ActualWidth > 0 ? ActualWidth : 1024);
        session.Changed += Refresh; Refresh();
    }
    private void CloseModal()
    {
        var dock = _session?.Docks.LastOrDefault(d => d.IsOpen && d.Definition.Modal);
        if (dock is not null) _session!.CloseDock(dock.Definition.Id);
    }
    private Scalar PageContext() => Scalar.Object(new Dictionary<string, Scalar>
    {
        ["path"] = Scalar.From(_session!.CurrentPath), ["urlParams"] = Scalar.Object(_session.Current!.Address.Query)
    });
    public void Refresh()
    {
        if (_refreshing || _runtime is null || _session?.Current is not { } current) return;
        _refreshing = true;
        try
        {
            if (_entry != current.EntryId)
            {
                PrimaryHost.Load(_runtime, current.Page.View, current.Parameters, session: _session);
                _entry = current.EntryId;
            }
            var docks = _session.Docks;
            foreach (var key in _regions.Keys.Where(k => !docks.Any(d => d.Definition.Id == k && d.InstanceId == _regions[k].Id)).ToArray()) Remove(key);
            foreach (var dock in docks)
            {
                var id = dock.Definition.Id;
                if (!_regions.TryGetValue(id, out var region))
                {
                    var host = new ViewHost(); host.Load(_runtime, dock.Definition.View, dock.Parameters, session: _session);
                    host.Error += message => Error?.Invoke(message); host.ValueChanged += () => ValueChanged?.Invoke();
                    var frame = Frame(host);
                    var handle = ControlTheme.Button(id, () => _session?.ToggleDock(id));
                    AutomationProperties.SetAutomationId(handle, "dock-handle-" + id);
                    var splitter = new DockSplitter { Vertical = dock.Definition.Edge is DockEdge.North or DockEdge.South };
                    AutomationProperties.SetAutomationId(splitter, "dock-splitter-" + id);
                    splitter.DragDelta += delta =>
                    {
                        var state = _session?.Docks.FirstOrDefault(d => d.Definition.Id == id);
                        if (state is not null) _session!.ResizeDock(id, state.Size + (state.Definition.Edge is DockEdge.South or DockEdge.East ? -delta : delta));
                    };
                    region = new(dock.InstanceId, host, frame, handle, splitter) { Inputs = dock.Parameters };
                    _regions.Add(id, region); _canvas.Children.Add(frame); _canvas.Children.Add(handle); _canvas.Children.Add(splitter);
                }
                foreach (var (key, value) in dock.Parameters)
                    if (!region.Inputs.TryGetValue(key, out var previous) || previous != value) region.Host.Instance!.WriteParameter(key, value);
                region.Inputs = dock.Parameters;
            }
            var context = PageContext();
            void RefreshHost(ViewHost host)
            {
                host.Instance?.SetPageProperties(context);
                foreach (var child in host.EmbeddedHosts) RefreshHost(child);
                host.Refresh();
            }
            RefreshHost(PrimaryHost);
            foreach (var dock in docks.Where(d => d.IsOpen)) RefreshHost(_regions[dock.Definition.Id].Host);
            ArrangeRegions(docks);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { Error?.Invoke(ex.Message); }
        finally { _refreshing = false; }
    }
    private void ArrangeRegions(IReadOnlyList<DockSnapshot> docks)
    {
        var width = ActualWidth; var height = ActualHeight;
        Layout = PageLayout.Arrange(docks.Select(d => new DockLayoutInput(d.Definition.Id, d.Definition.Edge, d.Size, d.IsOpen, d.PushesContent)), width, height, _session!.CornerPriority);
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        Place(_primary, Layout.Primary);
        var modal = docks.LastOrDefault(d => d.IsOpen && d.Definition.Modal);
        _primary.IsEnabled = modal is null;
        _shade.Visibility = modal is null ? Visibility.Collapsed : Visibility.Visible;
        Place(_shade, new(0, 0, width, height)); Canvas.SetZIndex(_shade, 20);
        foreach (var dock in docks)
        {
            var region = _regions[dock.Definition.Id]; var definition = dock.Definition;
            var active = modal is null || modal.InstanceId == dock.InstanceId;
            region.Frame.Visibility = dock.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            region.Frame.IsEnabled = active; region.Handle.IsEnabled = active;
            var showHandle = definition.ShowHandle && definition.Display != DockDisplay.Visible;
            region.Handle.Visibility = showHandle ? Visibility.Visible : Visibility.Collapsed;
            region.Handle.Content = (dock.IsOpen ? "− " : "+ ") + definition.Id;
            region.Splitter.Visibility = dock.IsOpen && definition.Resizable && active ? Visibility.Visible : Visibility.Collapsed;
            var z = modal?.InstanceId == dock.InstanceId ? 30 : dock.PushesContent ? 1 : 10;
            Canvas.SetZIndex(region.Frame, z); Canvas.SetZIndex(region.Handle, z + 2); Canvas.SetZIndex(region.Splitter, z + 1);
            var box = Layout.Docks.GetValueOrDefault(definition.Id, new RectD(0, 0, 0, 0));
            if (dock.IsOpen) Place(region.Frame, box);
            const double handleWidth = 96, handleHeight = 28, grip = 6;
            var h = definition.Edge switch
            {
                DockEdge.North => new RectD(Math.Max(0, (width - handleWidth) / 2), dock.IsOpen ? box.Bottom : 0, Math.Min(width, handleWidth), Math.Min(height, handleHeight)),
                DockEdge.South => new RectD(Math.Max(0, (width - handleWidth) / 2), Math.Max(0, (dock.IsOpen ? box.Y : height) - handleHeight), Math.Min(width, handleWidth), Math.Min(height, handleHeight)),
                DockEdge.West => new RectD(dock.IsOpen ? box.Right : 0, Math.Max(0, (height - handleHeight) / 2), Math.Min(width, handleWidth), Math.Min(height, handleHeight)),
                _ => new RectD(Math.Max(0, (dock.IsOpen ? box.X : width) - handleWidth), Math.Max(0, (height - handleHeight) / 2), Math.Min(width, handleWidth), Math.Min(height, handleHeight))
            };
            Place(region.Handle, h);
            var splitter = definition.Edge switch
            {
                DockEdge.North => new RectD(box.X, Math.Max(box.Y, box.Bottom - grip), box.Width, Math.Min(grip, box.Height)),
                DockEdge.South => new RectD(box.X, box.Y, box.Width, Math.Min(grip, box.Height)),
                DockEdge.West => new RectD(Math.Max(box.X, box.Right - grip), box.Y, Math.Min(grip, box.Width), box.Height),
                _ => new RectD(box.X, box.Y, Math.Min(grip, box.Width), box.Height)
            };
            Place(region.Splitter, splitter);
        }
    }
    private static void Place(FrameworkElement element, RectD box)
    { Canvas.SetLeft(element, box.X); Canvas.SetTop(element, box.Y); element.Width = box.Width; element.Height = box.Height; }
    private void Remove(string id)
    {
        var region = _regions[id]; region.Host.Dispose();
        _canvas.Children.Remove(region.Frame); _canvas.Children.Remove(region.Handle); _canvas.Children.Remove(region.Splitter); _regions.Remove(id);
    }
    private void Detach()
    {
        if (_session is not null) _session.Changed -= Refresh;
        foreach (var id in _regions.Keys.ToArray()) Remove(id);
        PrimaryHost.Dispose(); _session = null; _runtime = null;
    }
    public new void Dispose() => Detach();
}
