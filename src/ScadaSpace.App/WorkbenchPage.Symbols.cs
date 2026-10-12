using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Controls;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private SymbolLibrary? _symbolLibrary;
    private object? SymbolLibraryTelemetry(Func<FrameworkElement, object?> bounds)
    {
        if (_symbolLibrary is null) return null;
        var menus = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, field) in _symbolLibrary.Fields)
            if (field is ComboBox { IsDropDownOpen: true } combo)
                menus[key] = ((IEnumerable<string>)combo.ItemsSource).Select((name, i) => new { name, bounds = combo.ContainerFromIndex(i) is FrameworkElement row ? bounds(row) : null }).ToArray();
        return new { fields = _symbolLibrary.Fields.ToDictionary(p => p.Key, p => bounds(p.Value)), symbols = _symbolLibrary.Symbols.ToDictionary(p => p.Key, p => BrowserServices.ObserveSymbol(p.Value)), defaults = _symbolLibrary.Defaults, menus };
    }
    private void InitializeSymbolTools()
    {
        if (_commands["menu-Tools"] is not MenuBarItem tools) return;
        var entry = new MenuFlyoutItem { Text = "Symbol Library…" };
        entry.Click += (_, _) => RunDialog(OpenSymbolLibraryAsync); tools.Items.Add(entry); _commands["symbol-library"] = entry;
    }
    private async Task OpenSymbolLibraryAsync()
    {
        if (_session is null) return;
        _symbolLibrary = new(_session.Symbols);
        var dialog = ConfigurationDialog("Industrial Symbol Library", _symbolLibrary, "Use session defaults"); dialog.CloseButtonText = "Close";
        dialog.PrimaryButtonClick += (_, _) => { _session.Symbols = _symbolLibrary.Defaults; ActiveHost.Refresh(); SetStatus("Symbol session defaults updated · project definitions are unchanged"); };
        try { await dialog.ShowAsync(); }
        finally { _symbolLibrary.Dispose(); _symbolLibrary = null; _configurationButtons.Clear(); PublishTelemetry(); }
    }
}
