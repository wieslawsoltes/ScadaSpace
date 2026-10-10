using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Graphics;
using ScadaSpace.Core;
using ScadaSpace.Data;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ScadaSpace.Controls;

public sealed record TableColumn(string Key, string Title, double Weight = 1);
public sealed record TableRow(string Key, IReadOnlyList<string> Cells, string? Accent = null, IReadOnlyList<Scalar>? Values = null);

/// <summary>Virtualized drawing table. Only visible rows are submitted to Skia; filtering and sorting do not rebuild controls.</summary>
public partial class HmiDataTable : Grid
{
    private readonly TableSurface _surface = new();
    private readonly TextBox _filter = new() { PlaceholderText = "Filter…", FontSize = 11, MinHeight = 26, Padding = new Thickness(6, 2, 6, 2) };
    private readonly TextBlock _count = ControlTheme.Text("0 rows", 10);
    private readonly StackPanel _commands = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    public event Action<string?>? SelectionChanged;
    public event Action<string, int>? CellEditRequested;
    public IReadOnlyList<TableRow> VisibleRows => _surface.VisibleRows;
    public string? SelectedKey => _surface.SelectedKey;
    public IReadOnlyList<TableRow> Rows => _surface.Rows;
    public HmiDataTable()
    {
        Background = ControlTheme.Brush("#FFFFFF"); BorderBrush = ControlTheme.Brush("#CFDADF"); BorderThickness = new Thickness(1);
        RowDefinitions.Add(new() { Height = new GridLength(29) }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = new GridLength(28) });
        Children.Add(_filter); Grid.SetRow(_surface, 1); Children.Add(_surface);
        var footer = new Grid { Padding = new Thickness(5, 1, 5, 1), Background = ControlTheme.Brush("#F0F3F5") };
        footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(_count); Grid.SetColumn(_commands, 1); footer.Children.Add(_commands); Grid.SetRow(footer, 2); Children.Add(footer);
        _filter.TextChanged += (_, _) => { _surface.Filter = _filter.Text; _surface.Refresh(); _count.Text = $"{_surface.VisibleCount} rows"; };
        _surface.SelectionChanged += key => SelectionChanged?.Invoke(key);
        _surface.CellEditRequested += (key, column) => CellEditRequested?.Invoke(key, column);
        _surface.ScrollChanged += () => _count.Text = $"{_surface.VisibleCount} rows · scroll to browse";
    }
    public void AddCommand(string caption, Action<string> action)
    {
        _commands.Children.Add(ControlTheme.Button(caption, () => { if (SelectedKey is { } key) action(key); }));
    }
    public void SetData(IReadOnlyList<TableColumn> columns, IReadOnlyList<TableRow> rows)
    { _surface.Columns = columns; _surface.Rows = rows; _surface.Refresh(); _count.Text = $"{_surface.VisibleCount} rows"; }
    public void SetFilter(string text) { if (_filter.Text != text) _filter.Text = text; }
    public void Select(string key) { _surface.Select(key); }
    private partial class TableSurface : SKCanvasElement
    {
        private const float HeaderHeight = 27;
        private const float RowHeight = 26;
        private int _offset;
        private int _sort = -1;
        private bool _descending;
        private TableRow[] _visible = [];
        private float[] _edges = [];
        public IReadOnlyList<TableColumn> Columns { get; set; } = [];
        public IReadOnlyList<TableRow> Rows { get; set; } = [];
        public string Filter { get; set; } = "";
        public string? SelectedKey { get; private set; }
        public IReadOnlyList<TableRow> VisibleRows => Array.AsReadOnly(_visible);
        public event Action<string, int>? CellEditRequested;
        public int VisibleCount => _visible.Length;
        public event Action<string?>? SelectionChanged;
        public event Action? ScrollChanged;
        public TableSurface()
        {
            PointerPressed += (_, e) =>
            {
                var point = e.GetCurrentPoint(this).Position;
                if (point.Y < HeaderHeight)
                {
                    var column = Array.FindIndex(_edges, x => point.X < x);
                    if (column >= 0) { _descending = column == _sort && !_descending; _sort = column; Refresh(); }
                }
                else
                {
                    var index = _offset + (int)((point.Y - HeaderHeight) / RowHeight);
                    if (index >= 0 && index < _visible.Length) Select(_visible[index].Key);
                }
                e.Handled = true;
            };
            DoubleTapped += (_, e) =>
            {
                var point = e.GetPosition(this);
                var index = _offset + (int)((point.Y - HeaderHeight) / RowHeight);
                var column = Array.FindIndex(_edges, x => point.X < x);
                if (point.Y >= HeaderHeight && index >= 0 && index < _visible.Length && column >= 0)
                    CellEditRequested?.Invoke(_visible[index].Key, column);
                e.Handled = true;
            };
            PointerWheelChanged += (_, e) =>
            {
                _offset = Math.Clamp(_offset - Math.Sign(e.GetCurrentPoint(this).Properties.MouseWheelDelta) * 3, 0, MaxOffset());
                Invalidate(); ScrollChanged?.Invoke(); e.Handled = true;
            };
            SizeChanged += (_, _) => { _offset = Math.Min(_offset, MaxOffset()); Invalidate(); };
        }
        private int MaxOffset() => Math.Max(0, _visible.Length - Math.Max(1, (int)((ActualHeight - HeaderHeight) / RowHeight)));
        public void Refresh()
        {
            IEnumerable<TableRow> query = Rows;
            if (!string.IsNullOrWhiteSpace(Filter)) query = query.Where(r => r.Cells.Any(c => c.Contains(Filter, StringComparison.OrdinalIgnoreCase)));
            if (_sort >= Columns.Count) _sort = -1;
            Scalar SortValue(TableRow row) => row.Values is { } values && _sort < values.Count ? values[_sort] : Scalar.From(row.Cells.ElementAtOrDefault(_sort));
            if (_sort >= 0) query = _descending ? query.OrderByDescending(SortValue, PropertyValueComparer.Instance) : query.OrderBy(SortValue, PropertyValueComparer.Instance);
            _visible = query.ToArray(); _offset = Math.Min(_offset, MaxOffset());
            if (SelectedKey is not null && !Rows.Any(r => r.Key == SelectedKey)) SelectedKey = null;
            Invalidate();
        }
        public void Select(string key) { SelectedKey = key; SelectionChanged?.Invoke(key); Invalidate(); }
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            using var p = new CanvasPainter(canvas);
            var width = (float)area.Width; var height = (float)area.Height;
            p.Rect(SKRect.Create(width, height), SKColors.White);
            p.Rect(SKRect.Create(width, HeaderHeight), SKColor.Parse("#E8EEF1"));
            var total = Math.Max(1, Columns.Sum(c => c.Weight));
            var left = 0f; _edges = new float[Columns.Count];
            for (var c = 0; c < Columns.Count; c++)
            {
                var cellWidth = (float)(width * Columns[c].Weight / total); _edges[c] = left + cellWidth;
                canvas.Save(); canvas.ClipRect(SKRect.Create(left, 0, cellWidth - 2, HeaderHeight));
                p.Text(Columns[c].Title + (c == _sort ? _descending ? " ▼" : " ▲" : ""), left + 7, 18, 11, SKColor.Parse("#354B58")); canvas.Restore();
                left += cellWidth;
            }
            var count = Math.Min(_visible.Length - _offset, (int)Math.Ceiling(Math.Max(0, height - HeaderHeight) / RowHeight));
            for (var i = 0; i < count; i++)
            {
                var row = _visible[i + _offset]; var y = HeaderHeight + i * RowHeight;
                var selected = row.Key == SelectedKey;
                p.Rect(SKRect.Create(0, y, width, RowHeight), SKColor.Parse(selected ? "#CEEAF3" : i % 2 == 0 ? "#FFFFFF" : "#F5F8FA"));
                if (row.Accent is not null) p.Rect(SKRect.Create(0, y + 2, 3, RowHeight - 4), CanvasPainter.Color(row.Accent));
                left = 0;
                for (var c = 0; c < Columns.Count; c++)
                {
                    canvas.Save(); canvas.ClipRect(SKRect.Create(left + 2, y, _edges[c] - left - 5, RowHeight));
                    p.Text(row.Cells.ElementAtOrDefault(c) ?? "", left + 7, y + 17, 11, SKColor.Parse("#354B58")); canvas.Restore(); left = _edges[c];
                }
                p.Line(0, y + RowHeight, width, y + RowHeight, SKColor.Parse("#E4EAED"));
            }
            if (_visible.Length == 0) p.Text("No matching records", width / 2, Math.Max(HeaderHeight + 25, height / 2), 12, SKColor.Parse("#81929B"), SKTextAlign.Center);
            if (MaxOffset() > 0)
            {
                var barHeight = Math.Max(16, (height - HeaderHeight) * Math.Max(1, count) / _visible.Length);
                var y = HeaderHeight + (height - HeaderHeight - barHeight) * _offset / MaxOffset();
                p.Rect(SKRect.Create(width - 4, y, 3, barHeight), SKColor.Parse("#91A6B2"), 2);
            }
        }
    }
}
