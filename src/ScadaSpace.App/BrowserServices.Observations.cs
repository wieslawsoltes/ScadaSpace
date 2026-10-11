using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Controls;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using Windows.Foundation;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    private sealed class InstanceIdentity { public string Id { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<ViewInstance, InstanceIdentity> Identities = new();
    private static WeakReference<ViewHost>? _observedRoot;
    private static void PublishViewObservations()
    {
#if __WASM__
        if (_observedRoot is null || !_observedRoot.TryGetTarget(out var host) || !host.IsLoaded)
        {
            host = null;
            if (App.MainWindow?.Content is DependencyObject root)
            {
                var stack = new Stack<DependencyObject>(); stack.Push(root); var budget = 10000;
                while (stack.Count > 0 && budget-- > 0)
                {
                    var current = stack.Pop();
                    if (current is ViewHost candidate && candidate.Instance is not null) { host = candidate; break; }
                    for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
            if (host is null) return;
            _observedRoot = new(host);
        }
        var views = new List<object>();
        object? Bounds(FrameworkElement element)
        {
            if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
            var rect = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height };
        }
        void Observe(ViewHost current)
        {
            if (current.Instance is not { } instance || views.Count >= 256) return;
            var components = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var node in instance.View.Root.DescendantsAndSelf())
            {
                if (!current.Elements.TryGetValue(node.Id, out var element)) continue;
                components[node.Name] = new
                {
                    bounds = Bounds(element), value = current.Read(node, "value"), text = current.Read(node, "text"),
                    rows = element is HmiDataTable table ? table.VisibleRows.Take(20).Select(r => new { key = r.Key, cells = r.Cells.ToArray() }).ToArray() : null,
                    tableError = element is HmiDataTable dataTable ? dataTable.DataError : null,
                    tree = element is HmiTree tree ? ObserveTree(tree) : null
                };
            }
            views.Add(new { id = Identities.GetValue(instance, _ => new()).Id, view = instance.View.Path, evaluations = instance.EvaluationCount, bindingIndexBuilds = instance.BindingIndexBuildCount, historyReads = instance.HistoryQueryReads, historyHits = instance.HistoryQueryHits, parameters = instance.Parameters, components, diagnostics = instance.Diagnostics.ToArray() });
            foreach (var embedded in current.EmbeddedHosts) Observe(embedded);
        }
        Observe(host);
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.scadaViews=" + JsonSerializer.Serialize(views, ProjectCodec.Options) + "; '';");
#endif
    }
}
