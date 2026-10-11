using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ScadaSpace.Controls;

public partial class DesignerSurface
{
    private Action<string>? _contextCommand;
    private readonly Dictionary<string, FrameworkElement> _contextItems = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FrameworkElement> ContextMenuItems => _contextItems;
    public void ConfigureContextMenu(Action<string> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var first = _contextCommand is null; _contextCommand = command;
        if (!first) return;
        _adorner.RightTapped += (_, e) =>
        {
            if (Preview || Document is null) return;
            var position = e.GetPosition(_adorner); var hit = Hit(position);
            if (hit is not null && !Document.Selection.Contains(hit.Node.Id)) { Document.Select(hit.Node.Id); RefreshSelection(); }
            var menu = new MenuFlyout(); _contextItems.Clear();
            foreach (var (id, label) in new[] { ("cut", "Cut"), ("copy", "Copy"), ("paste", "Paste"), ("duplicate", "Duplicate"), ("delete", "Delete"), ("events", "Configure Events…"), ("configurations", "Configuration Explorer") })
            {
                if (id == "events") menu.Items.Add(new MenuFlyoutSeparator());
                var item = new MenuFlyoutItem { Text = label, IsEnabled = id is "paste" or "configurations" || Document.Selection.Count > 0 };
                item.Click += (_, _) => _contextCommand?.Invoke(id); menu.Items.Add(item); _contextItems[id] = item;
            }
            menu.ShowAt(_adorner, new FlyoutShowOptions { Position = position }); e.Handled = true;
        };
    }
}
