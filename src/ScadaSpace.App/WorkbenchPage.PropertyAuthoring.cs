using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Core;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private object InspectorAuthoringTelemetry(Func<FrameworkElement, object?> bounds)
    {
        var editor = _inspector.CreationEditor;
        return new
        {
            validation = _inspector.ValidationMessage, boundTextUpdates = _inspector.BoundTextUpdates,
            labels = _inspector.PropertyLabels.ToDictionary(p => p.Key, p => bounds(p.Value)),
            bindings = _inspector.BindingCommands.ToDictionary(p => p.Key, p => bounds(p.Value)),
            add = _inspector.CreationOpenCommands.ToDictionary(p => p.Key, p => bounds(p.Value)),
            fields = _inspector.QualifiedEditorFields.ToDictionary(p => p.Key, p => p.Value is TextBox box ? new
            { text = box.Text, readOnly = box.IsReadOnly, foreground = (box.Foreground as SolidColorBrush)?.Color.ToString() } : null),
            creation = editor is null ? null : new
            {
                component = editor.Draft.ComponentName, group = editor.Draft.Group.ToString(),
                kind = editor.SelectedKind.ToString(), text = editor.ValueText, error = editor.ErrorText,
                fields = editor.EditorFields.ToDictionary(p => p.Key, p => bounds(p.Value)),
                commands = _inspector.CreationCommands.ToDictionary(p => p.Key, p => bounds(p.Value)),
                options = editor.KindInput.IsDropDownOpen ? ((IEnumerable<ScalarKind>)editor.KindInput.ItemsSource).Select((kind, i) => new
                { name = kind.ToString(), bounds = editor.KindInput.ContainerFromIndex(i) is FrameworkElement item ? bounds(item) : null }).ToArray() : null
            }
        };
    }
}
