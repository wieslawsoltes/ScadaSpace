using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;

namespace ScadaSpace.Controls;

public partial class ViewHost
{
    private sealed record EmbeddedSlot(string Path, ViewHost Host, Viewbox Box, Dictionary<string, Scalar> Inputs);
    private FrameworkElement BuildEmbedded(ComponentNode node)
    {
        var panel = new Grid(); var slots = new Dictionary<int, EmbeddedSlot>();
        _refresh.Add(() =>
        {
            var path = Text(node, "view");
            var instances = Value(node, "instances");
            var count = node.Type == "EmbeddedView" ? 1 : instances.Kind == ScalarKind.Array ? instances.Items.Count : (int)Number(node, "count", 1);
            if (count is < 0 or > 100) throw new InvalidOperationException("Repeaters support 0–100 instances.");
            if (Runtime?.Project.Views.Any(v => v.Path == path) != true) throw new InvalidOperationException("Unknown embedded view: " + path);
            foreach (var index in slots.Keys.Where(i => i >= count || slots[i].Path != path).ToArray())
            {
                var slot = slots[index]; panel.Children.Remove(slot.Box); _embedded.Remove(slot.Host); slot.Host.Dispose(); slots.Remove(index);
            }
            while (panel.ColumnDefinitions.Count > count) panel.ColumnDefinitions.RemoveAt(panel.ColumnDefinitions.Count - 1);
            while (panel.ColumnDefinitions.Count < count) panel.ColumnDefinitions.Add(new());
            for (var i = 0; i < count; i++)
            {
                var inputs = new Dictionary<string, Scalar>(StringComparer.Ordinal);
                foreach (var name in node.Props.Keys.Where(k => k.StartsWith("param.", StringComparison.Ordinal))) inputs[name[6..]] = Value(node, name);
                var parameters = Value(node, "params");
                if (parameters.Kind == ScalarKind.Object) foreach (var pair in parameters.Fields) inputs[pair.Key] = pair.Value;
                if (node.Type == "FlexRepeater")
                {
                    if (instances.Kind == ScalarKind.Array)
                    {
                        if (instances.Items[i].Kind != ScalarKind.Object) throw new FormatException("Each repeater instance must be a parameter object.");
                        foreach (var pair in instances.Items[i].Fields) inputs[pair.Key] = pair.Value;
                    }
                    inputs["index"] = Scalar.From(i);
                }
                if (!slots.TryGetValue(i, out var existing))
                {
                    var child = new ViewHost();
                    child.Error += message => Error?.Invoke(message); child.ValueChanged += () => ValueChanged?.Invoke();
                    try { child.Load(Runtime!, path, inputs, IsEditing, Session, _depth + 1, _ancestry); }
                    catch { child.Dispose(); throw; }
                    var box = new Viewbox { Child = child, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
                    Grid.SetColumn(box, i); panel.Children.Add(box); _embedded.Add(child);
                    existing = new(path, child, box, inputs); slots.Add(i, existing);
                }
                else
                {
                    // Only externally changed inputs are applied; local child state survives parent refresh.
                    foreach (var (key, value) in inputs)
                        if (!existing.Inputs.TryGetValue(key, out var old) || old != value) existing.Host.Instance!.WriteParameter(key, value);
                    foreach (var key in existing.Inputs.Keys.Where(k => !inputs.ContainsKey(k)))
                    {
                        if (existing.Host.Instance!.View.Parameters.TryGetValue(key, out var fallback)) existing.Host.Instance.WriteParameter(key, fallback);
                        else existing.Host.Instance.Parameters.Remove(key);
                    }
                    slots[i] = existing with { Inputs = inputs };
                }
                existing.Box.Margin = new Thickness(Math.Max(0, Number(node, "gap", 4)) / 2);
            }
        });
        return panel;
    }
}
