using ScadaSpace.Core;

namespace ScadaSpace.Layout;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    public bool Intersects(RectD other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    public RectD Offset(double x, double y) => this with { X = X + x, Y = Y + y };
}
public sealed record LayoutSlot(ComponentNode Node, RectD Bounds, bool Visible = true);

/// <summary>Platform-neutral layout for coordinate, weighted flex, responsive columns, breakpoints and tabs.</summary>
public static class LayoutEngine
{
    public static IReadOnlyList<LayoutSlot> Arrange(ComponentNode container, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        var padding = Math.Max(0, container.Number("padding"));
        var gap = Math.Max(0, container.Number("gap", 8));
        var innerWidth = Math.Max(0, width - padding * 2);
        var innerHeight = Math.Max(0, height - padding * 2);
        var children = container.Children;
        var layout = container.Layout;
        if (layout == LayoutKind.Breakpoint) layout = width < container.Number("breakpoint", 600) ? LayoutKind.FlexColumn : LayoutKind.FlexRow;
        var result = new List<LayoutSlot>(children.Count);
        if (layout == LayoutKind.Coordinate)
        {
            var percent = container.Flag("percentCoordinates");
            foreach (var child in children)
                result.Add(new(child, percent ? new(padding + child.X * innerWidth, padding + child.Y * innerHeight, child.Width * innerWidth, child.Height * innerHeight) : new(padding + child.X, padding + child.Y, child.Width, child.Height), child.Flag("visible", true)));
            return result;
        }
        if (layout == LayoutKind.Tab)
        {
            var active = Math.Clamp((int)container.Number("activeTab"), 0, Math.Max(0, children.Count - 1));
            for (var i = 0; i < children.Count; i++) result.Add(new(children[i], new(padding, padding, innerWidth, innerHeight), i == active));
            return result;
        }
        if (layout == LayoutKind.Column)
        {
            var columns = Math.Clamp((int)container.Number("columns", 2), 1, 64);
            var minimum = Math.Max(0, container.Number("minimumColumnWidth"));
            if (minimum > 0) columns = Math.Min(columns, Math.Max(1, (int)((innerWidth + gap) / (minimum + gap))));
            var cellWidth = Math.Max(0, (innerWidth - gap * (columns - 1)) / columns);
            var y = padding;
            for (var row = 0; row < children.Count; row += columns)
            {
                var rowNodes = children.Skip(row).Take(columns).ToArray();
                var rowHeight = rowNodes.Max(n => n.Height);
                for (var i = 0; i < rowNodes.Length; i++) result.Add(new(rowNodes[i], new(padding + i * (cellWidth + gap), y, cellWidth, rowNodes[i].Flag("stretch", true) ? rowHeight : rowNodes[i].Height), rowNodes[i].Flag("visible", true)));
                y += rowHeight + gap;
            }
            return result;
        }
        var horizontal = layout == LayoutKind.FlexRow;
        var visible = children.Where(n => n.Flag("visible", true)).ToArray();
        var total = Math.Max(0, (horizontal ? innerWidth : innerHeight) - Math.Max(0, visible.Length - 1) * gap);
        var bases = visible.Select(n => Math.Max(0, n.Number("basis", horizontal ? n.Width : n.Height))).ToArray();
        var growth = visible.Select(n => Math.Max(0, n.Number("grow", 1))).ToArray();
        var sum = bases.Sum(); var grow = growth.Sum(); var offset = padding;
        for (var i = 0; i < visible.Length; i++)
        {
            var length = sum > total && sum > 0 ? bases[i] * total / sum : bases[i] + (grow > 0 ? (total - sum) * growth[i] / grow : 0);
            var node = visible[i];
            var cross = node.Flag("stretch", true) ? horizontal ? innerHeight : innerWidth : horizontal ? node.Height : node.Width;
            result.Add(new(node, horizontal ? new(offset, padding, length, cross) : new(padding, offset, cross, length)));
            offset += length + gap;
        }
        foreach (var node in children.Where(n => !n.Flag("visible", true))) result.Add(new(node, new(0, 0, 0, 0), false));
        return result;
    }
    public static IReadOnlyList<LayoutSlot> Flatten(ComponentNode root, double width, double height)
    {
        var result = new List<LayoutSlot>();
        void Walk(ComponentNode parent, double x, double y, double w, double h, int depth)
        {
            if (depth > 24) throw new InvalidOperationException("Layout nesting limit exceeded.");
            foreach (var slot in Arrange(parent, w, h))
            {
                if (!slot.Visible) continue;
                var bounds = slot.Bounds.Offset(x, y); result.Add(slot with { Bounds = bounds });
                if (slot.Node.Type == "Container") Walk(slot.Node, bounds.X, bounds.Y, bounds.Width, bounds.Height, depth + 1);
            }
        }
        Walk(root, 0, 0, width, height, 0);
        return result;
    }
    public static LayoutSlot? HitTest(IReadOnlyList<LayoutSlot> slots, double x, double y) => slots.Reverse().FirstOrDefault(s => s.Visible && s.Bounds.Contains(x, y));
}
