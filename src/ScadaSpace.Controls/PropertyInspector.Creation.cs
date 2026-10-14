using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    private readonly Dictionary<string, FrameworkElement> _creationCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _creationOpenCommands = new(StringComparer.Ordinal);
    private ReadOnlyDictionary<string, FrameworkElement>? _creationCommandView, _creationOpenView;
    public PropertyCreationEditor? CreationEditor { get; private set; }
    public IReadOnlyDictionary<string, FrameworkElement> CreationCommands => _creationCommandView ??= new(_creationCommands);
    public IReadOnlyDictionary<string, FrameworkElement> CreationOpenCommands => _creationOpenView ??= new(_creationOpenCommands);

    private void BuildAddPropertyCommand(bool custom)
    {
        var key = custom ? "custom" : "props";
        var button = ControlTheme.Button(custom ? "+ Add Custom Property…" : "+ Add Property…", async () => await AddPropertyAsync(custom));
        button.Margin = new Thickness(5, 2, 5, 2); button.FontSize = 11;
        AutomationProperties.SetAutomationId(button, "inspector-add-" + key);
        ToolTipService.SetToolTip(button, custom ? "Add a String, Number, Boolean, Object, Array or Null property." : "Add a component property. Prefer Custom for application-specific data.");
        _creationOpenCommands[key] = button; AddFilterRow(button, "Add property", property: false);
    }
    private async Task AddPropertyAsync(bool custom)
    {
        if (CreationEditor is not null || _document is null || _id is null || _host?.IsEditing != true || XamlRoot is null) return;
        var owner = _document; var id = _id; var inspection = _inspectionVersion;
        var draft = new PropertyCreationDraft(owner, id, custom ? PropertyCreationGroup.Custom : PropertyCreationGroup.Props);
        var editor = new PropertyCreationEditor(draft) { Width = Math.Min(440, Math.Max(220, XamlRoot.Size.Width - 100)) };
        CreationEditor = editor;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = custom ? "Add Custom Property" : "Add Component Property",
            Content = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, PrimaryButtonText = "Add", CloseButtonText = "Cancel" };
        dialog.Opened += (_, _) => {
            void Visit(DependencyObject parent)
            {
                if (parent is Button b && b.Name is "PrimaryButton" or "CloseButton") _creationCommands[b.Name == "PrimaryButton" ? "add" : "cancel"] = b;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Visit(VisualTreeHelper.GetChild(parent, i));
            }
            Visit(dialog);
        };
        dialog.PrimaryButtonClick += (_, args) => {
            if (!ReferenceEquals(owner, _document) || id != _id || inspection != _inspectionVersion)
            { editor.SetError("The inspected component changed. Reopen the editor before applying."); args.Cancel = true; return; }
            args.Cancel = !editor.Apply(owner);
        };
        try { await dialog.ShowAsync(); }
        finally { CreationEditor = null; _creationCommands.Clear(); }
    }
}
