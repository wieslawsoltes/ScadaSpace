using Microsoft.UI.Xaml;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    // Passive native-control observations. The browser drives ordinary input, never managed commands.
    private object TypedInspectorTelemetry(Func<FrameworkElement, object?> bounds) => new
    {
        colors = _inspector.ColorEditors.ToDictionary(p => p.Key, p => new
        {
            text = p.Value.Text, enabled = p.Value.IsEnabled, open = p.Value.IsColorFlyoutOpen, error = p.Value.ColorError,
            controls = p.Value.AuxiliaryControls.ToDictionary(c => c.Key, c => bounds(c.Value))
        }),
        switches = _inspector.SwitchEditors.ToDictionary(p => p.Key, p => new
        { value = p.Value.IsChecked, enabled = p.Value.IsEnabled, bounds = bounds(p.Value) })
    };
}
