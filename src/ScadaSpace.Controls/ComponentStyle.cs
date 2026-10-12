using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using Windows.UI.Text;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace ScadaSpace.Controls;

/// <summary>Local style adapter shared by runtime controls and the style preview. Not a CSS engine.</summary>
public static class ComponentStyle
{
    public static FrameworkElement Apply(FrameworkElement element, IReadOnlyDictionary<string, Scalar> values)
    {
        StyleRules.ValidateResolvedValues(values);
        ApplyText(element, values);
        if (element is Control control)
        {
            if (values.TryGetValue("background", out var background)) control.Background = ControlTheme.Brush(background.Text!);
            if (values.TryGetValue("borderColor", out var borderColor)) control.BorderBrush = ControlTheme.Brush(borderColor.Text!);
            if (values.TryGetValue("borderWidth", out var width)) control.BorderThickness = new Thickness(width.Number);
            if (values.TryGetValue("padding", out var padding)) control.Padding = new Thickness(padding.Number);
            if (control is Button button && values.TryGetValue("cornerRadius", out var radius)) button.CornerRadius = new CornerRadius(radius.Number);
            if (values.TryGetValue("opacity", out var opacity)) control.Opacity = opacity.Number;
            return element;
        }
        if (values.Keys.Any(k => k is "background" or "borderColor" or "borderWidth" or "cornerRadius" or "padding"))
        {
            var frame = new Border { Child = element, IsHitTestVisible = element.IsHitTestVisible };
            ApplyFrame(frame, values); return frame;
        }
        if (values.TryGetValue("opacity", out var alpha)) element.Opacity = alpha.Number;
        return element;
    }
    public static void ApplyText(FrameworkElement element, IReadOnlyDictionary<string, Scalar> values)
    {
        if (element is TextBlock text)
        {
            if (values.TryGetValue("foreground", out var color)) text.Foreground = ControlTheme.Brush(color.Text!);
            if (values.TryGetValue("fontSize", out var size)) text.FontSize = size.Number;
            if (values.TryGetValue("fontWeight", out var weight)) text.FontWeight = Weight(weight.Text!);
            if (values.TryGetValue("italic", out var italic)) text.FontStyle = italic.Boolean ? FontStyle.Italic : FontStyle.Normal;
            if (values.TryGetValue("textAlignment", out var align)) text.TextAlignment = Enum.Parse<TextAlignment>(align.Text!);
        }
        else if (element is Control control)
        {
            if (values.TryGetValue("foreground", out var color)) control.Foreground = ControlTheme.Brush(color.Text!);
            if (values.TryGetValue("fontSize", out var size)) control.FontSize = size.Number;
            if (values.TryGetValue("fontWeight", out var weight)) control.FontWeight = Weight(weight.Text!);
            if (values.TryGetValue("italic", out var italic)) control.FontStyle = italic.Boolean ? FontStyle.Italic : FontStyle.Normal;
        }
    }
    public static void ApplyFrame(Border frame, IReadOnlyDictionary<string, Scalar> values)
    {
        frame.Background = values.TryGetValue("background", out var background) ? ControlTheme.Brush(background.Text!) : null;
        frame.BorderBrush = values.TryGetValue("borderColor", out var border) ? ControlTheme.Brush(border.Text!) : ControlTheme.Brush("#D5E0E5");
        // Preserve the previous background-frame default while supporting border-only styles.
        frame.BorderThickness = new Thickness(values.GetValueOrDefault("borderWidth", Scalar.From(values.ContainsKey("background") || values.ContainsKey("borderColor") ? 1 : 0)).Number);
        frame.CornerRadius = new CornerRadius(values.GetValueOrDefault("cornerRadius").Number);
        frame.Padding = new Thickness(values.GetValueOrDefault("padding").Number);
        frame.Opacity = values.GetValueOrDefault("opacity", Scalar.From(1)).Number;
    }
    private static FontWeight Weight(string value) => value switch { "Bold" => FontWeights.Bold, "SemiBold" => FontWeights.SemiBold, _ => FontWeights.Normal };
}
