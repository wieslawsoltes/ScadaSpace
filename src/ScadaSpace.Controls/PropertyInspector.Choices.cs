using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ComboBox> ChoiceEditors => _choices;
    // Retain the existing flat-inspector entry point; it now delegates to shared symbol metadata.
    private bool TryBuildLedChoice(ComponentNode node, string property, Scalar value, bool bound) => TryBuildPropertyChoice(node, property, value, bound);
    private bool TryBuildPropertyChoice(ComponentNode node, string property, Scalar value, bool bound)
    {
        if (value.Kind != ScalarKind.String) return false;
        var presets = SymbolEditorOptions.Choices(node.Type, property, _document?.Project.SymbolStates);
        if (presets.Count == 0 && !(SymbolStateCatalog.IsFamily(node.Type) && property == "state")) return false;
        var row = new Grid { MinHeight = 28, Background = ControlTheme.Brush("#FAFBFC") };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(88) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
        var label = ControlTheme.Text(property, 11); label.Margin = new Thickness(7, 0, 2, 0); row.Children.Add(label); ToolTipService.SetToolTip(label, property);
        var choice = new ComboBox
        {
            ItemsSource = SymbolEditorOptions.IncludeCurrent(presets, value.Text!), SelectedItem = value.Text,
            IsEnabled = !bound, FontSize = 11, MinHeight = 27, MinWidth = 0, Padding = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = ControlTheme.Brush(bound ? "#EDF8FC" : "#FFFFFF")
        };
        AutomationProperties.SetName(choice, property); AutomationProperties.SetAutomationId(choice, "inspector-" + property);
        _choices[property] = choice; _editorFields[property] = choice;
        var owner = _document; var componentId = _id; var generation = owner?.Generation; var inspection = _inspectionVersion;
        choice.SelectionChanged += (_, _) =>
        {
            if (bound || choice.SelectedItem is not string selected || selected == value.Text || _inspectionVersion != inspection
                || !ReferenceEquals(_document, owner) || _id != componentId || owner?.Generation != generation) return;
            try { Edit("Set " + property, n => n.Props[property] = Scalar.From(selected)); _message.Text = ""; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
            { _message.Text = error.Message; Error?.Invoke(error.Message); }
        };
        if (bound) _live.Add(() =>
        {
            var current = _document?.Find(_id!); if (current is null) return;
            var sample = _host!.Read(current, property); var text = sample.IsGood ? sample.Value.ToString() : sample.Quality.ToString();
            if (Equals(choice.SelectedItem, text)) return;
            choice.ItemsSource = SymbolEditorOptions.IncludeCurrent(presets, text); choice.SelectedItem = text;
        });
        Grid.SetColumn(choice, 1); row.Children.Add(choice);
        var binding = ControlTheme.Button(bound ? "●" : "○", async () => await EditBindingAsync(property));
        binding.Foreground = ControlTheme.Brush(bound ? "#138EB6" : "#8396A2"); binding.Padding = new Thickness(2); binding.BorderThickness = new Thickness(0);
        Grid.SetColumn(binding, 2); row.Children.Add(binding); AddFilterRow(row, property, bound); return true;
    }
}
