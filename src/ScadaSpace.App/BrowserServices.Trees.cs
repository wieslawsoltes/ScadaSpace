using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Controls;
using Windows.Foundation;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    private static object ObserveTree(HmiTree tree)
    {
        static object? Bounds(FrameworkElement? element)
        {
            if (element is null || !element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
            var r = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return new { x = r.X, y = r.Y, width = r.Width, height = r.Height };
        }
        static Button? Toggle(DependencyObject element)
        {
            if (element is Button button) return button;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) if (Toggle(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
            return null;
        }
        return new
        {
            count = tree.ItemCount, visibleCount = tree.VisibleCount, rebuilds = tree.RebuildCount,
            selection = tree.CurrentPath, selectedPaths = tree.SelectedPaths, selectionData = tree.SelectionData,
            enabled = tree.IsEnabled, error = tree.DataError, filter = Bounds(tree.FilterInput), filterText = tree.Filter,
            lastKey = tree.LastNavigationKey, focusedPath = tree.FocusedPath,
            rows = tree.RealizedRows.OrderBy(c => c.Row!.Path, StringComparer.Ordinal).Select(container =>
            {
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(container);
                return new { path = container.Row!.Path, label = container.Row.Label, expanded = container.Row.Row.Expanded,
                    depth = container.Row.Row.Item.Depth, bounds = Bounds(container), toggle = Bounds(Toggle(container)),
                    role = peer?.GetAutomationControlType().ToString(), expansion = (peer?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider)?.ExpandCollapseState.ToString() };
            }).ToArray()
        };
    }
}
