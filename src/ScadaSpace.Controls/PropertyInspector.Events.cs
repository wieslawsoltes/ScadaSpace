using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

public partial class PropertyInspector
{
    /// <summary>Hosts may route this command through their shared modal coordinator.</summary>
    public event Action<string>? EventsRequested;
    public FrameworkElement? EventConfigurationButton { get; private set; }
    private bool _eventDialogOpen;

    private void BuildEventCommand(ComponentNode node)
    {
        var id = node.Id;
        var button = ControlTheme.Button("Configure Events…", async () =>
        {
            if (EventsRequested is { } handler) handler(id);
            else await ShowEventConfigurationAsync(id);
        }, "inspector-configure-events");
        button.FontSize = 11;
        AutomationProperties.SetName(button, "Configure events on " + node.Name);
        ToolTipService.SetToolTip(button, "Configure ordered local actions; Apply and Cancel do not execute actions.");
        EventConfigurationButton = button; AddFilterRow(button, "Configure Events", property: false);
    }

    private async Task ShowEventConfigurationAsync(string id)
    {
        if (_eventDialogOpen || _document is null || _host?.IsEditing != true) return;
        _eventDialogOpen = true;
        try
        {
            var draft = new EventConfigurationDraft(_document, id);
            var editor = new EventActionEditor(draft, _document.Project)
            {
                Width = Math.Min(970, Math.Max(650, XamlRoot.Size.Width - 90)),
                Height = Math.Min(560, Math.Max(350, XamlRoot.Size.Height - 240))
            };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Event Configuration on " + draft.ComponentName,
                Content = editor, PrimaryButtonText = "OK", SecondaryButtonText = "Apply", CloseButtonText = "Cancel"
            };
            dialog.Resources["ContentDialogMaxWidth"] = 1100d;
            dialog.PrimaryButtonClick += (_, args) => args.Cancel = !editor.Apply(_document);
            dialog.SecondaryButtonClick += (_, args) => { editor.Apply(_document); args.Cancel = true; };
            await dialog.ShowAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        { Error?.Invoke(ex.Message); }
        finally { _eventDialogOpen = false; }
    }
}
