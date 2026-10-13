using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ComboBox> ChoiceEditors => _choices;
    private bool TryBuildLedChoice(ComponentNode node, string property, Scalar value, bool bound)
    {
        if (node.Type != "LEDDisplay" || value.Kind != ScalarKind.String || property is not ("segmentFormat" or "numberFormat")) return false;
        var presets = property == "segmentFormat" ? LedDisplayOptions.SegmentFormats : LedDisplayOptions.NumberFormats;
        var row = new Grid { MinHeight = 28, Background = ControlTheme.Brush("#FAFBFC") };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(88) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
        var label = ControlTheme.Text(property, 11); label.Margin = new Thickness(7, 0, 2, 0); row.Children.Add(label);
        var choice = new ComboBox
        {
            ItemsSource = presets.Append(value.Text!).Distinct(StringComparer.Ordinal).ToArray(), SelectedItem = value.Text,
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
            choice.ItemsSource = presets.Append(text).Distinct(StringComparer.Ordinal).ToArray(); choice.SelectedItem = text;
        });
        Grid.SetColumn(choice, 1); row.Children.Add(choice);
        var binding = ControlTheme.Button(bound ? "●" : "○", async () => await EditBindingAsync(property));
        binding.Foreground = ControlTheme.Brush(bound ? "#138EB6" : "#8396A2"); binding.Padding = new Thickness(2); binding.BorderThickness = new Thickness(0);
        Grid.SetColumn(binding, 2); row.Children.Add(binding); _fields.Children.Add(row); return true;
    }
}
