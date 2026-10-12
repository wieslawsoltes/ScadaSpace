using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Controls;
using ScadaSpace.Designer;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private SymbolLibrary? _symbolLibrary;
    private SymbolStateEditor? _symbolStateEditor;
    private object? SymbolLibraryTelemetry(Func<FrameworkElement, object?> bounds)
    {
        BrowserServices.PublishSymbolStateEditor(StateEditorTelemetry(bounds));
        if (_symbolLibrary is null) return null;
        var menus = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, field) in _symbolLibrary.Fields)
            if (field is ComboBox { IsDropDownOpen: true } combo)
                menus[key] = ((IEnumerable<string>)combo.ItemsSource).Select((name, i) => new { name, bounds = combo.ContainerFromIndex(i) is FrameworkElement row ? bounds(row) : null }).ToArray();
        return new { fields = _symbolLibrary.Fields.ToDictionary(p => p.Key, p => bounds(p.Value)), symbols = _symbolLibrary.Symbols.ToDictionary(p => p.Key, p => BrowserServices.ObserveSymbol(p.Value)), defaults = _symbolLibrary.Defaults, menus };
    }
    private object? StateEditorTelemetry(Func<FrameworkElement, object?> bounds)
    {
        if (_symbolStateEditor is not { } editor) return null;
        var list = (ListView)editor.Fields["states"]; var theme = (ComboBox)editor.Fields["theme"];
        return new
        {
            fields = editor.Fields.ToDictionary(p => p.Key, p => bounds(p.Value)),
            selected = editor.SelectedState, theme = editor.ThemeName, error = editor.ErrorText, dirty = editor.Draft.IsDirty,
            previewVisible = editor.PreviewVisible,
            names = editor.Draft.Names, state = editor.Draft.Catalog.Get(editor.SelectedState), deleteConfirmation = editor.DeleteConfirmationVisible,
            rows = ((IEnumerable<string>)list.ItemsSource).Select(name => new { name, bounds = list.ContainerFromItem(name) is FrameworkElement row ? bounds(row) : null }).ToArray(),
            themeMenu = theme.IsDropDownOpen ? ((IEnumerable<string>)theme.ItemsSource).Select((name, i) => new { name, bounds = theme.ContainerFromIndex(i) is FrameworkElement row ? bounds(row) : null }).ToArray() : null,
            previews = editor.Previews.ToDictionary(p => p.Key, p => BrowserServices.ObserveSymbol(p.Value)),
            colors = editor.ColorInputs.ToDictionary(p => p.Key, p => new { text = p.Value.Text, open = p.Value.IsColorFlyoutOpen, error = p.Value.ColorError,
                controls = p.Value.AuxiliaryControls.ToDictionary(c => c.Key, c => bounds(c.Value)) })
        };
    }
    private void InitializeSymbolTools()
    {
        if (_commands["menu-Tools"] is MenuBarItem tools)
        {
            var entry = new MenuFlyoutItem { Text = "Symbol Library…" };
            entry.Click += (_, _) => RunDialog(OpenSymbolLibraryAsync); tools.Items.Add(entry); _commands["symbol-library"] = entry;
        }
        if (_commands["menu-Project"] is MenuBarItem project)
        {
            var entry = new MenuFlyoutItem { Text = "Symbol States…" };
            entry.Click += (_, _) => RunDialog(OpenSymbolStatesAsync); project.Items.Add(entry); _commands["symbol-states"] = entry;
        }
    }
    private async Task OpenSymbolStatesAsync()
    {
        if (_document is null || _preview || _sessionMode) { SetStatus("Switch to Design mode to configure project symbol states."); return; }
        _symbolStateEditor = new(new SymbolStateDraft(_document));
        var dialog = ConfigurationDialog("Project Properties · Symbols", _symbolStateEditor, "OK", "Apply");
        _symbolStateEditor.Height = Math.Min(710, Math.Max(350, ActualHeight - 235));
        dialog.PrimaryButtonClick += (_, e) => e.Cancel = !_symbolStateEditor.Apply(_document);
        dialog.SecondaryButtonClick += (_, e) => { _symbolStateEditor.Apply(_document); e.Cancel = true; };
        try { await dialog.ShowAsync(); }
        finally { _symbolStateEditor.Dispose(); _symbolStateEditor = null; _configurationButtons.Clear(); PublishTelemetry(); }
    }
    private async Task OpenSymbolLibraryAsync()
    {
        if (_session is null) return;
        _symbolLibrary = new(_session.Symbols, _runtime.Project.SymbolStates);
        var dialog = ConfigurationDialog("Industrial Symbol Library", _symbolLibrary, "Use session defaults"); dialog.CloseButtonText = "Close";
        dialog.PrimaryButtonClick += (_, _) => { _session.Symbols = _symbolLibrary.Defaults; ActiveHost.Refresh(); SetStatus("Symbol session defaults updated · project definitions are unchanged"); };
        try { await dialog.ShowAsync(); }
        finally { _symbolLibrary.Dispose(); _symbolLibrary = null; _configurationButtons.Clear(); PublishTelemetry(); }
    }
}
