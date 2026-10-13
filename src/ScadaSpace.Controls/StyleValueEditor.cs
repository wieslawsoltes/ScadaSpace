using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;
using Windows.UI;

namespace ScadaSpace.Controls;

/// <summary>Typed style input with an explicit inherited state and a lazily constructed color flyout.</summary>
public sealed partial class StyleValueEditor : UserControl
{
    private const string Inherit = "(inherit)";
    private readonly Grid _layout = new() { ColumnSpacing = 3 };
    private readonly TextBox? _input;
    private readonly ComboBox? _choice;
    private readonly Button? _swatch;
    private readonly Dictionary<string, FrameworkElement> _auxiliary = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, FrameworkElement> _auxiliaryView;
    private Flyout? _flyout;
    private ColorPicker? _picker;
    private TextBox? _colorText;
    private TextBlock? _colorError;
    private bool _sync, _colorSync;
    private string _text = "";
    public event Action? ValueTextChanged;
    /// <summary>Raised only when the user explicitly accepts a valid color in the flyout.</summary>
    public event Action? ColorAccepted;
    public FrameworkElement InputElement { get; }
    public IReadOnlyDictionary<string, FrameworkElement> AuxiliaryControls => _auxiliaryView;
    public StyleProperty Property { get; }
    public bool IsColorFlyoutOpen { get; private set; }
    public string ColorError => _colorError?.Text ?? "";
    public string Text
    {
        // TextChanged is dispatched later than the TextBox dependency-property update.
        // Enter/LostFocus must read the current input, not the last delivered notification.
        get => _input?.Text ?? _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value == _text && (_input is null || _input.Text == value)) return;
            if (_choice is not null && value.Length > 0 && !Options(Property.Name).Contains(value, StringComparer.Ordinal))
                throw new ArgumentException("Unknown " + Property.Name + " option.");
            _text = value; _sync = true;
            try
            {
                if (_input is not null) _input.Text = value;
                if (_choice is not null) _choice.SelectedItem = value.Length == 0 ? Inherit : value;
                UpdateSwatch();
            }
            finally { _sync = false; }
            ValueTextChanged?.Invoke();
        }
    }
    private static string[] Options(string name) => name switch
    {
        "fontWeight" => [Inherit, "Normal", "SemiBold", "Bold"],
        "italic" => [Inherit, "false", "true"],
        "textAlignment" => [Inherit, "Left", "Center", "Right"],
        _ => []
    };
    public StyleValueEditor(StyleProperty property, bool showResetButton = true)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        _auxiliaryView = new(_auxiliary);
        IsTabStop = false; Content = _layout; _layout.ColumnDefinitions.Add(new());
        var options = Options(property.Name);
        if (options.Length > 0)
        {
            _choice = new ComboBox { ItemsSource = options, SelectedIndex = 0, FontSize = 12, MinWidth = 0, MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
            _choice.SelectionChanged += (_, _) =>
            {
                if (_sync || _choice.SelectedItem is not string choice) return;
                var next = choice == Inherit ? "" : choice;
                if (next != _text) { _text = next; ValueTextChanged?.Invoke(); }
            };
            InputElement = _choice;
        }
        else
        {
            _input = new TextBox { FontSize = 12, MinHeight = 28, MinWidth = 0, Padding = new Thickness(5, 2, 5, 2), MaxLength = 128, PlaceholderText = property.Hint };
            _input.TextChanged += (_, _) =>
            {
                if (_sync || _input.Text == _text) return;
                _text = _input.Text; UpdateSwatch(); ValueTextChanged?.Invoke();
            };
            InputElement = _input;
        }
        _layout.Children.Add(InputElement);
        AutomationProperties.SetName(InputElement, property.Name);
        ToolTipService.SetToolTip(InputElement, showResetButton ? property.Hint + ". Blank / (inherit) removes this class property." : property.Hint);
        if (property.Name is "foreground" or "background" or "borderColor")
        {
            _layout.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
            _swatch = ControlTheme.Button("…", () => OpenColorPicker());
            _swatch.Padding = new Thickness(1); _swatch.MinWidth = 0; _swatch.Width = 27;
            _swatch.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(_swatch, 1); _layout.Children.Add(_swatch); Track("color", _swatch, "Choose " + property.Name + " color");
        }
        _layout.ColumnDefinitions.Add(new() { Width = new GridLength(showResetButton ? 25 : 0) });
        var reset = ControlTheme.Button("×", () => Text = ""); reset.Padding = new Thickness(1); reset.MinWidth = 0; reset.Width = 25;
        reset.HorizontalAlignment = HorizontalAlignment.Stretch; reset.Visibility = showResetButton ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(reset, _layout.ColumnDefinitions.Count - 1); _layout.Children.Add(reset); Track("reset", reset, "Inherit " + property.Name);
        UpdateSwatch();
        Unloaded += (_, _) => _flyout?.Hide();
    }
    private void Track(string id, FrameworkElement control, string title)
    {
        _auxiliary[id] = control;
        AutomationProperties.SetAutomationId(control, "style-value-" + Property.Name + "-" + id);
        AutomationProperties.SetName(control, title); ToolTipService.SetToolTip(control, title);
    }
    private static Color NativeColor(StyleColor value) => Color.FromArgb(value.Alpha, value.Red, value.Green, value.Blue);
    private static StyleColor ColorValue(Color value) => new(value.A, value.R, value.G, value.B);
    private void UpdateSwatch()
    {
        if (_swatch is null) return;
        if (StyleColor.TryParse(_text.Trim(), out var color))
        { _swatch.Background = new SolidColorBrush(NativeColor(color)); _swatch.Content = ""; }
        else { _swatch.Background = ControlTheme.Brush("#F5F6F7"); _swatch.Content = "…"; }
    }
    private void OpenColorPicker()
    {
        if (!IsEnabled || _swatch is null) return;
        if (_flyout is null) BuildColorFlyout();
        _colorSync = true;
        try
        {
            var value = StyleColor.TryParse(_text.Trim(), out var current) ? current : new StyleColor(255, 0, 0, 0);
            _picker!.Color = NativeColor(value); _colorText!.Text = value.ToString(); _colorError!.Text = "";
        }
        finally { _colorSync = false; }
        _flyout!.ShowAt(_swatch);
    }
    private void BuildColorFlyout()
    {
        _picker = new ColorPicker { IsAlphaEnabled = true, IsHexInputVisible = false, Width = 280 };
        _colorText = new TextBox { Header = "Hex (#AARRGGBB)", MaxLength = 9, FontSize = 12 };
        _colorError = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = ControlTheme.Brush("#A73828") };
        var panel = new StackPanel { Spacing = 6 }; panel.Children.Add(_picker); panel.Children.Add(_colorText); panel.Children.Add(_colorError);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var use = ControlTheme.Button("Use color", () =>
        {
            if (!StyleColor.TryParse(_colorText.Text, out var color)) { _colorError.Text = "Enter #RRGGBB or #AARRGGBB."; return; }
            Text = color.ToString(); _flyout!.Hide(); ColorAccepted?.Invoke();
        });
        var cancel = ControlTheme.Button("Cancel", () => _flyout!.Hide());
        buttons.Children.Add(use); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        _flyout = new Flyout { Content = panel };
        _flyout.Opened += (_, _) => IsColorFlyoutOpen = true;
        _flyout.Closed += (_, _) => IsColorFlyoutOpen = false;
        Track("color-picker", _picker, "Color spectrum and channels"); Track("color-text", _colorText, "ARGB color text");
        Track("color-use", use, "Use color"); Track("color-cancel", cancel, "Cancel color selection");
        _picker.ColorChanged += (_, _) =>
        {
            if (_colorSync) return;
            _colorSync = true;
            try { _colorText.Text = ColorValue(_picker.Color).ToString(); _colorError.Text = ""; }
            finally { _colorSync = false; }
        };
        _colorText.TextChanged += (_, _) =>
        {
            if (_colorSync) return;
            if (!StyleColor.TryParse(_colorText.Text, out var color)) { _colorError.Text = "Enter #RRGGBB or #AARRGGBB."; return; }
            _colorSync = true;
            try { _picker.Color = NativeColor(color); _colorError.Text = ""; }
            finally { _colorSync = false; }
        };
    }
}
