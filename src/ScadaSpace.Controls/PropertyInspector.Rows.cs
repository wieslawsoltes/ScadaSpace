using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private readonly Dictionary<ScalarKind, SolidColorBrush> _valueBrushes = [];
    private readonly Dictionary<string, FrameworkElement> _propertyLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _bindingCommands = new(StringComparer.Ordinal);
    private ReadOnlyDictionary<string, FrameworkElement>? _propertyLabelView, _bindingCommandView;
    public IReadOnlyDictionary<string, FrameworkElement> PropertyLabels => _propertyLabelView ??= new(_propertyLabels);
    public IReadOnlyDictionary<string, FrameworkElement> BindingCommands => _bindingCommandView ??= new(_bindingCommands);
    public string ValidationMessage => _message.Text;
    public long BoundTextUpdates { get; private set; }

    private SolidColorBrush ValueBrush(ScalarKind kind)
    {
        if (!_valueBrushes.TryGetValue(kind, out var brush)) _valueBrushes[kind] = brush = ControlTheme.Brush(PropertyValueColors.For(kind));
        return brush;
    }
    private Grid BuildPropertyRow(string name, Action? binding = null, bool bound = false)
    {
        var row = new Grid { MinHeight = 28, Background = ControlTheme.Brush("#FAFBFC") };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(23) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(108) }); row.ColumnDefinitions.Add(new());
        var path = _filterSectionName + "." + name;
        var label = ControlTheme.Text(name, 11); label.Margin = new Thickness(2, 0, 4, 0);
        Grid.SetColumn(label, 1); row.Children.Add(label); ToolTipService.SetToolTip(label, path); _propertyLabels[path] = label;
        if (binding is not null)
        {
            var button = ControlTheme.Button(bound ? "●" : "○", binding);
            button.Foreground = ControlTheme.Brush(bound ? "#138EB6" : "#8396A2"); button.Padding = new Thickness(1); button.BorderThickness = new Thickness(0);
            AutomationProperties.SetName(button, "Configure binding for " + path);
            AutomationProperties.SetAutomationId(button, "inspector-binding-" + path);
            ToolTipService.SetToolTip(button, (bound ? "Edit configured binding: " : "Configure binding: ") + path);
            row.Children.Add(button); _bindingCommands[path] = button;
        }
        return row;
    }
    private void TrackBoundText(string property, Action<string, ScalarKind> apply)
    {
        QualifiedValue? previous = null;
        _live.Add(() => {
            var current = _id is null ? null : _document?.Find(_id); if (current is null || _host is null) return;
            var sample = _host.Read(current, property);
            if (previous is { } old && old.Value == sample.Value && old.Quality == sample.Quality) return;
            previous = sample; BoundTextUpdates++;
            apply(sample.IsGood ? PropertyValueText.Format(sample.Value) : sample.Quality.ToString(), sample.IsGood ? sample.Value.Kind : ScalarKind.Null);
        });
    }
}
