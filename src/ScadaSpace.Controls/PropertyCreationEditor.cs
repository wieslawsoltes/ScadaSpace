using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

/// <summary>Reusable detached typed-property form; the host decides when to commit it.</summary>
public sealed class PropertyCreationEditor : StackPanel
{
    private readonly TextBox _name = new() { Header = "Property name", Text = "customValue", MaxLength = 128 };
    private readonly ComboBox _kind = new() { Header = "Value type", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _value = new() { Header = "Initial value", MaxLength = PropertyValueText.MaximumInputLength };
    private readonly TextBlock _hint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A45837") };
    private readonly Dictionary<ScalarKind, string> _texts = new()
    { [ScalarKind.String] = "", [ScalarKind.Number] = "0", [ScalarKind.Boolean] = "false", [ScalarKind.Object] = "{}", [ScalarKind.Array] = "[]", [ScalarKind.Null] = "null" };
    public PropertyCreationDraft Draft { get; }
    public IReadOnlyDictionary<string, FrameworkElement> EditorFields { get; }
    public ScalarKind SelectedKind { get; private set; } = ScalarKind.String;
    public string ErrorText => _error.Text;
    public string ValueText => _value.Text;
    public ComboBox KindInput => _kind;

    public PropertyCreationEditor(PropertyCreationDraft draft)
    {
        Draft = draft ?? throw new ArgumentNullException(nameof(draft)); Spacing = 10;
        _kind.ItemsSource = new[] { ScalarKind.String, ScalarKind.Number, ScalarKind.Boolean, ScalarKind.Object, ScalarKind.Array, ScalarKind.Null };
        _kind.SelectedItem = ScalarKind.String;
        var scope = new TextBlock { Text = draft.ComponentName + "." + draft.Group.ToString().ToLowerInvariant(), FontSize = 12 };
        Children.Add(scope); Children.Add(_name); Children.Add(_kind); Children.Add(_value); Children.Add(_hint); Children.Add(_error);
        EditorFields = new ReadOnlyDictionary<string, FrameworkElement>(new Dictionary<string, FrameworkElement>
        { ["name"] = _name, ["kind"] = _kind, ["value"] = _value });
        foreach (var (key, field) in EditorFields) AutomationProperties.SetAutomationId(field, "property-create-" + key);
        _kind.SelectionChanged += (_, _) => {
            if (_kind.SelectedItem is not ScalarKind next || next == SelectedKind) return;
            _texts[SelectedKind] = _value.Text; SelectedKind = next; _value.Text = _texts[next];
            _error.Text = ""; UpdateKind();
        };
        UpdateKind();
    }
    private void UpdateKind()
    {
        var structured = SelectedKind is ScalarKind.Array or ScalarKind.Object;
        _value.AcceptsReturn = structured; _value.TextWrapping = structured ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _value.MinHeight = structured ? 110 : 32; _value.MaxHeight = structured ? 240 : 64;
        _value.IsReadOnly = SelectedKind == ScalarKind.Null;
        _value.Foreground = ControlTheme.Brush(PropertyValueColors.For(SelectedKind));
        _hint.Text = SelectedKind switch
        {
            ScalarKind.Object => "Enter a JSON object, for example {\"speed\": 12.5, \"enabled\": true}. Nested values are supported.",
            ScalarKind.Array => "Enter a JSON array, for example [1, true, null, {\"name\": \"Pump\"}].",
            ScalarKind.Number => "Enter a finite number using a decimal point. Full binary64 precision is preserved.",
            ScalarKind.Boolean => "Enter true or false.",
            ScalarKind.Null => "A typed null value; no initial data is stored in this property.",
            _ => "Enter literal text. Whitespace is preserved; quotation marks are not required."
        };
        AutomationProperties.SetHelpText(_value, _hint.Text);
    }
    public bool Apply(DesignerDocument document)
    {
        try { Draft.Apply(document, _name.Text, SelectedKind, _value.Text); _error.Text = ""; return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException or OverflowException)
        { SetError(ex.Message); return false; }
    }
    public void SetError(string message) => _error.Text = message;
}

internal static class PropertyValueColors
{
    // Independent shades following the documented type categories, not copied artwork.
    public static string For(ScalarKind kind) => kind switch
    {
        ScalarKind.Number => "#A65300", ScalarKind.Boolean => "#205DB0",
        ScalarKind.String => "#16733C", _ => "#586776"
    };
}
