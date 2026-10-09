using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ScadaSpace.Controls;

public static class ControlTheme
{
    public static SolidColorBrush Brush(string color)
    {
        var c = ScadaSpace.Graphics.CanvasPainter.Color(color);
        return new(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
    }
    public static TextBlock Text(string text, double size = 12, string color = "#344A58") => new() { Text = text, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    public static Button Button(string text, Action action, string? id = null)
    {
        var button = new Button { Content = text, FontSize = 12, Padding = new Thickness(9, 3, 9, 3), MinWidth = 0, MinHeight = 28, CornerRadius = new CornerRadius(2), Background = Brush("#F5F6F7"), BorderBrush = Brush("#BAC4CB"), BorderThickness = new Thickness(1) };
        if (id is not null) AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action();
        return button;
    }
    public static Border Frame(UIElement content, string background = "#FFFFFF", double padding = 0) => new() { Child = content, Background = Brush(background), BorderBrush = Brush("#B8C2C8"), BorderThickness = new Thickness(1), Padding = new Thickness(padding) };
}
