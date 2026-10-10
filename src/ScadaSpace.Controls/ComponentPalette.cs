using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using Windows.ApplicationModel.DataTransfer;

namespace ScadaSpace.Controls;

public partial class ComponentPalette : Grid
{
    private readonly TextBox _search = new() { PlaceholderText = "Search components", FontSize = 12, MinHeight = 29, Padding = new Thickness(6, 3, 6, 3) };
    private readonly StackPanel _items = new() { Spacing = 1 };
    public event Action<string>? ComponentRequested;
    public ComponentPalette()
    {
        RowDefinitions.Add(new() { Height = new GridLength(32) }); RowDefinitions.Add(new()); Children.Add(_search);
        var scroll = new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); Children.Add(scroll);
        _search.TextChanged += (_, _) => Build(); Build();
    }
    private void Build()
    {
        _items.Children.Clear();
        foreach (var group in ComponentCatalog.All.Where(c => c.DisplayName.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) || c.Category.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).GroupBy(c => c.Category))
        {
            _items.Children.Add(new Border { Child = ControlTheme.Text("▾  " + group.Key, 11), Background = ControlTheme.Brush("#E7ECEF"), Padding = new Thickness(7, 5, 5, 5), Margin = new Thickness(0, 3, 0, 0) });
            foreach (var descriptor in group)
            {
                var button = ControlTheme.Button("  " + Icon(descriptor.Category) + "   " + descriptor.DisplayName, () => ComponentRequested?.Invoke(descriptor.Type), "palette-" + descriptor.Type);
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.BorderThickness = new Thickness(0); button.Background = ControlTheme.Brush("#F9FAFB"); button.CanDrag = true; button.FontSize = 11;
                button.DragStarting += (_, e) => { e.Data.SetText("scadaspace:" + descriptor.Type); e.Data.RequestedOperation = DataPackageOperation.Copy; };
                ToolTipService.SetToolTip(button, "Click to insert or drag onto the view"); _items.Children.Add(button);
            }
        }
    }
    private static string Icon(string category) => category switch { "Display" => "▣", "Chart" => "▥", "Input" => "⊞", "Drawing" => "◇", "Symbols" => "◉", "Alarming" => "△", _ => "▤" };
}
