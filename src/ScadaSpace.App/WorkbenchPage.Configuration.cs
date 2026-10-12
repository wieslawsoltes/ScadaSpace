using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Controls;
using ScadaSpace.Designer;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private EventActionEditor? _eventEditor;
    private ConfigurationExplorer? _configurationExplorer;
    private readonly ConfigurationIndex _configurationIndex = new();
    private readonly Dictionary<string, FrameworkElement> _configurationButtons = new(StringComparer.Ordinal);
    private MenuFlyoutItem? _eventsMenu;

    private void InitializeConfigurationTools()
    {
        if (_commands["menu-Component"] is not MenuBarItem menu) return;
        _eventsMenu = new MenuFlyoutItem { Text = "Configure Events…", IsEnabled = false };
        _eventsMenu.Click += (_, _) => RunDialog(() => ConfigureEventsAsync());
        var explorer = new MenuFlyoutItem { Text = "Configuration Explorer" };
        explorer.Click += (_, _) => RunDialog(OpenConfigurationExplorerAsync);
        menu.Items.Insert(0, _eventsMenu); menu.Items.Insert(1, explorer); menu.Items.Insert(2, new MenuFlyoutSeparator());
        _commands["configure-events"] = _eventsMenu; _commands["configuration-explorer"] = explorer;
        _surface.ConfigureContextMenu(command =>
        {
            switch (command)
            {
                case "cut": Cut(); break; case "copy": Copy(); break; case "paste": Paste(); break;
                case "duplicate": Duplicate(); break; case "delete": Delete(); break;
                case "wrap-coordinate": WrapSelectedComponents(); break;
                case "modify-source": ModifySourceView(); break;
                case "events": RunDialog(() => ConfigureEventsAsync()); break;
                case "configurations": RunDialog(OpenConfigurationExplorerAsync); break;
            }
        });
        _surface.SelectionChanged += UpdateConfigurationCommands;
        _inspector.EventsRequested += id =>
        {
            if (_preview || _sessionMode || _document.Find(id) is null) return;
            _document.Select(id); _surface.RefreshSelection(); RunDialog(() => ConfigureEventsAsync());
        };
    }
    private void UpdateConfigurationCommands()
    {
        if (_eventsMenu is not null) _eventsMenu.IsEnabled = _document is not null && !_preview && !_sessionMode && _document.Selection.Count == 1;
    }
    private ContentDialog ConfigurationDialog(string title, FrameworkElement content, string primary, string secondary = "")
    {
        content.Width = Math.Min(970, Math.Max(650, ActualWidth - 90));
        content.Height = Math.Min(560, Math.Max(350, ActualHeight - 240));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, SecondaryButtonText = secondary, CloseButtonText = "Cancel" };
        dialog.Resources["ContentDialogMaxWidth"] = 1100d;
        dialog.Resources["ContentDialogMaxHeight"] = 900d;
        dialog.Opened += (_, _) =>
        {
            void Find(DependencyObject parent)
            {
                if (parent is Button button)
                {
                    var key = button.Name switch { "PrimaryButton" => "ok", "SecondaryButton" => "apply", "CloseButton" => "cancel", _ => null };
                    if (key is not null) _configurationButtons[key] = button;
                }
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Find(VisualTreeHelper.GetChild(parent, i));
            }
            _configurationButtons.Clear(); Find(dialog); PublishTelemetry();
        };
        return dialog;
    }
    private async Task ConfigureEventsAsync(string? initialEvent = null, int initialAction = 0)
    {
        if (_preview || _sessionMode || _document.Selection.Count != 1) { SetStatus("Select one component in Design mode to configure events."); return; }
        var draft = new EventConfigurationDraft(_document, _document.Selection.Single());
        _eventEditor = new EventActionEditor(draft, _document.Project, initialEvent, initialAction);
        var dialog = ConfigurationDialog("Event Configuration on " + draft.ComponentName, _eventEditor, "OK", "Apply");
        dialog.PrimaryButtonClick += (_, e) => e.Cancel = !_eventEditor.Apply(_document);
        dialog.SecondaryButtonClick += (_, e) => { _eventEditor.Apply(_document); e.Cancel = true; };
        try { await dialog.ShowAsync(); }
        finally { _eventEditor = null; _configurationButtons.Clear(); PublishTelemetry(); }
    }
    private async Task OpenConfigurationExplorerAsync()
    {
        if (_document is null) return;
        _configurationIndex.Refresh(_document); _configurationExplorer = new ConfigurationExplorer(_configurationIndex);
        var dialog = ConfigurationDialog("Configuration Explorer", _configurationExplorer, "Go To Reference");
        ConfigurationReference? reference = null;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            reference = _configurationExplorer.Selected;
            e.Cancel = reference is null || _document.Find(reference.ComponentId) is null;
            if (e.Cancel) SetStatus("Select a configuration that still exists in this project.");
        };
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && reference is not null)
            {
                OpenView(reference.View); _document.Select(reference.ComponentId); _surface.RefreshSelection();
                SetStatus(reference.Kind + " · " + reference.ComponentName + "." + reference.Location);
            }
        }
        finally { _configurationExplorer = null; _configurationButtons.Clear(); PublishTelemetry(); }
        if (result == ContentDialogResult.Primary && reference?.EventName is { } eventName && !_preview && !_sessionMode)
            await ConfigureEventsAsync(eventName, reference.ActionIndex ?? 0);
    }
}
