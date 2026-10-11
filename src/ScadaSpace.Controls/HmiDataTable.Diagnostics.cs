using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ScadaSpace.Controls;

public partial class HmiDataTable
{
    private Border? _errorPanel;
    private TextBlock? _errorText;
    public string? DataError { get; private set; }
    /// <summary>Displays a native, accessible diagnostic and prevents input against failed data.</summary>
    public void SetDataError(string? message)
    {
        if (DataError == message) return;
        DataError = message;
        if (message is not null && _errorPanel is null)
        {
            _errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ControlTheme.Brush("#A14632") };
            _errorPanel = new Border { Child = _errorText, Padding = new Thickness(12), Background = ControlTheme.Brush("#FFF5F0") };
            AutomationProperties.SetName(_errorPanel, "Table data error");
            Grid.SetRow(_errorPanel, 1); Children.Add(_errorPanel);
        }
        if (_errorPanel is not null)
        {
            _errorPanel.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
            _errorText!.Text = message ?? "";
        }
        AutomationProperties.SetHelpText(this, message ?? "");
        _surface.IsHitTestVisible = message is null;
        _filter.IsEnabled = _commands.IsEnabled = message is null;
    }
}
