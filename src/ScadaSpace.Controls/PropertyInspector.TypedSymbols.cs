using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private readonly Dictionary<string, StyleValueEditor> _colorEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _switchEditors = new(StringComparer.Ordinal);
    private ReadOnlyDictionary<string, StyleValueEditor>? _colorView;
    private ReadOnlyDictionary<string, CheckBox>? _switchView;
    public IReadOnlyDictionary<string, StyleValueEditor> ColorEditors => _colorView ??= new(_colorEditors);
    public IReadOnlyDictionary<string, CheckBox> SwitchEditors => _switchView ??= new(_switchEditors);

    private bool TryBuildTypedSymbolProperty(ComponentNode node, string property, Scalar value, bool bound)
    {
        var color = value.Kind == ScalarKind.String && SymbolPropertyKinds.IsColor(node.Type, property);
        var flag = value.Kind == ScalarKind.Boolean && SymbolPropertyKinds.IsSwitch(node.Type, property);
        if (!color && !flag) return false;

        var row = new Grid { MinHeight = 28, Background = ControlTheme.Brush("#FAFBFC") };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(88) });
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
        var label = ControlTheme.Text(property, 11); label.Margin = new Thickness(7, 0, 2, 0);
        ToolTipService.SetToolTip(label, property); row.Children.Add(label);
        var owner = _document; var componentId = _id; var generation = owner?.Generation; var inspection = _inspectionVersion;
        bool Current() => !bound && owner is not null && _inspectionVersion == inspection
            && ReferenceEquals(_document, owner) && _id == componentId && owner.Generation == generation;

        FrameworkElement input;
        if (color)
        {
            var editor = new StyleValueEditor(new("background", "Symbol", ScalarKind.String, "#RRGGBB or #AARRGGBB"), showResetButton: false)
            { Text = value.Text!, IsEnabled = !bound };
            var textBox = (TextBox)editor.InputElement;
            textBox.FontSize = 11; textBox.MinHeight = 27; textBox.IsReadOnly = bound;
            AutomationProperties.SetName(textBox, property);
            ToolTipService.SetToolTip(editor.AuxiliaryControls["color"], "Choose " + property);
            _colorEditors[property] = editor; _editorFields[property] = textBox;
            var original = value.Text!;
            void Apply()
            {
                if (!Current()) return;
                try
                {
                    var text = editor.Text.Trim(); var parsed = StyleColor.Parse(text);
                    if (StyleColor.TryParse(original, out var previous) && previous == parsed)
                    { editor.Text = original; _message.Text = ""; return; }
                    Edit("Set " + property, n => n.Props[property] = Scalar.From(text));
                    original = text; _message.Text = "";
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
                { editor.Text = original; _message.Text = error.Message; Error?.Invoke(error.Message); }
            }
            textBox.LostFocus += (_, _) => Apply();
            textBox.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Apply(); e.Handled = true; } };
            editor.ColorAccepted += Apply;
            if (bound) _live.Add(() =>
            {
                var current = _document?.Find(_id!); if (current is null) return;
                var sample = _host!.Read(current, property);
                editor.Text = sample.IsGood ? sample.Value.ToString() : sample.Quality.ToString();
            });
            input = editor;
        }
        else
        {
            var toggle = new CheckBox { IsChecked = value.Boolean, IsEnabled = !bound, MinHeight = 27, Padding = new Thickness(4, 0, 4, 0), FontSize = 11 };
            AutomationProperties.SetName(toggle, property); _switchEditors[property] = toggle; _editorFields[property] = toggle;
            void Apply()
            {
                if (!Current() || toggle.IsChecked is not bool next || next == value.Boolean) return;
                try { Edit("Set " + property, n => n.Props[property] = Scalar.From(next)); _message.Text = ""; }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
                { toggle.IsChecked = value.Boolean; _message.Text = error.Message; Error?.Invoke(error.Message); }
            }
            toggle.Checked += (_, _) => Apply(); toggle.Unchecked += (_, _) => Apply();
            if (bound) _live.Add(() =>
            {
                var current = _document?.Find(_id!); if (current is null) return;
                var sample = _host!.Read(current, property);
                toggle.IsChecked = sample.IsGood && sample.Value.Kind == ScalarKind.Boolean ? sample.Value.Boolean : null;
                AutomationProperties.SetHelpText(toggle, sample.IsGood ? sample.Value.ToString() : sample.Quality.ToString());
            });
            input = toggle;
        }
        AutomationProperties.SetAutomationId(input, "inspector-" + property);
        Grid.SetColumn(input, 1); row.Children.Add(input);
        var binding = ControlTheme.Button(bound ? "●" : "○", async () => await EditBindingAsync(property));
        binding.Foreground = ControlTheme.Brush(bound ? "#138EB6" : "#8396A2"); binding.Padding = new Thickness(2); binding.BorderThickness = new Thickness(0);
        Grid.SetColumn(binding, 2); row.Children.Add(binding); AddFilterRow(row, property, bound); return true;
    }
}
