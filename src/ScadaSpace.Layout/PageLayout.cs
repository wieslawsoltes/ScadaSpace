using ScadaSpace.Core;

namespace ScadaSpace.Layout;

public sealed record DockLayoutInput(string Id, DockEdge Edge, double Size, bool IsOpen, bool PushesContent);
public sealed record PageLayoutResult(RectD Primary, IReadOnlyDictionary<string, RectD> Docks);

/// <summary>Pure page-edge layout. Opposing docks share constrained space proportionally; the center never has negative size.</summary>
public static class PageLayout
{
    public static PageLayoutResult Arrange(IEnumerable<DockLayoutInput> input, double width, double height, CornerPriority priority)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 || !Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(width));
        var all = input.ToArray();
        if (all.Length > 4 || all.Select(d => d.Edge).Distinct().Count() != all.Length || all.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != all.Length || all.Any(d => string.IsNullOrEmpty(d.Id) || !Enum.IsDefined(d.Edge) || !double.IsFinite(d.Size) || d.Size < 0)) throw new ArgumentException("Invalid dock layout inputs.");
        var docks = all.Where(d => d.IsOpen).ToDictionary(d => d.Edge);
        double Size(DockEdge edge) => docks.TryGetValue(edge, out var d) ? d.Size : 0;
        static (double First, double Second) Fit(double a, double b, double available)
        {
            var capacity = Math.Max(0, available - Math.Min(32, available));
            var total = a + b;
            return total > capacity && total > 0 ? (a / total * capacity, b / total * capacity) : (a, b);
        }
        var (n, s) = Fit(Size(DockEdge.North), Size(DockEdge.South), height);
        var (w, e) = Fit(Size(DockEdge.West), Size(DockEdge.East), width);
        double Push(DockEdge edge, double size) => docks.TryGetValue(edge, out var d) && d.PushesContent ? size : 0;
        var pn = Push(DockEdge.North, n); var ps = Push(DockEdge.South, s); var pw = Push(DockEdge.West, w); var pe = Push(DockEdge.East, e);
        var primary = new RectD(pw, pn, Math.Max(0, width - pw - pe), Math.Max(0, height - pn - ps));
        var topBottom = priority == CornerPriority.TopBottom;
        var regions = new Dictionary<DockEdge, RectD>
        {
            [DockEdge.North] = new(topBottom ? 0 : w, 0, topBottom ? width : Math.Max(0, width - w - e), n),
            [DockEdge.South] = new(topBottom ? 0 : w, height - s, topBottom ? width : Math.Max(0, width - w - e), s),
            [DockEdge.West] = new(0, topBottom ? n : 0, w, topBottom ? Math.Max(0, height - n - s) : height),
            [DockEdge.East] = new(width - e, topBottom ? n : 0, e, topBottom ? Math.Max(0, height - n - s) : height)
        };
        return new(primary, new System.Collections.ObjectModel.ReadOnlyDictionary<string, RectD>(docks.Values.ToDictionary(d => d.Id, d => regions[d.Edge], StringComparer.Ordinal)));
    }
}
